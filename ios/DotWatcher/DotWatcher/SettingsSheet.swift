import SwiftUI
import UIKit
import UserNotifications

/// App preferences: dot size, notifications, the hidden Developer section, and legal/version info.
/// Opened from the gear in the start screen's top bar and the gear on the map rail. Who the person is
/// (initials, crew name, blocked users, sign out) lives in `AuthSheet` instead.
struct SettingsSheet: View {
    @Bindable var location: LocationManager

    @State private var notificationStatus: UNAuthorizationStatus = .notDetermined
    @State private var testNotificationSentAt: Date?

    private static let testNotificationDelay: TimeInterval = 5

    @Environment(\.dismiss) private var dismiss
    @Environment(\.openURL) private var openURL
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        NavigationStack {
            Form {
                Section("Appearance") {
                    VStack(alignment: .leading, spacing: 10) {
                        Text("Dot size")
                        DotSizePicker(selection: $location.mapDotSize, initials: location.runnerName)
                    }
                    .padding(.vertical, 4)
                }
                notificationsSection
                if showsDeveloperSection {
                    developerSection
                }
                aboutSection
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .task { notificationStatus = await SatelliteAlerts.shared.authorizationStatus() }
            .onChange(of: scenePhase) { _, phase in
                guard phase == .active else { return }
                Task { notificationStatus = await SatelliteAlerts.shared.authorizationStatus() }
            }
        }
    }

    private var notificationsSection: some View {
        Section {
            Toggle("Satellite alerts", isOn: $location.satelliteAlertsEnabled)
            Toggle("Play sound", isOn: $location.satelliteAlertSound)
                .disabled(!location.satelliteAlertsEnabled)
            Toggle("Crew alerts", isOn: $location.crewOutAlertsEnabled)
            if notificationStatus == .denied {
                Button("Allow notifications in Settings") {
                    if let url = URL(string: UIApplication.openSettingsURLString) { openURL(url) }
                }
            }
        } header: {
            Text("Notifications")
        } footer: {
            Text(notificationStatus == .denied
                 ? "Notifications are turned off for DotWatcher in iOS Settings, so these alerts can't be shown."
                 : "Satellite alerts tell you when your phone switches to satellite while you're tracking. Crew alerts tell you when someone in your crew starts a Go run while you're on one.")
        }
    }

    /// Debug builds always show the Developer section; Release/TestFlight builds only once the
    /// hidden 10-tap gesture on the build/SHA label has unlocked it.
    private var showsDeveloperSection: Bool {
        #if DEBUG
        return true
        #else
        return location.developerOptionsUnlocked
        #endif
    }

    /// Tools for testing on a real device: force satellite mode mid-run, fire the satellite alert on
    /// demand, and see how many posts landed in each network mode.
    private var developerSection: some View {
        Section {
            Toggle("Force satellite mode", isOn: $location.debugForceUltraConstrained)
            Button("Send test notification in \(Int(Self.testNotificationDelay))s") {
                SatelliteAlerts.shared.sendTestNotification(
                    after: Self.testNotificationDelay,
                    playSound: location.satelliteAlertSound
                )
                testNotificationSentAt = Date()
            }
            Button("Send test crew alert in \(Int(Self.testNotificationDelay))s") {
                CrewOutAlerts.shared.sendTestNotification(after: Self.testNotificationDelay)
                testNotificationSentAt = Date()
            }
            if let testNotificationSentAt {
                Text("Sent at \(testNotificationSentAt, style: .time) — lock the phone now.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            postStatsRow("Normal posts", location.normalPostStats)
            postStatsRow("Satellite posts", location.satellitePostStats)
            if location.developerOptionsUnlocked {
                Button("Hide developer options", role: .destructive) {
                    location.developerOptionsUnlocked = false
                    location.debugForceUltraConstrained = false
                }
            }
        } header: {
            Text("Developer")
        } footer: {
            Text("Force satellite mode simulates a carrier satellite (Direct-to-Cell) connection: the on-satellite map badge, paused polling, a 90s post interval, and the satellite alert if you're tracking. It switches off when the app relaunches. Post counts reset on relaunch.")
        }
    }

    private func postStatsRow(_ title: String, _ stats: LocationManager.PostStats) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title)
            Text("\(stats.successes)/\(stats.attempts) sent · \(stats.transportFailures) failed · \(stats.retries) retries · \(stats.pathTriggers) path triggers")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var aboutSection: some View {
        Section {
            Link("Privacy Policy", destination: LegalLinks.privacy)
            Link("Terms of Use", destination: LegalLinks.terms)
            if let version = Self.versionText {
                LabeledContent("Version", value: version)
            }
        } header: {
            Text("About")
        } footer: {
            Text(LegalLinks.disclaimer)
        }
    }

    /// "1.4.4 (975)", from the bundle's marketing version and build number.
    private static var versionText: String? {
        let info = Bundle.main.infoDictionary
        let version = info?["CFBundleShortVersionString"] as? String
        let build = info?["CFBundleVersion"] as? String
        switch (version, build) {
        case let (version?, build?): return "\(version) (\(build))"
        case let (version?, nil): return version
        case let (nil, build?): return "build \(build)"
        default: return nil
        }
    }
}

/// The privacy policy and terms, shown in Settings and on the sign-in screen.
enum LegalLinks {
    static let privacy = URL(string: "https://dot-watcher.skelstar.io/privacy")!
    static let terms = URL(string: "https://dot-watcher.skelstar.io/terms")!
    static let disclaimer = "Location data can be delayed, inaccurate, or unavailable."
}

/// The Dot size choice as three tiles, each showing the user's own dot at that size; the chosen
/// tile is outlined. No text on the tiles: the dots are the labels (VoiceOver still reads the names).
private struct DotSizePicker: View {
    @Binding var selection: MapDotSize
    let initials: String

    var body: some View {
        HStack(spacing: 10) {
            ForEach(MapDotSize.allCases) { size in
                let isSelected = size == selection
                Button {
                    selection = size
                } label: {
                    RunnerMapPin(name: initials.isEmpty ? "ME" : initials, size: size.pinSize, isHighlighted: true)
                        .frame(maxWidth: .infinity)
                        .frame(height: 72)
                        .background(Color(.secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))
                        .overlay {
                            RoundedRectangle(cornerRadius: 12)
                                .strokeBorder(isSelected ? Color.accentColor : .clear, lineWidth: 2.5)
                        }
                        .contentShape(RoundedRectangle(cornerRadius: 12))
                }
                // Plain style, or a Form row fires every button in it on any tap.
                .buttonStyle(.plain)
                .accessibilityLabel(size.label)
                .accessibilityAddTraits(isSelected ? .isSelected : [])
            }
        }
        .animation(.easeInOut(duration: 0.15), value: selection)
    }
}
