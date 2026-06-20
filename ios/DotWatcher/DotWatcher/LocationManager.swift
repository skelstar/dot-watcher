import CoreLocation
import Foundation
import Security

struct AppUser: Codable {
    let userId: String
    let username: String
    let displayName: String
}

struct AuthSession: Codable {
    let accessToken: String
    let user: AppUser
}

struct SessionMembership: Codable, Identifiable {
    var id: String { sessionCode }

    let sessionCode: String
    let inviteCode: String
    let role: String
    let displayName: String
}

private struct LocationPostResponse: Codable {
    let participants: [String]
}

enum DotWatcherAPIError: LocalizedError {
    case missingToken
    case badResponse(Int)
    case network

    var errorDescription: String? {
        switch self {
        case .missingToken:
            return "Sign in required."
        case .badResponse(let status):
            return "HTTP \(status)"
        case .network:
            return "Network error."
        }
    }
}

@Observable
@MainActor
final class LocationManager {
    private static let tokenAccount = "DotWatcherUserAccessToken"

    private(set) var status = "Idle"
    private(set) var lastSent: Date?
    private(set) var isTracking = false
    private(set) var currentUser: AppUser?
    private(set) var memberships: [SessionMembership] = []

    var participants: [String] = []
    private var lastParticipantCount = 0
    fileprivate var latestLocation: CLLocation?

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?
    private var accessToken: String?

    let serverBaseURL = URL(string: "http://dot-watcher.skelstar.io/api")!
    var sessionCode = UserDefaults.standard.string(forKey: "sessionCode") ?? "" {
        didSet {
            UserDefaults.standard.set(sessionCode, forKey: "sessionCode")
            participants = []
            lastParticipantCount = 0
            if isTracking { captureAndPost() }
        }
    }

    var runnerName: String = UserDefaults.standard.string(forKey: "runnerName") ?? "" {
        didSet { UserDefaults.standard.set(runnerName, forKey: "runnerName") }
    }

    let interval: TimeInterval = 15

    var isAuthenticated: Bool { accessToken != nil }

    var activeMembership: SessionMembership? {
        memberships.first { $0.sessionCode == sessionCode }
    }

    var canTrackSelectedSession: Bool {
        activeMembership?.role == "owner" || activeMembership?.role == "runner"
    }

    var dateSuffix: String {
        let cal = Calendar.current
        let now = Date()
        let day = cal.component(.day, from: now)
        let month = cal.component(.month, from: now)
        return String(format: "-%02d%02d", day, month)
    }

    var fullSessionName: String { sessionCode }

    init() {
        accessToken = Self.readToken()
        if let data = UserDefaults.standard.data(forKey: "currentUser"),
           let user = try? JSONDecoder().decode(AppUser.self, from: data) {
            currentUser = user
        }

        locationDelegate.owner = self
        clManager.delegate = locationDelegate
        clManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
        clManager.distanceFilter = 25.0
        clManager.activityType = .fitness
        clManager.pausesLocationUpdatesAutomatically = true
        clManager.allowsBackgroundLocationUpdates = true
        clManager.showsBackgroundLocationIndicator = true
    }

    func signIn(username: String, password: String) async throws {
        try await authenticate(path: "/auth/login", body: [
            "username": username,
            "password": password,
        ])
    }

    func register(username: String, password: String, displayName: String) async throws {
        try await authenticate(path: "/auth/register", body: [
            "username": username,
            "password": password,
            "displayName": displayName,
        ])
    }

    func signOut() {
        stop()
        accessToken = nil
        currentUser = nil
        memberships = []
        sessionCode = ""
        Self.storeToken(nil)
        UserDefaults.standard.removeObject(forKey: "currentUser")
        status = "Signed out"
    }

    func loadSessions() async {
        guard isAuthenticated else { return }
        do {
            memberships = try await send(path: "/me/sessions")
            if !sessionCode.isEmpty && activeMembership == nil {
                sessionCode = ""
            }
        } catch DotWatcherAPIError.badResponse(401) {
            signOut()
            status = "Sign in required"
        } catch {
            status = error.localizedDescription
        }
    }

    func createSession(code: String, displayName: String?) async throws {
        let requestedCode = code.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        let name = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let membership: SessionMembership = try await send(
            path: "/sessions",
            method: "POST",
            body: [
                "sessionCode": requestedCode.isEmpty ? NSNull() : requestedCode,
                "displayName": (name?.isEmpty ?? true) ? NSNull() : name!,
            ])
        upsertMembership(membership)
        selectSession(membership)
    }

    func joinInvite(code: String, displayName: String?) async throws {
        let inviteCode = code.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        let name = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let membership: SessionMembership = try await send(
            path: "/session-invites/\(inviteCode)/join",
            method: "POST",
            body: [
                "displayName": (name?.isEmpty ?? true) ? NSNull() : name!,
            ])
        upsertMembership(membership)
        selectSession(membership)
    }

