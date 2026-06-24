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
    var id: String { sessionId }

    let sessionId: String
    let sessionName: String
    let inviteCode: String
    let role: String
    let displayName: String
}

struct SessionMember: Codable, Identifiable {
    var id: String { userId }

    let userId: String
    let role: String
    let displayName: String
}

struct BrowsableSession: Codable, Identifiable {
    var id: String { sessionId }
    let sessionId: String
    let sessionName: String
    let ownerDisplayName: String
    let memberCount: Int
    let createdAt: String
}

struct JoinRequest: Codable, Identifiable {
    var id: String { requestId }
    let requestId: String
    let sessionId: String
    let userId: String
    let displayName: String
    let createdAt: String

    var formattedDate: String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: createdAt) { return d.formatted(date: .abbreviated, time: .shortened) }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: createdAt)?.formatted(date: .abbreviated, time: .shortened) ?? createdAt
    }
}

private struct LocationPostResponse: Codable {
    let participants: [String]
    let pendingJoinRequests: Int?
}

private struct ServerErrorBody: Decodable {
    let error: String
}

enum DotWatcherAPIError: LocalizedError {
    case missingToken
    case badResponse(Int, String?)
    case network

    var errorDescription: String? {
        switch self {
        case .missingToken:
            return "Sign in required."
        case .badResponse(_, let message):
            return message ?? "Something went wrong. Please try again."
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
    private(set) var selectedSessionMembers: [SessionMember] = []
    private(set) var pendingJoinRequestCount = 0
    private(set) var pendingJoinRequests: [JoinRequest] = []

    var participants: [String] = []
    private var lastParticipantCount = 0
    fileprivate var latestLocation: CLLocation?

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?
    private var accessToken: String?

    let serverBaseURL = LocationManager.configuredServerBaseURL()
    var sessionId = UserDefaults.standard.string(forKey: "sessionId") ?? "" {
        didSet {
            UserDefaults.standard.set(sessionId, forKey: "sessionId")
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
        memberships.first { $0.sessionId == sessionId }
    }

    var canTrackSelectedSession: Bool {
        activeMembership?.role == "owner" || activeMembership?.role == "runner"
    }


    init() {
        accessToken = Self.readToken()
        if let data = UserDefaults.standard.data(forKey: "currentUser"),
           let user = try? JSONDecoder().decode(AppUser.self, from: data) {
            currentUser = user
        }

        locationDelegate.owner = self
        clManager.delegate = locationDelegate
        clManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
        clManager.distanceFilter = 10.0
        clManager.activityType = .fitness
        clManager.pausesLocationUpdatesAutomatically = false
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

    func signOut(status nextStatus: String = "Signed out") async {
        stop()
        let tokenToRevoke = accessToken
        status = "Signing out..."
        if let tokenToRevoke {
            await revokeToken(tokenToRevoke)
        }
        clearAuthState(status: nextStatus)
    }

    func deleteAccount() async throws {
        stop()
        guard let token = accessToken else { throw DotWatcherAPIError.missingToken }
        status = "Deleting account..."
        try await sendEmpty(path: "/me", method: "DELETE", token: token)
        clearAuthState(status: "Account deleted")
    }

    private func clearAuthState(status nextStatus: String) {
        accessToken = nil
        currentUser = nil
        memberships = []
        selectedSessionMembers = []
        pendingJoinRequests = []
        pendingJoinRequestCount = 0
        sessionId = ""
        Self.storeToken(nil)
        UserDefaults.standard.removeObject(forKey: "currentUser")
        status = nextStatus
    }

    func loadSessions() async {
        guard isAuthenticated else { return }
        do {
            memberships = try await send(path: "/me/sessions")
            if !sessionId.isEmpty && activeMembership == nil {
                sessionId = ""
            }
            if sessionId.isEmpty, let owned = memberships.first(where: { $0.role == "owner" }) {
                sessionId = owned.sessionId
                status = "Ready"
                Task { participants = await previewSession(owned.sessionId) }
            }
            if activeMembership?.role == "owner" {
                await loadSelectedSessionMembers()
            } else {
                selectedSessionMembers = []
            }
        } catch DotWatcherAPIError.badResponse(401, _) {
            await signOut(status: "Sign in required")
        } catch {
            status = error.localizedDescription
        }
    }

    func createSession(name: String, displayName: String?) async throws {
        let requestedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let dispName = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        let membership: SessionMembership = try await send(
            path: "/sessions",
            method: "POST",
            body: [
                "sessionName": requestedName.isEmpty ? NSNull() : requestedName,
                "displayName": (dispName?.isEmpty ?? true) ? NSNull() : dispName!,
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
        sessionId = membership.sessionId
        status = membership.role == "viewer" ? "Viewer only" : "Ready"
        Task { participants = await previewSession(membership.sessionId) }
        Task { await loadSelectedSessionMembers() }
    }

    func loadSelectedSessionMembers() async {
        guard let membership = activeMembership, membership.role == "owner" else {
            selectedSessionMembers = []
            pendingJoinRequests = []
            pendingJoinRequestCount = 0
            return
        }

        do {
            selectedSessionMembers = try await send(path: "/sessions/\(membership.sessionId)/members")
        } catch DotWatcherAPIError.badResponse(401, _) {
            await signOut(status: "Sign in required")
        } catch {
            status = error.localizedDescription
        }
        await loadJoinRequests()
    }

    func updateMemberRole(_ member: SessionMember, role: String) async throws {
        guard let membership = activeMembership, membership.role == "owner" else {
            throw DotWatcherAPIError.badResponse(403, nil)
        }

        let updated: SessionMember = try await send(
            path: "/sessions/\(membership.sessionId)/members/\(member.userId)/role",
            method: "POST",
            body: ["role": role])
        if let index = selectedSessionMembers.firstIndex(where: { $0.userId == updated.userId }) {
            selectedSessionMembers[index] = updated
        }
    }

    func deleteSession(sessionId code: String) async throws {
        guard let token = accessToken else { throw DotWatcherAPIError.missingToken }
        try await sendEmpty(path: "/me/sessions/\(code)", method: "DELETE", token: token)
        memberships.removeAll { $0.sessionId == code }
        if sessionId == code {
            sessionId = ""
            participants = []
            selectedSessionMembers = []
            pendingJoinRequests = []
            pendingJoinRequestCount = 0
            status = "Idle"
        }
    }

    func browseSessions() async throws -> [BrowsableSession] {
        try await send(path: "/sessions/browse")
    }

    func requestToJoin(sessionId code: String, displayName: String?) async throws -> JoinRequest {
        let name = displayName?.trimmingCharacters(in: .whitespacesAndNewlines)
        return try await send(
            path: "/sessions/\(code)/join-requests",
            method: "POST",
            body: ["displayName": (name?.isEmpty ?? true) ? NSNull() : name!])
    }

    func loadJoinRequests() async {
        guard let membership = activeMembership, membership.role == "owner" else {
            pendingJoinRequests = []
            return
        }
        do {
            pendingJoinRequests = try await send(path: "/sessions/\(membership.sessionId)/join-requests")
        } catch DotWatcherAPIError.badResponse(401, _) {
            await signOut(status: "Sign in required")
        } catch {
            // silently ignore — badge count from POST response is the primary signal
        }
    }

    func approveJoinRequest(_ request: JoinRequest) async throws {
        guard let membership = activeMembership else { return }
        let _: SessionMembership = try await send(
            path: "/sessions/\(membership.sessionId)/join-requests/\(request.requestId)/approve",
            method: "POST")
        pendingJoinRequestCount = max(0, pendingJoinRequestCount - 1)
        await loadSelectedSessionMembers()
    }

    func denyJoinRequest(_ request: JoinRequest) async throws {
        guard let membership = activeMembership, let token = accessToken else { return }
        try await sendEmpty(
            path: "/sessions/\(membership.sessionId)/join-requests/\(request.requestId)/deny",
            method: "POST",
            token: token)
        pendingJoinRequests.removeAll { $0.requestId == request.requestId }
        pendingJoinRequestCount = max(0, pendingJoinRequestCount - 1)
    }

    func start() {
        guard !isTracking else { return }
        guard isAuthenticated else {
            status = "Sign in required"
            return
        }
        guard !sessionId.isEmpty else {
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

    func previewSession(_ sessionId: String) async -> [String] {
        do {
            let groups: [[RunnerPositionResponse]] = try await send(path: "/locations/\(sessionId)")
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
                "sessionId": sessionId,
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
            if let count = response.pendingJoinRequests {
                pendingJoinRequestCount = count
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
                let message = try? JSONDecoder().decode(ServerErrorBody.self, from: data)
                throw DotWatcherAPIError.badResponse(statusCode, message?.error)
            }
            return try JSONDecoder().decode(T.self, from: data)
        } catch let error as DotWatcherAPIError {
            throw error
        } catch {
            throw DotWatcherAPIError.network
        }
    }

    private func revokeToken(_ token: String) async {
        guard let url = URL(string: serverBaseURL.absoluteString + "/auth/logout") else {
            return
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        _ = try? await URLSession.shared.data(for: request)
    }

    private func sendEmpty(path: String, method: String, token: String) async throws {
        let normalizedPath = path.hasPrefix("/") ? path : "/\(path)"
        guard let url = URL(string: serverBaseURL.absoluteString + normalizedPath) else {
            throw DotWatcherAPIError.network
        }

        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")

        do {
            let (_, response) = try await URLSession.shared.data(for: request)
            let statusCode = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard (200..<300).contains(statusCode) else {
                throw DotWatcherAPIError.badResponse(statusCode, nil)
            }
        } catch let error as DotWatcherAPIError {
            throw error
        } catch {
            throw DotWatcherAPIError.network
        }
    }

    private func upsertMembership(_ membership: SessionMembership) {
        memberships.removeAll { $0.sessionId == membership.sessionId }
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

    private static func configuredServerBaseURL() -> URL {
        let configured = Bundle.main.object(forInfoDictionaryKey: "DotWatcherAPIBaseURL") as? String
        let rawValue = configured?.trimmingCharacters(in: .whitespacesAndNewlines)
        let fallback = "https://dot-watcher.skelstar.io/api"
        let urlString: String
        if let rawValue, !rawValue.isEmpty {
            urlString = rawValue
        } else {
            urlString = fallback
        }

        guard let url = URL(string: urlString),
              let scheme = url.scheme?.lowercased(),
              url.host != nil
        else {
            preconditionFailure("DotWatcherAPIBaseURL must be a valid absolute URL.")
        }

        if scheme == "https" {
            return url
        }

        #if DEBUG
        if scheme == "http" {
            return url
        }
        #endif

        preconditionFailure("DotWatcherAPIBaseURL must use HTTPS outside local development.")
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
