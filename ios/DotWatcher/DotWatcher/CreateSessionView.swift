import SwiftUI

struct CreateSessionView: View {
    @Binding var sessionCode: String
    let isBusy: Bool
    let onCreate: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
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
            .disabled(isBusy || sessionCode.count < 4)

            Button("Cancel", role: .cancel) {
                dismiss()
            }
            .foregroundStyle(.secondary)
        }
        .padding(32)
    }
}
