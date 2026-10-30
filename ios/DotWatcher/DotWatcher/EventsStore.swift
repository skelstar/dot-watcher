import CoreLocation
import Observation
import SwiftUI

// Public events near the signed-in account (server: Controllers/EventsController.cs). Same approach as
// crew and casual: the server decides who has the feature, and an account without it gets a 404, which
// hides the sign quietly. With a position the list is the runs people nearby can join (see
// .ai/plans/nearby-join.md); without one the server still sends its placeholder event.

/// A public event shown on the start screen. `eyebrow` is the small line above the title.
struct NearbyEvent: Decodable, Identifiable, Equatable {
    let id: String
    let eyebrow: String
    let title: String
    let subtitle: String
    /// Set when this is a session the person can join from where they are; nil for the placeholder.
    let sessionId: String?
}

/// Shares `LocationManager`'s network stack (headers, token, the 426 version gate), like `CasualStore`.
@Observable
@MainActor
final class EventsStore {
    /// Events near the person, nearest first. Empty until loaded, and for an account without events.
    private(set) var events: [NearbyEvent] = []
    /// True once the server has answered for this account, false after a 404. Gates the Create sheet's
    /// nearby switch and the "allow location" sign.
    private(set) var isAvailable = false

    /// The runs the person can join, as opposed to the placeholder.
    var joinable: [NearbyEvent] { events.filter { $0.sessionId != nil } }

    @ObservationIgnored private weak var location: LocationManager?

    func attach(_ location: LocationManager) {
        self.location = location
    }

    /// Reloads the list. Safe to call often: a network failure keeps what is already shown. Uses a position
    /// only when location is already allowed: this never asks for the permission itself.
    func refresh() async {
        guard let location, location.isAuthenticated else { return }
        var path = "/me/events/nearby"
        if let fix = await location.currentFix() {
            path += String(format: "?lat=%.6f&lon=%.6f", fix.coordinate.latitude, fix.coordinate.longitude)
            if fix.horizontalAccuracy >= 0 { path += String(format: "&accuracy=%.0f", fix.horizontalAccuracy) }
        }
        do {
            events = try await location.send(path: path)
            isAvailable = true
        } catch DotWatcherAPIError.badResponse(let status, _) {
            // 401 means the session ended, 426 is handled app-wide and 429 means slow down; none says
            // anything about whether the account has events. Anything else (404) hides the sign quietly.
            if status == 401 || status == 426 || status == 429 { return }
            events = []
            isAvailable = false
        } catch {
            // Network trouble: keep what is already shown.
        }
    }

    func clear() {
        events = []
        isAvailable = false
    }
}

/// Loads the events when the app opens, on sign-in, when it comes to the front, when location is allowed,
/// and once a minute while the landing screen is up (not while tracking).
struct EventsHost: ViewModifier {
    private static let refreshSeconds = 60

    var events: EventsStore
    var location: LocationManager
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .task {
                events.attach(location)
                await events.refresh()
            }
            .task(id: location.isAuthenticated) {
                guard location.isAuthenticated else { return }
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(Self.refreshSeconds))
                    if scenePhase == .active, !location.isTracking { await events.refresh() }
                }
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if isAuthenticated {
                    Task { await events.refresh() }
                } else {
                    events.clear()
                }
            }
            .onChange(of: location.locationAuthorization) { _, _ in
                Task { await events.refresh() }
            }
            .onChange(of: scenePhase) { _, phase in
                if phase == .active { Task { await events.refresh() } }
            }
    }
}
