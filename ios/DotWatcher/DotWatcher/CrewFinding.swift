import CoreLocation
import Foundation
import Observation
import UIKit

// "Find new crew" (server: CrewController /me/crew/finding, CrewFinder). Two people standing together turn it
// on, see each other by name, and become crew once both have accepted. It lasts two minutes and stops when the
// crew sheet closes. See .ai/plans/crew-finding.md.

/// Someone within 50 m who also has "Find new crew" on. `id` is a token for their current window, not an
/// account id; the server sends no position or distance.
struct CrewNearbyPerson: Codable, Identifiable, Hashable {
    let id: String
    let nickname: String?
    let displayName: String
    /// This phone's person tapped them and accepted.
    let youAccepted: Bool
    /// They tapped this phone's person and accepted; accepting back makes the two crew.
    let acceptedYou: Bool

    var label: String { CrewFormat.label(nickname: nickname, displayName: displayName) }
}

struct CrewFindingState: Decodable {
    let expiresAt: String
    let people: [CrewNearbyPerson]
    /// People who became crew during this window.
    let added: [CrewMember]
}

@Observable
@MainActor
final class CrewFinding {
    /// How often a fresh position is sent while it's on. The server lists someone only while their last fix
    /// is under 30 s old.
    static let refreshInterval: Duration = .seconds(4)
    static let window: TimeInterval = 120

    private(set) var isOn = false
    private(set) var isStarting = false
    private(set) var expiresAt: Date?
    /// People who have accepted this person come first, so they are easy to spot.
    private(set) var people: [CrewNearbyPerson] = []
    /// A line under the switch: why it couldn't start, or that it turned itself off.
    private(set) var note: String?
    /// Location is off for the app, so the note offers Settings.
    private(set) var needsSettings = false
    private(set) var acceptingId: String?
    var acceptError: String?
    /// Someone who just became crew this way, for a short confirmation.
    var justAdded: CrewMember?

    /// Called once for each person who becomes crew, so the crew list can show them straight away.
    @ObservationIgnored var onAdded: ((CrewMember) -> Void)?
    @ObservationIgnored private var loop: Task<Void, Never>?
    @ObservationIgnored private var reported: Set<String> = []
    @ObservationIgnored private weak var location: LocationManager?

    func attach(_ location: LocationManager) {
        self.location = location
    }

    /// The switch.
    func setOn(_ on: Bool) {
        if on {
            Task { await start() }
        } else {
            stop()
        }
    }

    func start() async {
        guard let location, !isOn, !isStarting else { return }
        note = nil
        needsSettings = false
        acceptError = nil
        justAdded = nil
        if location.isLocationDenied {
            needsSettings = true
            note = "Location is off for DotWatcher, so people next to you can't find you."
            return
        }
        guard location.canUseLocationNow else {
            location.requestWhenInUseAuthorizationIfNeeded()
            note = "Allow location, then turn this on again."
            return
        }

        isStarting = true
        defer { isStarting = false }
        guard let fix = await location.currentFix() else {
            note = "Couldn't get your position. Try again in a moment."
            return
        }
        do {
            let state: CrewFindingState = try await location.send(
                path: "/me/crew/finding", method: "POST", body: Self.body(fix))
            isOn = true
            reported = []
            apply(state)
            startLoop()
        } catch DotWatcherAPIError.badResponse(let status, _) {
            switch status {
            case 400:
                note = "Your position isn't accurate enough yet. Check Precise Location is on, then try again."
            case 429:
                note = "Too many tries. Wait a few minutes and try again."
            default:
                note = "Couldn't turn this on. Try again."
            }
        } catch {
            note = "Network error. Try again."
        }
    }

    /// Off: from the switch, when the sheet closes, or when the two minutes are up (`expired`). Unanswered
    /// accepts are dropped by the server.
    func stop(expired: Bool = false) {
        loop?.cancel()
        loop = nil
        let wasOn = isOn
        isOn = false
        expiresAt = nil
        people = []
        acceptingId = nil
        acceptError = nil
        note = expired ? "Turned off after 2 minutes. Turn on again to keep looking." : nil
        if wasOn, let location {
            Task { try? await location.sendAuthorizedEmpty(path: "/me/crew/finding", method: "DELETE") }
        }
    }

    func accept(_ person: CrewNearbyPerson) async {
        guard isOn, let location, acceptingId == nil else { return }
        acceptingId = person.id
        acceptError = nil
        defer { acceptingId = nil }
        do {
            let state: CrewFindingState = try await location.send(
                path: "/me/crew/finding/\(person.id)/accept", method: "POST")
            apply(state)
        } catch DotWatcherAPIError.badResponse(let status, _) {
            switch status {
            case 410: stop(expired: true)
            case 409: acceptError = "Crew is full, so \(person.label) can't be added."
            default: acceptError = "\(person.label) isn't nearby any more."
            }
        } catch {
            acceptError = "Network error. Try again."
        }
    }

    private func startLoop() {
        loop?.cancel()
        loop = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.refreshInterval)
                guard !Task.isCancelled, let self else { return }
                await self.refresh()
            }
        }
    }

    private func refresh() async {
        guard isOn, let location else { return }
        if let expiresAt, Date() >= expiresAt {
            stop(expired: true)
            return
        }
        guard let fix = await location.currentFix() else { return }
        do {
            let state: CrewFindingState = try await location.send(
                path: "/me/crew/finding", method: "PUT", body: Self.body(fix))
            guard isOn else { return }
            apply(state)
        } catch DotWatcherAPIError.badResponse(410, _) {
            stop(expired: true)
        } catch {
            // A missed refresh is fine: the next one follows in a few seconds, until the window ends.
        }
    }

    private func apply(_ state: CrewFindingState) {
        expiresAt = CrewFormat.parseServerDate(state.expiresAt) ?? expiresAt ?? Date().addingTimeInterval(Self.window)

        let hadAcceptedYou = Set(people.filter(\.acceptedYou).map(\.id))
        people = state.people.filter(\.acceptedYou) + state.people.filter { !$0.acceptedYou }
        if !Set(people.filter(\.acceptedYou).map(\.id)).subtracting(hadAcceptedYou).isEmpty {
            UIImpactFeedbackGenerator(style: .light).impactOccurred()
        }

        // Oldest first, so the newest ends up as the confirmation.
        for member in state.added.reversed() where !reported.contains(member.userId) {
            reported.insert(member.userId)
            onAdded?(member)
            justAdded = member
            UINotificationFeedbackGenerator().notificationOccurred(.success)
        }
    }

    private static func body(_ fix: CLLocation) -> [String: Any] {
        var body: [String: Any] = [
            "latitude": fix.coordinate.latitude,
            "longitude": fix.coordinate.longitude,
        ]
        if fix.horizontalAccuracy >= 0 { body["accuracy"] = fix.horizontalAccuracy }
        return body
    }
}
