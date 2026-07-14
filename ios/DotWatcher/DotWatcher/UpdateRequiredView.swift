import SwiftUI

// TestFlight-only for now — there's no public App Store listing yet. Swap this URL (and the
// button label/message below) to the App Store listing once the app ships publicly.
private let updateURL = URL(string: "itms-beta://")!

struct UpdateRequiredView: View {
    @Environment(\.openURL) private var openURL

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "arrow.up.circle.fill")
                .font(.system(size: 56))
                .foregroundStyle(.blue)
            Text("Update Required")
                .font(.title2.bold())
            Text("This version of DotWatcher is no longer supported. Open TestFlight to install the latest build.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.horizontal, 24)
            Button {
                openURL(updateURL)
            } label: {
                Text("Open TestFlight")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .padding(.horizontal, 40)
            .padding(.top, 8)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(.systemBackground))
    }
}

#Preview {
    UpdateRequiredView()
}
