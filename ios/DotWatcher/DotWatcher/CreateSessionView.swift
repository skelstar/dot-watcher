import SwiftUI

struct CreateSessionView: View {
    @Binding var sessionCode: String
    let isBusy: Bool
    let isOffline: Bool
    let onCreate: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            if isOffline {
                Label("No internet connection. Creating a session needs a connection.", systemImage: "wifi.slash")
                    .font(.headline)
                    .foregroundStyle(.white)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
                    .padding(.horizontal, 16)
                    .background(Color.red)
            }

            VStack(spacing: 24) {
                Text("Create a New Session")
                    .font(.title2.bold())

                Text("Give your session a name. You'll get an invite code to share with others.")
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)

                CodeBoxField(text: $sessionCode, length: 8)

                Button("Create Session") {
                    onCreate()
                    dismiss()
                }
                .buttonStyle(.borderedProminent)
                .disabled(isBusy || isOffline || sessionCode.count < 4)

                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                .foregroundStyle(.secondary)
            }
            .padding(32)
        }
    }
}
