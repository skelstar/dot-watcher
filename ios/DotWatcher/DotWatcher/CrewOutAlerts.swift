import Foundation
import UserNotifications

/// Which crew members are newly out, from one Go post reply to the next (see .ai/plans/crew-out-alerts.md).
/// Pure logic, kept apart from the notification so it can be reasoned about (and tested) on its own.
struct CrewOutTracker {
    /// A dot that drops out for a bad-signal stretch and comes back shouldn't alert twice.
    static let cooldown: TimeInterval = 30 * 60

    /// Nil until the first reply of a run: whoever is already out then sets the baseline silently.
    private var lastOut: Set<String>?
    private var lastAlertedAt: [String: Date] = [:]

    /// Forget everything, so the next reply sets a new baseline. Called when a run starts and stops.
    mutating func reset() {
        lastOut = nil
        lastAlertedAt = [:]
    }

    /// `out` is the crew on a Go run in this reply (not yourself, not crew in a named session). Returns
    /// the names to announce, sorted.
    mutating func update(out: Set<String>, now: Date) -> [String] {
        defer { lastOut = out }
        guard let previous = lastOut else {
            for name in out { lastAlertedAt[name] = now }
            return []
        }
        let fresh = out.subtracting(previous).filter { name in
            guard let last = lastAlertedAt[name] else { return true }
            return now.timeIntervalSince(last) >= Self.cooldown
        }
        for name in fresh { lastAlertedAt[name] = now }
        return fresh.sorted()
    }
}

/// The local notification for crew who just started a Go run while you're on one. Fired from the reply to
/// this phone's own `POST /location`, so it works with the screen locked while tracking and needs no push.
@MainActor
final class CrewOutAlerts {
    static let shared = CrewOutAlerts()

    /// Identifier prefix: a tap on one of these opens the Go map (see `SatelliteAlerts`' delegate).
    nonisolated static let identifierPrefix = "crew-out"

    private var tracker = CrewOutTracker()

    func reset() { tracker.reset() }

    /// Feed one successful Go post reply. Self and crew in a named session never count.
    func handle(positions: [RunnerPositionResponse], enabled: Bool, now: Date = Date()) {
        let out = Set(positions.filter { $0.isSelf != true && $0.inSession != true }.map(\.runnerName))
        let fresh = tracker.update(out: out, now: now)
        guard enabled, !fresh.isEmpty else { return }
        post(names: fresh, trigger: nil)
    }

    /// Developer option: a sample alert after a delay, so there's time to lock the phone first.
    func sendTestNotification(after delay: TimeInterval) {
        SatelliteAlerts.shared.requestAuthorizationIfNeeded()
        post(names: ["Test"], trigger: UNTimeIntervalNotificationTrigger(timeInterval: delay, repeats: false))
    }

    private func post(names: [String], trigger: UNNotificationTrigger?) {
        let content = UNMutableNotificationContent()
        content.title = Self.title(for: names)
        content.body = names.count == 1 ? "Started a Go run" : "Started Go runs"
        content.sound = .default
        let id = "\(Self.identifierPrefix)-\(names.joined(separator: "-"))"
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: trigger))
    }

    /// "DW: Mia has joined", "DW: Mia and Sam have joined", "DW: Jo, Mia and Sam have joined". The "DW:"
    /// says whose alert it is on a watch, which shows the text but not DotWatcher's icon.
    static func title(for names: [String]) -> String {
        switch names.count {
        case 0: return ""
        case 1: return "DW: \(names[0]) has joined"
        default: return "DW: \(names.dropLast().joined(separator: ", ")) and \(names[names.count - 1]) have joined"
        }
    }
}

extension Notification.Name {
    /// Posted when someone taps a crew-out alert; the Go map opens if a Go run is still being tracked.
    nonisolated static let crewOutAlertTapped =Notification.Name("crewOutAlertTapped")
}
