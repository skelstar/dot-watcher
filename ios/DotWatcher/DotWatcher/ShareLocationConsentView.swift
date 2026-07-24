import SwiftUI

/// App Store 5.1.2(i): explicit, declinable consent to share location with other members of a
/// session — distinct from the OS CoreLocation permission prompt, and asked once per session
/// (each session has a different set of viewers). Declining leaves the user able to view the
/// session as normal; only starting to share their own position is refused.
///
/// Also collects how long to share for, capped at `LocationManager.maxTrackingDuration`: sharing
/// always auto-stops on its own, the user just picks how soon (this is a cap on automatic
/// sharing, not an automatic/manual mode switch — every option here still auto-stops).
struct ShareLocationConsentView: View {
    let sessionName: String
    let onShare: (TimeInterval) -> Void
    let onDecline: () -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var selectedDuration: TimeInterval = LocationManager.trackingDurationOptions.last ?? 24 * 60 * 60

    var body: some View {
        VStack(spacing: 24) {
            Image(systemName: "location.circle.fill")
                .font(.system(size: 44))
                .foregroundStyle(Color.accentColor)
                .frame(width: 64, height: 64)
                .background(Color.accentColor.opacity(0.16), in: RoundedRectangle(cornerRadius: 16))

            VStack(spacing: 8) {
                Text("Share your location?")
                    .font(.title2.bold())
                Text("Other members of “\(sessionName)” will be able to see your live position on the map while tracking is active. You can stop at any time.")
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .foregroundStyle(.secondary)
            }

            VStack(alignment: .leading, spacing: 8) {
                Text("Share for")
                    .font(.caption)
                    .fontWeight(.semibold)
                    .foregroundStyle(.secondary)
                Picker("Share for", selection: $selectedDuration) {
                    ForEach(LocationManager.trackingDurationOptions, id: \.self) { duration in
                        Text(Self.label(for: duration)).tag(duration)
                    }
                }
                .pickerStyle(.segmented)
                Text("Sharing always stops on its own — this just picks how soon, up to 24 hours.")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            VStack(spacing: 10) {
                Button {
                    onShare(selectedDuration)
                    dismiss()
                } label: {
                    Text("Share My Location")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(.green)
                .controlSize(.large)

                Button {
                    onDecline()
                    dismiss()
                } label: {
                    Text("Don't Share")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .controlSize(.large)
            }

            Text("You can still view this session as a spectator if you don't share.")
                .font(.caption)
                .multilineTextAlignment(.center)
                .foregroundStyle(.tertiary)
        }
        .padding(32)
    }

    private static func label(for duration: TimeInterval) -> String {
        let hours = Int(duration / 3600)
        return "\(hours)h"
    }
}
