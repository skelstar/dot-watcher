import CoreLocation
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
    /// The nearby-join choices; only offered when `nearbyAvailable` (the account has the events feature).
    @Binding var nearby: NearbyCreateDraft
    let nearbyAvailable: Bool
    /// The "groups" feature: the nearby section offers "Ask for a code to join".
    var groupsAvailable = false
    let locationState: NearbyLocationState
    let onAllowLocation: () -> Void
    /// Where the organiser is now, for the nearby range preview map.
    var loadPreviewFix: (() async -> CLLocation?)? = nil
    let isBusy: Bool
    let isOffline: Bool
    let onCreate: () -> Void
    @Environment(\.dismiss) private var dismiss

    private var nearbyIsOn: Bool { nearbyAvailable && nearby.isOn }

    private var canCreate: Bool {
        if isBusy || isOffline { return false }
        if nearbyIsOn {
            return !nearby.trimmedTitle.isEmpty && locationState == .ready && (!groupsAvailable || nearby.isEntryCodeValid)
        }
        return sessionCode.count >= 4
    }

    var body: some View {
        VStack(spacing: 0) {
            if isOffline {
                Label("No internet connection. Creating a session needs a connection.", systemImage: "wifi.slash")
                    .font(.app(.headline))
                    .foregroundStyle(.white)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 10)
                    .padding(.horizontal, 16)
                    .background(Color.red)
            }

            ScrollView {
                VStack(spacing: 24) {
                    Text("Create a New Session")
                        .font(.app(.title2, weight: .bold))

                    if nearbyIsOn {
                        Text("Give your run a title. People nearby will see it. You'll still get an invite code to share.")
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                            .foregroundStyle(.secondary)

                        TextField("Title", text: $nearby.title)
                            .textFieldStyle(.roundedBorder)
                            .submitLabel(.done)
                            .onChange(of: nearby.title) { _, title in
                                if title.count > NearbyCreateDraft.maxTitleLength {
                                    nearby.title = String(title.prefix(NearbyCreateDraft.maxTitleLength))
                                }
                            }
                    } else {
                        Text("Give your session a name. You'll get an invite code to share with others.")
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                            .foregroundStyle(.secondary)

                        CodeBoxField(text: $sessionCode, length: 8)
                    }

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
                            .font(.app(.footnote))
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.leading)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }

                    if nearbyAvailable {
                        NearbyCreateSection(
                            draft: $nearby, locationState: locationState, onAllowLocation: onAllowLocation,
                            groupsAvailable: groupsAvailable, loadPreviewFix: loadPreviewFix)
                    }

                    Button("Create Session") {
                        onCreate()
                        dismiss()
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!canCreate)

                    Button("Cancel", role: .cancel) {
                        dismiss()
                    }
                    .foregroundStyle(.secondary)
                }
                .padding(32)
            }
        }
    }
}
