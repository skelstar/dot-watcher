import CoreLocation
import Foundation
import Network
import Security
import SwiftUI

struct AppUser: Codable {
    let userId: String
    let username: String
    let displayName: String
}

enum AppearanceMode: String, CaseIterable, Identifiable {
    case system
    case light
    case dark

    var id: String { rawValue }

    var label: String {
        switch self {
        case .system: return "System"
        case .light: return "Light"
        case .dark: return "Dark"
        }
    }

    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return .light
        case .dark: return .dark
        }
    }
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

private struct LocationPostResponse: Codable {
    let participants: [String]
    /// Latest position for every runner in the session, included so the app can eventually
    /// render a native map without a separate `GET /locations/{sessionId}` round-trip. Each
    /// inner array holds exactly one position (the runner's latest), matching the shape
    /// `GET /locations/{sessionId}` already returns. Unused today — reserved for a future
    /// native map view.
    let positions: [[RunnerPositionResponse]]?
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
    private(set) var recentSessions: [SessionMembership] = []
    private(set) var isOffline = false
    /// Latest known position for every runner in the session, as of the local user's most
    /// recent `POST /location`. Only refreshes on that cadence (every `interval` seconds while
    /// tracking) — there's no separate polling of `GET /locations/{sessionId}`.
    private(set) var runnerPositions: [RunnerPositionResponse] = []

    var participants: [String] = []
    private var lastParticipantCount = 0
    private var isLoadingSessions = false
    fileprivate var latestLocation: CLLocation?
    fileprivate var oneShotLocationContinuation: CheckedContinuation<CLLocation?, Never>?

    /// The phone's own current position, straight from CoreLocation — not from the last
    /// `POST /location` response. Used to render the local user's own map pin immediately,
    /// without waiting for a round-trip to the server.
    var currentCoordinate: CLLocationCoordinate2D? { latestLocation?.coordinate }
    var currentHeading: Double? {
        guard let course = latestLocation?.course, course >= 0 else { return nil }
        return course
    }

    private let clManager = CLLocationManager()
    private let locationDelegate = LocationDelegate()
    private var trackingTask: Task<Void, Never>?
    private var accessToken: String?
    private let pathMonitor = NWPathMonitor()
    private let pathMonitorQueue = DispatchQueue(label: "DotWatcher.NWPathMonitor")
    private var connectivityPollTimer: Timer?

    let serverBaseURL = LocationManager.configuredServerBaseURL()
    let webBaseURL = LocationManager.configuredWebBaseURL()
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

    var appearanceMode: AppearanceMode = AppearanceMode(rawValue: UserDefaults.standard.string(forKey: "appearanceMode") ?? "") ?? .system {
        didSet { UserDefaults.standard.set(appearanceMode.rawValue, forKey: "appearanceMode") }
    }

    let interval: TimeInterval = 15

    var isAuthenticated: Bool { accessToken != nil && currentUser != nil }

    var activeMembership: SessionMembership? {
        memberships.first { $0.sessionId == sessionId }
    }

    var canTrackSelectedSession: Bool {
        activeMembership?.role == "runner"
    }

    func nextPostAt(from now: Date = Date()) -> Date {
        let seconds = now.timeIntervalSince1970
        let nextEpoch = (floor(seconds / interval) + 1) * interval
        return Date(timeIntervalSince1970: nextEpoch)
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

        pathMonitor.pathUpdateHandler = { [weak self] path in
            guard let self else { return }
            Task { @MainActor [self] in
                self.isOffline = path.status != .satisfied
            }
        }
        pathMonitor.start(queue: pathMonitorQueue)
    }

    // NWPathMonitor's callback can lag behind the real connectivity state (especially in the
    // Simulator), so re-check the live path directly rather than relying on it firing promptly.
    func refreshConnectivity() {
        isOffline = pathMonitor.currentPath.status != .satisfied
    }

