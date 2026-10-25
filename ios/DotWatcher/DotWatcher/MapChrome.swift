import SwiftUI

// The pieces of the full-screen map that the casual map and the session map share: the wordmark and
// countdown at the top left, the round buttons down the right, the hold-to-stop pill and the battery
// card. Laid out like the Android app's casual map.

/// "dot-watchr" with the person's initials as the dot, over the map.
struct MapWordmark: View {
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
        .shadow(color: .white, radius: 4)
        .foregroundStyle(.black)
    }
}

/// The ring and "h:mm" until tracking stops by itself. Tapping it opens the "add time or stop" sheet.
struct MapCountdown: View {
    var location: LocationManager
    let onTap: () -> Void

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let remaining = max(0, (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date))
            let total = max(1, location.trackingTotalDuration)
            let low = remaining <= 30 * 60
            let tint = low ? Color(red: 0xE0 / 255, green: 0x8A / 255, blue: 0) : Color(red: 0x30 / 255, green: 0xA1 / 255, blue: 0x4E / 255)
            Button(action: onTap) {
                ZStack {
                    Circle().stroke(Color.black.opacity(0.08), lineWidth: 3)
                    Circle()
                        .trim(from: 0, to: min(1, remaining / total))
                        .stroke(tint, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                    Text(Self.hoursMinutes(remaining))
                        .font(.system(size: 11, weight: .bold, design: .monospaced))
                        .foregroundStyle(low ? Color(red: 0xA3 / 255, green: 0x5F / 255, blue: 0) : .black)
                }
                .padding(2)
                .frame(width: 46, height: 46)
                .background(low ? Color(red: 1, green: 0.88, blue: 0.54) : Color.white.opacity(0.85), in: Circle())
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Auto-stops in \(Int(remaining / 60)) minutes")
            .accessibilityHint("Add time or stop now")
        }
    }

    static func hoursMinutes(_ seconds: TimeInterval) -> String {
        let total = Int(seconds)
        return "\(total / 3600):" + String(format: "%02d", (total % 3600) / 60)
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

/// "Auto-stops in 8 min": only inside the last 30 minutes, next to the amber countdown.
struct EndingSoonPill: View {
    var location: LocationManager

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let remaining = (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date)
            if remaining > 0, remaining <= 30 * 60 {
                Text("Auto-stops in \(Int((remaining + 59) / 60)) min")
                    .font(.system(size: 12.5, weight: .semibold))
                    .foregroundStyle(Color(red: 0x7A / 255, green: 0x4D / 255, blue: 0))
                    .padding(.horizontal, 13)
                    .frame(height: 36)
                    .background(Color(red: 1, green: 0.96, blue: 0.84), in: Capsule())
            }
        }
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
