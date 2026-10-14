import SwiftUI

struct CreateSessionView: View {
    /// Event length choices, in hours. The server accepts 1-240; these are the presets offered.
    static let lengthOptions: [(hours: Int, label: String)] = [
        (12, "12 hours"), (24, "24 hours"), (48, "2 days"), (72, "3 days"), (120, "5 days"), (240, "10 days"),
    ]
    /// Default event length, in hours (also what the picker starts on).
    static let defaultLengthHours = 12

    @Binding var sessionCode: String
    @Binding var maxLengthHours: Int
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
                    .fixedSize(horizontal: false, vertical: true)
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
                    .fixedSize(horizontal: false, vertical: true)
                    .foregroundStyle(.secondary)

                CodeBoxField(text: $sessionCode, length: 8)

                VStack(spacing: 6) {
                    HStack {
                        Text("Event length")
                            .foregroundStyle(.primary)
                        Spacer()
                        Picker("Event length", selection: $maxLengthHours) {
                            ForEach(Self.lengthOptions, id: \.hours) { option in
                                Text(option.label).tag(option.hours)
                            }
                        }
                        .pickerStyle(.menu)
                    }
                    Text("Pick the longest your event could run. Going over won't stop tracking, but replay will start later.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }

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
