import Foundation
import Observation

// Casual tracking (server: Controllers/CasualController.cs): start tracking without creating or joining
// a session. Crew see you by name; people nearby see an anonymous dot, and only when both of you have
// chosen to be seen. See .ai/plans/casual-tracking.md.

struct CasualStartResponse: Decodable {
    let sessionId: String
    let runId: String
}

private struct CasualWatchLinkResponse: Decodable {
    let token: String
    let path: String
}

private struct CasualSettings: Decodable {
    let visibleToNonCrew: Bool
}

/// Yourself or someone in your crew who is out casually. The same fields as `RunnerPositionResponse`
/// plus `inSession` (they are in a named session rather than out casually: the lock) and `isSelf`.
struct CasualPosition: Decodable {
    let runnerName: String
    let latitude: Double
    let longitude: Double
    let heading: Double?
    let timestamp: String
    let inSession: Bool?
    let isSelf: Bool?
}

/// Someone who is not in your crew: a position and nothing else, so there is no name or id to show.
struct CasualNearbyDot: Decodable {
    let latitude: Double
    let longitude: Double
    let heading: Double?
    let inSession: Bool?
}

private struct CasualLiveResponse: Decodable {
    let participants: [String]?
    let positions: [[CasualPosition]]?
    let nearby: [CasualNearbyDot]?
}

/// Casual state and actions for the signed-in account. Shares `LocationManager`'s network stack
/// (headers, token, the 426 version gate) rather than having its own, as `CrewStore` does.
@Observable
@MainActor
final class CasualStore {
    enum Availability { case unknown, available, unavailable }

    /// The one thing kept on this phone: the "show me to people nearby" choice, so the switch is right
    /// the moment the screen opens. The server holds the real value.
    private static let visibleKey = "casualVisibleToNonCrew"

    /// How long a casual run shares for unless the person picks otherwise. Shorter than the 24 hours
    /// a session defaults to: a run is rarely longer than a few hours, and 24 is still one tap away.
    static let defaultDuration: TimeInterval = 4 * 60 * 60

    /// Whether this server has casual for this account. Until the server is updated, or while the
    /// account hasn't been given the feature, `/me/casual/settings` is a 404, and the UI then shows
    /// nothing rather than an error. Re-checked on sign-in and whenever the app comes to the front.
    private(set) var availability: Availability = .unknown
    /// "Show me to people nearby". Off until the person turns it on, then remembered.
    private(set) var visibleToNonCrew = UserDefaults.standard.bool(forKey: CasualStore.visibleKey)
    /// How many of the person's crew are out right now; nil until first loaded.
    private(set) var crewOut: Int?
    var settingsError: String?
    var startError: String?
    private(set) var isStarting = false

    /// The secret link to the current run that anyone can open in a browser, with no account or app.
    /// Made when the person opens "Share your run" and kept for the run, so reopening the sheet doesn't
    /// make another. Every link stops working when the run ends.
    private(set) var watchLink: URL?
    private(set) var isMakingWatchLink = false
    private(set) var watchLinkError: String?

    /// What the map shows: this person and their crew by name, and anonymous dots nearby.
    private(set) var people: [CasualPosition] = []
    private(set) var nearby: [CasualNearbyDot] = []

    /// The sheets and the map are presented once at the top of `ContentView` (see `CasualHost`).
    var isBeforeYouGoPresented = false
    var isWhoCanSeeMePresented = false
    var isMapPresented = false

    var isAvailable: Bool { availability == .available }

    @ObservationIgnored private weak var location: LocationManager?

    func attach(_ location: LocationManager) {
        self.location = location
    }

    // MARK: - Loading

    /// Reloads availability, the setting and the crew-out count. Safe to call often: a network
    /// failure keeps what is already shown.
    func refresh() async {
        guard let location, location.isAuthenticated else { return }
        do {
            let settings: CasualSettings = try await location.send(path: "/me/casual/settings")
            availability = .available
            visibleToNonCrew = settings.visibleToNonCrew
            UserDefaults.standard.set(settings.visibleToNonCrew, forKey: Self.visibleKey)
            await loadLive()
        } catch DotWatcherAPIError.badResponse(let status, _) {
            // 401 means the session ended and 426 is handled app-wide; neither says anything about
            // whether the server has casual. Anything else (404 from a server without it) hides the
            // feature quietly.
            if status == 401 || status == 426 { return }
            availability = .unavailable
            crewOut = nil
        } catch {
            // Network trouble: keep what is already shown.
        }
    }