    func selectSession(_ membership: SessionMembership) {
        sessionCode = membership.sessionCode
        status = membership.role == "viewer" ? "Viewer only" : "Ready"
        Task { participants = await previewSession(membership.sessionCode) }
    }

    func start() {
        guard !isTracking else { return }
        guard isAuthenticated else {
            status = "Sign in required"
            return
        }
        guard !sessionCode.isEmpty else {
            status = "Choose session"
            return
        }
        guard canTrackSelectedSession else {
            status = "Owner/runner required"
            return
        }

        clManager.requestAlwaysAuthorization()
        clManager.startUpdatingLocation()
        isTracking = true
        status = "Tracking..."
        trackingTask = Task { [weak self] in await self?.trackingLoop() }
        captureAndPost()
    }

    func stop() {
        trackingTask?.cancel()
        trackingTask = nil
        clManager.stopUpdatingLocation()
        isTracking = false
        status = "Stopped"
    }

    private func trackingLoop() async {
        while !Task.isCancelled {
            let now = Date().timeIntervalSince1970
            let delay = ((floor(now / interval) + 1) * interval) - now
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled else { break }
            captureAndPost()
        }
    }

    func previewSession(_ sessionName: String) async -> [String] {
        do {
            let groups: [[RunnerPositionResponse]] = try await send(path: "/locations/\(sessionName)")
            return groups.compactMap { $0.first?.runnerName }
        } catch {
            return []
        }
    }

    private func captureAndPost() {
        guard let loc = latestLocation else {
            status = "Waiting for GPS"
            return
        }

        let heading: Double? = loc.course >= 0 ? loc.course : nil
        Task {
            await post(
                lat: loc.coordinate.latitude,
                lon: loc.coordinate.longitude,
                heading: heading,
                timestamp: loc.timestamp)
        }
    }

    private func post(lat: Double, lon: Double, heading: Double?, timestamp: Date) async {
        do {
            var body: [String: Any] = [
                "runnerName": runnerName,
                "sessionCode": fullSessionName,
                "latitude": lat,
                "longitude": lon,
                "timestamp": ISO8601DateFormatter().string(from: timestamp),
            ]
            if let heading { body["heading"] = heading }

            let response: LocationPostResponse = try await send(path: "/location", method: "POST", body: body)
            status = "Sent"
            lastSent = Date()
            if response.participants.count != lastParticipantCount {
                lastParticipantCount = response.participants.count
                participants = response.participants
            }
        } catch {
            status = error.localizedDescription
        }
    }

    private func authenticate(path: String, body: [String: Any]) async throws {
        let session: AuthSession = try await send(path: path, method: "POST", body: body, authorized: false)
        accessToken = session.accessToken
        currentUser = session.user
        Self.storeToken(session.accessToken)
        if let data = try? JSONEncoder().encode(session.user) {
            UserDefaults.standard.set(data, forKey: "currentUser")
        }
        if runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
            runnerName = String(session.user.displayName.uppercased().prefix(3))
        }
        status = "Signed in"
        await loadSessions()
    }

    private func send<T: Decodable>(
        path: String,
        method: String = "GET",
        body: [String: Any]? = nil,
        authorized: Bool = true
    ) async throws -> T {
        let normalizedPath = path.hasPrefix("/") ? path : "/\(path)"
        guard let url = URL(string: serverBaseURL.absoluteString + normalizedPath) else {
            throw DotWatcherAPIError.network
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        if authorized {
            guard let accessToken else { throw DotWatcherAPIError.missingToken }
            request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        }
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONSerialization.data(withJSONObject: body)
        }

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            let statusCode = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard (200..<300).contains(statusCode) else {
                throw DotWatcherAPIError.badResponse(statusCode)
            }
            return try JSONDecoder().decode(T.self, from: data)
        } catch let error as DotWatcherAPIError {
            throw error
        } catch {
            throw DotWatcherAPIError.network
        }
    }

    private func upsertMembership(_ membership: SessionMembership) {
        memberships.removeAll { $0.sessionCode == membership.sessionCode }
        memberships.insert(membership, at: 0)
    }

    private static func readToken() -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: tokenAccount,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
              let data = item as? Data
        else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private static func storeToken(_ token: String?) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: tokenAccount,
        ]
        SecItemDelete(query as CFDictionary)
        guard let token,
              let data = token.data(using: .utf8)
        else { return }
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: tokenAccount,
            kSecValueData as String: data,
        ]
        SecItemAdd(item as CFDictionary, nil)
    }
}

private struct RunnerPositionResponse: Codable {
    let runnerName: String
    let latitude: Double
    let longitude: Double
    let heading: Double?
    let timestamp: String
}

private final class LocationDelegate: NSObject, CLLocationManagerDelegate {
    weak var owner: LocationManager?

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        Task { @MainActor [weak self] in self?.owner?.latestLocation = loc }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {}
}
