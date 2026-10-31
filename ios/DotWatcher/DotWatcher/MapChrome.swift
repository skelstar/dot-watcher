import SwiftUI

// The pieces of the full-screen map that the casual map and the session map share: the wordmark and
// countdown at the top left, the round buttons down the right, the hold-to-stop pill and the battery
// card. Laid out like the Android app's casual map.

/// "dot-watchr" with the person's initials as the dot, over the map.
struct MapWordmark: View {
    let runnerName: String

    var body: some View {
        WordmarkText(runnerName: runnerName)
            .shadow(color: .white, radius: 4)
    }
}

/// The "dot-watchr" lettering itself, shared by the bare wordmark and the timer pill.
private struct WordmarkText: View {
    let runnerName: String

    var body: some View {
        HStack(spacing: 1) {
            Text("d").font(.system(size: 19, weight: .heavy))
            Text(RunnerCircle.initials(for: runnerName))
                .font(.system(size: 9.5, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 25, height: 25)
                .background(Circle().fill(RunnerColorPalette.currentUser))
            Text("t-watchr").font(.system(size: 19, weight: .heavy))
        }
        .foregroundStyle(.black)
    }
}

/// The wordmark in a frosted pill that doubles as the auto-stop timer, like a fuel gauge on its side:
/// a solid bar fills the pill at the start and drains right to left, with the time left at the right
/// end. The bar turns red for the last 5 minutes. Tapping it opens the "add time or stop" sheet.
struct MapTimerPill: View {
    var location: LocationManager
    let runnerName: String
    let onTap: () -> Void

    private static let redZone: TimeInterval = 5 * 60

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let remaining = max(0, (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date))
            let total = max(1, location.trackingTotalDuration)
            let fraction = min(1, remaining / total)
            let bar = remaining <= Self.redZone
                ? Color(red: 0xE5 / 255, green: 0x37 / 255, blue: 0x2D / 255)
                : Color.white.opacity(0.9)
            Button(action: onTap) {
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.white.opacity(0.3))
                    GeometryReader { geometry in
                        Rectangle().fill(bar).frame(width: geometry.size.width * fraction)
                    }
                    HStack(spacing: 0) {
                        WordmarkText(runnerName: runnerName)
                        Spacer(minLength: 10)
                        Text(Self.timeLeft(remaining))
                            .font(.system(size: 13, weight: .bold))
                            .monospacedDigit()
                            .foregroundStyle(.black)
                    }
                    .padding(.horizontal, 14)
                }
                .frame(width: 204, height: 44)
                .background(.ultraThinMaterial, in: Capsule())
                .clipShape(Capsule())
                .overlay(Capsule().strokeBorder(Color.white.opacity(0.45), lineWidth: 1))
                .shadow(color: .black.opacity(0.18), radius: 3, y: 1)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Auto-stops in \(Int((remaining + 59) / 60)) minutes")
            .accessibilityHint("Add time or stop now")
        }
    }

    /// "1h 20m" from an hour up, "29m" under it, whole seconds ("35s") in the last minute.
    static func timeLeft(_ seconds: TimeInterval) -> String {
        if seconds < 60 { return "\(Int(seconds.rounded(.up)))s" }
        let minutes = Int((seconds + 59) / 60)
        return minutes >= 60 ? "\(minutes / 60)h \(minutes % 60)m" : "\(minutes)m"
    }
}

/// A round white button in the column down the right of the map.
struct MapRailButton: View {
    let symbol: String
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(.black)
                .frame(width: 44, height: 44)
                .background(Color.white.opacity(0.85), in: Circle())
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }
}

/// The red stop pill: held, like Go. It fills darker left to right and stops tracking when full, so a
/// bump or a stray tap can't end a run.
struct HoldToStopPill: View {
    @Binding var progress: CGFloat
    let onComplete: () -> Void

    var body: some View {
        ZStack {
            Capsule().fill(stopRed)
            GeometryReader { geometry in
                Capsule()
                    .fill(stopRedFill)
                    .frame(width: geometry.size.width * progress)
            }
            .clipShape(Capsule())
            Text("Hold to stop")
                .font(.system(size: 15, weight: .bold))
                .foregroundStyle(.white)
        }
        .frame(width: 150, height: 48)
        .shadow(color: .black.opacity(0.25), radius: 3, y: 2)
        .contentShape(Capsule())
        .holdToComplete(progress: $progress, onComplete: onComplete)
        .accessibilityLabel("Stop")
        .accessibilityHint("Hold to stop tracking")
    }
}

/// Shown instead of the stop pill while updates are paused for a critical battery.
struct BatteryPausedCard: View {
    var location: LocationManager
    let onStop: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Battery critical, updates paused", systemImage: "battery.0percent")
                .font(.system(size: 13.5, weight: .bold))
                .foregroundStyle(Color(red: 0xC4 / 255, green: 0x27 / 255, blue: 0x1D / 255))
            Text("Others can't see you move until you send manually, plug in, or turn automatic updates back on.")
                .font(.system(size: 11.5))
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                Button { Task { await location.sendLocationNowWhilePaused() } } label: {
                    Text("Send now").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                Button { location.resumeAutomaticTrackingOverridingBattery() } label: {
                    Text("Auto").frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
            }
            Button(action: onStop) {
                Text("Stop").fontWeight(.bold).frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(stopRed)
        }
        .padding(14)
        .background(Color(red: 1, green: 0.94, blue: 0.93), in: RoundedRectangle(cornerRadius: 22))
    }
}