    // Polls while the app is in the foreground so someone watching the screen for a signal to
    // come back (e.g. out on a trail) sees it update within a second, not just on app switches.
    func startConnectivityPolling() {
        refreshConnectivity()
        connectivityPollTimer?.invalidate()
        connectivityPollTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor [self] in self.refreshConnectivity() }
        }
    }

    func stopConnectivityPolling() {
        connectivityPollTimer?.invalidate()
        connectivityPollTimer = nil
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
        recentSessions = []
        sessionId = ""
        Self.storeToken(nil)
        UserDefaults.standard.removeObject(forKey: "currentUser")
        status = nextStatus
    }

    func loadSessions() async {
        guard isAuthenticated, !isLoadingSessions else { return }
        isLoadingSessions = true
        defer { isLoadingSessions = false }
        do {
            memberships = try await send(path: "/me/sessions")
            if !sessionId.isEmpty && activeMembership == nil {
                sessionId = ""
            }
            if sessionId.isEmpty {
                if let first = memberships.first {
                    selectSession(first)
                }
            }
        } catch DotWatcherAPIError.badResponse(401, _) {
            await signOut(status: "Sign in required")
        } catch {
            status = error.localizedDescription
        }
    }

    // Sessions the user has left; the server keeps these as archived (left_at set)
    // memberships rather than deleting them, so this survives across devices/reinstalls.
    func loadRecentSessions() async {
        guard isAuthenticated else { return }
        do {
            recentSessions = try await send(path: "/me/sessions/recent")
        } catch {
            // Leave the existing list in place; this is a secondary, best-effort fetch.
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
                "role": "runner",
            ])
        upsertMembership(membership)
        selectSession(membership)
    }

    func selectSession(_ membership: SessionMembership) {
        sessionId = membership.sessionId
        status = membership.role == "viewer" ? "Viewer only" : "Ready"
    }

    func leaveSession(sessionId code: String) async throws {
        if isTracking && sessionId == code { stop() }
        guard let token = accessToken else { throw DotWatcherAPIError.missingToken }
        try await sendEmpty(path: "/me/sessions/\(code)/membership", method: "DELETE", token: token)
        if let left = memberships.first(where: { $0.sessionId == code }) {
            recentSessions.removeAll { $0.sessionId == code }
            recentSessions.insert(left, at: 0)
        }
        memberships.removeAll { $0.sessionId == code }
        if sessionId == code {
            sessionId = ""
            participants = []
            status = "Idle"
        }
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

    func stopAndLeave() async {
        let id = sessionId
        stop()
        guard !id.isEmpty else { return }
        try? await leaveSession(sessionId: id)
    }

    private func trackingLoop() async {
        while !Task.isCancelled {
            let delay = nextPostAt().timeIntervalSinceNow
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled else { break }
            captureAndPost()
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
        let targetSessionId = sessionId
        do {
            var body: [String: Any] = [
                "runnerName": runnerName,
                "sessionId": targetSessionId,
                "latitude": lat,
                "longitude": lon,
                "timestamp": ISO8601DateFormatter().string(from: timestamp),
            ]
            if let heading { body["heading"] = heading }

            let response: LocationPostResponse = try await send(path: "/location", method: "POST", body: body)
            guard sessionId == targetSessionId else { return }
            status = "Sent"
            lastSent = Date()
            if response.participants.count != lastParticipantCount {
                lastParticipantCount = response.participants.count
                participants = response.participants
            }
            runnerPositions = (response.positions ?? []).flatMap { $0 }
        } catch {
            guard sessionId == targetSessionId else { return }
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
        runnerName = session.user.displayName.uppercased()
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
        request.setValue(UIDevice.current.name, forHTTPHeaderField: "X-Device-Name")
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
        request.setValue(UIDevice.current.name, forHTTPHeaderField: "X-Device-Name")
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
        request.setValue(UIDevice.current.name, forHTTPHeaderField: "X-Device-Name")

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

    /// TestFlight builds carry a sandbox receipt; App Store builds carry a production receipt.
    /// Both are archived from the same Release build configuration, so this is the only way to
    /// tell them apart at runtime and route TestFlight to the staging server.
    private static var isTestFlightBuild: Bool {
        Bundle.main.appStoreReceiptURL?.lastPathComponent == "sandboxReceipt"
    }

    private static func configuredServerBaseURL() -> URL {
        configuredBaseURL(
            infoKey: isTestFlightBuild ? "DotWatcherStagingAPIBaseURL" : "DotWatcherAPIBaseURL",
            fallback: "https://dot-watcher.skelstar.io/api"
        )
    }

    private static func configuredWebBaseURL() -> URL {
        configuredBaseURL(
            infoKey: isTestFlightBuild ? "DotWatcherStagingWebBaseURL" : "DotWatcherWebBaseURL",
            fallback: "https://dot-watcher.skelstar.io"
        )
    }

    private static func configuredBaseURL(infoKey: String, fallback: String) -> URL {
        let configured = Bundle.main.object(forInfoDictionaryKey: infoKey) as? String
        let rawValue = configured?.trimmingCharacters(in: .whitespacesAndNewlines)
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
            preconditionFailure("\(infoKey) must be a valid absolute URL.")
        }

        if scheme == "https" {
            return url
        }

        #if DEBUG
        if scheme == "http" {
            return url
        }
        #endif

        preconditionFailure("\(infoKey) must use HTTPS outside local development.")
    }
}

struct RunnerPositionResponse: Codable {
    let runnerName: String
    let latitude: Double
    let longitude: Double
    let heading: Double?
    let timestamp: String

    var parsedTimestamp: Date? {
        ISO8601DateFormatter().date(from: timestamp)
    }
}

private final class LocationDelegate: NSObject, CLLocationManagerDelegate {
    weak var owner: LocationManager?

    func locationManager(_ manager: CLLocationManager, didUpdateLocations locations: [CLLocation]) {
        guard let loc = locations.last else { return }
        Task { @MainActor [weak self] in
            self?.owner?.latestLocation = loc
            self?.owner?.oneShotLocationContinuation?.resume(returning: loc)
            self?.owner?.oneShotLocationContinuation = nil
        }
    }

    func locationManager(_ manager: CLLocationManager, didFailWithError error: Error) {
        Task { @MainActor [weak self] in
            self?.owner?.oneShotLocationContinuation?.resume(returning: nil)
            self?.owner?.oneShotLocationContinuation = nil
        }
    }

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {}
}
