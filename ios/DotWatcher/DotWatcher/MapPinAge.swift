import SwiftUI

/// How old a runner's last position can be before their pin changes on the map.
enum MapPinAge {
    /// A runner with no position update for longer than this is considered stale/stationary
    /// and rendered with the dimmed "sleep" pin style, matching the web client's 30s threshold.
    static let staleAfter: TimeInterval = 30
    /// A runner with no position update for longer than this is considered disconnected —
    /// rendered with a dashed outline and transparent fill instead of the pulsing sleep style.
    static let disconnectedAfter: TimeInterval = 60
}

/// Shown over the map while `LocationManager.isUltraConstrained` — mirrors `ContentView`'s
/// `offlineBanner` (`Label` + SF Symbol) but as a floating pill rather than a full-width bar,
/// since this overlays just the map rather than the whole screen.
struct SatelliteStatusBadge: View {
    let lastSent: Date?
    let nextRetryAt: Date?

    var body: some View {
        VStack(spacing: 2) {
            Label("On satellite — sending position, map paused", systemImage: "antenna.radiowaves.left.and.right")
                .font(.subheadline.weight(.semibold))
                .multilineTextAlignment(.leading)
            if let lastSent {
                Text("Last sent \(lastSent, style: .relative) ago")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.8))
            }
            // Only present after a failed send, and only until that retry fires (or the regular
            // send takes over). `.timer` counts down live without needing a view refresh.
            if let nextRetryAt, nextRetryAt > Date() {
                Text("Send failed — retrying in \(nextRetryAt, style: .timer)")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.8))
            }
        }
        .foregroundStyle(.white)
        .padding(.vertical, 10)
        .padding(.horizontal, 14)
        .frame(maxWidth: .infinity)
        .background(Color.orange, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .shadow(color: .black.opacity(0.25), radius: 4, y: 2)
    }
}
