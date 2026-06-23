import SwiftUI

struct NameEntryView: View {
    @Binding var name: String
    let isFirstLaunch: Bool
    let onConfirm: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 24) {
            RunnerCircle(name: name, size: 72)

            Text(isFirstLaunch ? "Welcome to DotWatcher" : "Enter Initials")
                .font(.title2.bold())

            Text("Enter your 2-letter initials so others can find you on the map.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)

            CodeBoxField(text: $name, length: 2, lettersOnly: true, autoFocus: true)

            Button("Save") {
                onConfirm()
            }
            .buttonStyle(.borderedProminent)
            .disabled(name.count != 2)

            if !isFirstLaunch {
                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                .foregroundStyle(.secondary)
            }
        }
        .padding(32)
    }
}
