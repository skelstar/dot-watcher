import Foundation
import UserNotifications

/// Local notification telling the runner their phone just switched to satellite. Entirely
/// on-device: fired from the `NWPathMonitor` transition in `LocationManager.handlePathChange`, so
/// it needs no server round-trip and works while the app is running in the background under the
/// `location` background mode (i.e. while tracking).
final class SatelliteAlerts: NSObject, UNUserNotificationCenterDelegate {
    static let shared = SatelliteAlerts()

    private static let identifier = "satellite-entered"
    /// Cell/satellite handoff can flap while moving; don't alert more than once per window.
    private static let cooldown: TimeInterval = 5 * 60
    private var lastFiredAt: Date?

    /// Must run at launch so a notification that arrives while the app is in the foreground is
    /// still presented (iOS suppresses foreground notifications without this delegate).
    func install() {
        UNUserNotificationCenter.current().delegate = self
    }

    /// Prompts the first time only; a no-op once the user has answered either way.
    func requestAuthorizationIfNeeded() {
        let center = UNUserNotificationCenter.current()
        center.getNotificationSettings { settings in
            guard settings.authorizationStatus == .notDetermined else { return }
            center.requestAuthorization(options: [.alert, .sound]) { _, _ in }
        }
    }

    func authorizationStatus() async -> UNAuthorizationStatus {
        await UNUserNotificationCenter.current().notificationSettings().authorizationStatus
    }

    func notifyEnteredSatellite(playSound: Bool) {
        if let last = lastFiredAt, Date().timeIntervalSince(last) < Self.cooldown { return }
        lastFiredAt = Date()

        let content = UNMutableNotificationContent()
        content.title = "On satellite"
        content.body = "No cell signal — still sending your position, less often."
        content.sound = playSound ? .default : nil
        let request = UNNotificationRequest(identifier: Self.identifier, content: content, trigger: nil)
        UNUserNotificationCenter.current().add(request)
    }

    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound])
    }
}