    /// Who the person can see right now (`GET /casual/live`). The server returns nearby dots only
    /// while the person is out themselves, so there is nothing to scan from here.
    func loadLive() async {
        guard let location, location.isAuthenticated, availability == .available else { return }
        guard let live: CasualLiveResponse = try? await location.send(path: "/casual/live") else { return }
        let everyone = (live.positions ?? []).compactMap(\.first)
        people = everyone
        nearby = live.nearby ?? []
        crewOut = everyone.filter { $0.isSelf != true }.count
    }

    /// Forgets everything: signing out or deleting the account must not leave this person's choice
    /// behind for whoever signs in next.
    func clear() {
        availability = .unknown
        visibleToNonCrew = false
        UserDefaults.standard.removeObject(forKey: Self.visibleKey)
        crewOut = nil
        people = []
        nearby = []
        watchLink = nil
        watchLinkError = nil
        settingsError = nil
        startError = nil
        isBeforeYouGoPresented = false
        isWhoCanSeeMePresented = false
        isMapPresented = false
    }

    // MARK: - Settings

    /// Turns "Show me to people nearby" on or off. Shown at once and remembered on this phone; put
    /// back if the server refuses it.
    func setVisibleToNonCrew(_ visible: Bool) async {
        guard let location else { return }
        let before = visibleToNonCrew
        visibleToNonCrew = visible
        UserDefaults.standard.set(visible, forKey: Self.visibleKey)
        settingsError = nil
        do {
            let _: CasualSettings = try await location.send(
                path: "/me/casual/settings", method: "PUT", body: ["visibleToNonCrew": visible])
        } catch {
            visibleToNonCrew = before
            UserDefaults.standard.set(before, forKey: Self.visibleKey)
            settingsError = error.localizedDescription
        }
    }

    // MARK: - Watch link

    /// Makes a link to the current run (if there isn't one yet). The server gives a path, which is
    /// joined to the web site's address here, as the invite links are.
    func makeWatchLink() async {
        guard let location, watchLink == nil, !isMakingWatchLink else { return }
        isMakingWatchLink = true
        watchLinkError = nil
        defer { isMakingWatchLink = false }
        do {
            let link: CasualWatchLinkResponse = try await location.send(path: "/casual/watch-link", method: "POST")
            let base = location.webBaseURL.absoluteString.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            watchLink = URL(string: base + link.path)
            if watchLink == nil { watchLinkError = "Couldn't make a link." }
        } catch {
            watchLinkError = error.localizedDescription
        }
    }

    /// Ends every watch link for the current run now. The next time the sheet opens it makes a new one.
    func turnOffWatchLinks() async {
        guard let location else { return }
        try? await location.sendAuthorizedEmpty(path: "/casual/watch-link", method: "DELETE")
        watchLink = nil
        watchLinkError = nil
    }

    // MARK: - Starting and stopping

    /// Holding Go, then Go on "Before you go": start the casual run on the server, then tracking,
    /// then show the map. A failure stays on the start screen with the reason.
    func start(duration: TimeInterval) async {
        guard let location, !isStarting else { return }
        isStarting = true
        startError = nil
        defer { isStarting = false }
        do {
            let started: CasualStartResponse = try await location.send(path: "/casual/start", method: "POST")
            location.startCasual(sessionId: started.sessionId, duration: duration)
            guard location.isTracking else { return }
            people = []
            nearby = []
            watchLink = nil
            watchLinkError = nil
            isMapPresented = true
        } catch {
            startError = error.localizedDescription
        }
    }

    /// Ends the casual run. `LocationManager.stop()` tells the server, which takes the person off
    /// everyone's map at once.
    func stop() {
        location?.stop()
        isMapPresented = false
        watchLink = nil
        watchLinkError = nil
        Task { await refresh() }
    }
}
