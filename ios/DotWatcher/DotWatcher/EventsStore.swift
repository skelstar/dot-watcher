import Observation
import SwiftUI

// Public events near the signed-in account (server: Controllers/EventsController.cs). Same approach as
// crew and casual: the server decides who has the feature, and an account without it gets a 404, which
// hides the sign quietly.

/// A public event shown on the start screen. `eyebrow` is the small line above the title.
struct NearbyEvent: Decodable, Identifiable, Equatable {
    let id: String
    let eyebrow: String
    let title: String
    let subtitle: String
}

/// Shares `LocationManager`'s network stack (headers, token, the 426 version gate), like `CasualStore`.
@Observable
@MainActor
final class EventsStore {
    /// Events near the person, nearest first. Empty until loaded, and for an account without events.
    private(set) var events: [NearbyEvent] = []

    @ObservationIgnored private weak var location: LocationManager?

    func attach(_ location: LocationManager) {
        self.location = location
    }

    /// Reloads the list. Safe to call often: a network failure keeps what is already shown.
    func refresh() async {
        guard let location, location.isAuthenticated else { return }
        do {
            events = try await location.send(path: "/me/events/nearby")
        } catch DotWatcherAPIError.badResponse(let status, _) {
            // 401 means the session ended and 426 is handled app-wide; neither says anything about
            // whether the account has events. Anything else (404) hides the sign quietly.
            if status == 401 || status == 426 { return }
            events = []
        } catch {
            // Network trouble: keep what is already shown.
        }
    }

    func clear() {
        events = []
    }
}

/// Loads the events when the app opens, on sign-in, and whenever it comes to the front.
struct EventsHost: ViewModifier {
    var events: EventsStore
    var location: LocationManager
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .task {
                events.attach(location)
                await events.refresh()
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if isAuthenticated {
                    Task { await events.refresh() }
                } else {
                    events.clear()
                }
            }
            .onChange(of: scenePhase) { _, phase in
                if phase == .active { Task { await events.refresh() } }
            }
    }
}
