import CoreLocation
import SwiftUI
import UIKit

// Nearby join: an organiser lets people near them find and join a session with no invite code. The server
// side is SessionsController / EventsController; the plan is .ai/plans/nearby-join.md. Everything here is
// reached only by accounts with the "events" feature.

/// The ranges an organiser can choose, in metres. The server accepts exactly these.
enum NearbyRadius {
    static let options: [(meters: Int, label: String)] = [
        (50, "50 m"), (100, "100 m"), (250, "250 m"), (500, "500 m"),
    ]
    static let defaultMeters = 100
}

/// What a nearby session is created with: the range, and the organiser's position, which becomes the
/// first join location.
struct NearbyJoinSettings {
    let radiusMeters: Int
    let fix: CLLocation
}

/// A session's nearby setting as the server reports it. No coordinates: they never leave the server.
struct NearbyJoinStatus: Decodable, Equatable {
    let nearby: Bool
    let radiusMeters: Int
}

/// The Create sheet's nearby choices, kept together so the sheet takes one binding.
struct NearbyCreateDraft {
    /// The most characters a public title can have (the server limit).
    static let maxTitleLength = 30

    var isOn = false
    var title = ""
    var radiusMeters = NearbyRadius.defaultMeters
    /// Groups: "Ask for a code to join" (see .ai/plans/go-and-groups.md).
    var askForCode = false
    var entryCode = ""

    var trimmedTitle: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    /// The code to send, or nil when not asking for one.
    var entryCodeToSend: String? { askForCode && !entryCode.isEmpty ? entryCode : nil }

    /// Asking for a code needs a code the server accepts.
    var isEntryCodeValid: Bool { !askForCode || EntryCode.isValid(entryCode) }
}

/// Whether a position can be taken now, for the Create sheet's nearby switch.
enum NearbyLocationState {
    case ready
    case needsPermission
    case denied
}

/// A fix is good enough to place a join location when it's known and within 100 m, the server's limit.
func isUsableNearbyFix(_ fix: CLLocation) -> Bool {
    fix.horizontalAccuracy >= 0 && fix.horizontalAccuracy <= 100
}

extension LocationManager {
    var nearbyLocationState: NearbyLocationState {
        if canUseLocationNow { return .ready }
        return isLocationDenied ? .denied : .needsPermission
    }

    /// The organiser's Ping: takes a fresh fix and makes it the session's join location. Returns words
    /// for the person, whether it worked or not. `radiusMeters` nil keeps the session's current range.
    func pingJoinLocation(sessionId: String, radiusMeters: Int? = nil) async -> String {
        guard let fix = await currentFix(), isUsableNearbyFix(fix) else {
            return "Couldn't get an accurate location. Try again with a clear view of the sky."
        }
        let radius = radiusMeters ?? nearbyStatuses[sessionId]?.radiusMeters ?? NearbyRadius.defaultMeters
        do {
            try await pingNearby(sessionId: sessionId, fix: fix, radiusMeters: radius, enabled: true)
            return "Join location set"
        } catch DotWatcherAPIError.badResponse(410, _) {
            return "This session has ended."
        } catch {
            return error.localizedDescription
        }
    }
}

// MARK: - Create

/// The "Let people nearby join" switch and range on the Create sheet.
struct NearbyCreateSection: View {
    @Binding var draft: NearbyCreateDraft
    let locationState: NearbyLocationState
    let onAllowLocation: () -> Void
    /// The "groups" feature: offers "Ask for a code to join".
    var groupsAvailable = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Toggle("Let people nearby join", isOn: $draft.isOn)
            Text("People near you can find this run and join. Turn off any time.")
                .font(.app(.footnote))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            if draft.isOn {
                HStack {
                    Text("Range")
                    Spacer()
                    Picker("Range", selection: $draft.radiusMeters) {
                        ForEach(NearbyRadius.options, id: \.meters) { option in
                            Text(option.label).tag(option.meters)
                        }
                    }
                    .pickerStyle(.menu)
                }
                Text("GPS can be off by 10 to 50 m, so a wider range is easier to join from.")
                    .font(.app(.footnote))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                locationNote
                if groupsAvailable { entryCodeChoice }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder
    private var entryCodeChoice: some View {
        Toggle("Ask for a code to join", isOn: $draft.askForCode)
        if draft.askForCode {
            TextField("Entry code", text: $draft.entryCode)
                .textFieldStyle(.roundedBorder)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .onChange(of: draft.entryCode) { _, value in
                    let cleaned = EntryCode.clean(value)
                    if cleaned != value { draft.entryCode = cleaned }
                }
            Text("4 to 8 letters or numbers. Give it to entrants. People nearby still see the run, but can't join without it.")
                .font(.footnote)
                .foregroundStyle(draft.entryCode.isEmpty || draft.isEntryCodeValid ? Color.secondary : Color.red)
                .fixedSize(horizontal: false, vertical: true)
        } else {
            Text("Off: anyone nearby can join with one tap.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private var locationNote: some View {
        switch locationState {
        case .ready:
            EmptyView()
        case .needsPermission:
            Button("Allow location", action: onAllowLocation)
                .buttonStyle(.bordered)
        case .denied:
            VStack(alignment: .leading, spacing: 6) {
                Text("Location is off for DotWatcher, so people nearby can't find this run.")
                    .font(.app(.footnote))
                    .foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
                Button("Open Settings") {
                    if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) }
                }
                .buttonStyle(.bordered)
            }
        }
    }
}

// MARK: - Join

/// What tapping a nearby run shows: who will see you, and when. Not a choice between runner and viewer:
/// watching without sharing is the shared watch link.
///
/// With groups (`location.hasGroups`) joining only changes who sees you, and nothing happens until you press Go.
/// When the run asks for an entry code (`NearbyEvent.needsCode`) the sheet has a field for it.
struct NearbyJoinSheet: View {
    let event: NearbyEvent
    var location: LocationManager
    let onJoined: () -> Void
    /// Groups, before Go: "Join and Go now" joins, then this starts Go. Nil hides it.
    var onJoinAndGo: (() -> Void)? = nil
    @Environment(\.dismiss) private var dismiss
    @State private var isBusy = false
    @State private var errorText: String?
    @State private var code = ""

    private var asGroup: Bool { location.hasGroups }
    private var needsCode: Bool { asGroup && event.needsCode == true }
    private var canJoin: Bool { !isBusy && !location.isOffline && (!needsCode || EntryCode.isValid(code)) }

    var body: some View {
        // Scrolls: with groups (the code field, a second button) it is taller than the half-height sheet.
        ScrollView { content }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text(event.eyebrow.uppercased())
                    .font(.app(.caption, weight: .semibold))
                    .foregroundStyle(.secondary)
                Text(event.title)
                    .font(.app(.title2, weight: .bold))
                Text(event.subtitle)
                    .foregroundStyle(.secondary)
            }

            VStack(alignment: .leading, spacing: 12) {
                if asGroup {
                    infoRow("person.2", "Everyone in this group sees you by name on their map, wherever they are, while you're out.")
                    infoRow("flag", "Not out yet? Join now and the group waits. Nobody sees you until you press Go.")
                    infoRow("clock", groupUntilText)
                    infoRow("eye", "Your run stays yours: joining doesn't change how you track.")
                } else {
                    infoRow("person.2", "Everyone in the run sees your name and live location.")
                    infoRow("clock", "Only while you're tracking. You choose how long, and you can leave any time.")
                    infoRow("arrow.counterclockwise", "A replay of the run is kept afterwards.")
                }
            }

            if needsCode {
                VStack(alignment: .leading, spacing: 6) {
                    TextField("Entry code", text: $code)
                        .textFieldStyle(.roundedBorder)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .onChange(of: code) { _, value in
                            let cleaned = EntryCode.clean(value)
                            if cleaned != value { code = cleaned }
                        }
                    Text("The organiser gave entrants this code.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            if let errorText {
                Text(errorText)
                    .font(.app(.subheadline, weight: .semibold))
                    .foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Button {
                Task { await join(thenGo: false) }
            } label: {
                Text(isBusy ? "Joining…" : (asGroup ? "Join group" : "Join this run"))
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .disabled(!canJoin)

            if asGroup, onJoinAndGo != nil {
                Button {
                    Task { await join(thenGo: true) }
                } label: {
                    Text("Join and Go now")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .disabled(!canJoin)
            }

            Button("Not now", role: .cancel) { dismiss() }
                .frame(maxWidth: .infinity)
                .foregroundStyle(.secondary)
        }
        .padding(28)
    }

    private func infoRow(_ symbol: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: symbol)
                .frame(width: 24)
                .foregroundStyle(.secondary)
            Text(text)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var groupUntilText: String {
        if let time = GroupTime(endsAtISO: event.endsAt, now: Date()) {
            return "Until the group ends at \(time.endsAt). You can leave any time."
        }
        return "Until the group ends. You can leave any time."
    }

    private func join(thenGo: Bool) async {
        guard let sessionId = event.sessionId else { return }
        isBusy = true
        errorText = nil
        defer { isBusy = false }

        guard let fix = await location.currentFix() else {
            errorText = "Couldn't get your location. Check that Location is allowed for DotWatcher and try again."
            return
        }
        do {
            try await location.joinNearby(
                sessionId: sessionId, fix: fix, displayName: location.runnerName, entryCode: needsCode ? code : nil)
            dismiss()
            if thenGo, let onJoinAndGo {
                onJoinAndGo()
            } else {
                onJoined()
            }
        } catch DotWatcherAPIError.badResponse(403, let message) where asGroup {
            // The only 403 this call gives is a missing or wrong entry code.
            errorText = code.isEmpty
                ? "Enter the code the organiser gave you."
                : (message ?? "That code doesn't match. Check with the organiser.")
        } catch DotWatcherAPIError.badResponse(404, _) {
            // One answer for every refusal, by design: too far, ended, switched off or gone.
            errorText = "You can't join this run from here. Move closer to the start and try again."
        } catch DotWatcherAPIError.badResponse(429, _) {
            errorText = "Too many attempts. Try again in a little while."
        } catch {
            errorText = error.localizedDescription
        }
    }
}

// MARK: - Organiser controls

/// What the map screen adds for the organiser of a nearby session: a short note under the top edge (the
/// join-location button's confirmation), the settings sheet, and the End session confirmation. A modifier
/// of its own so the map's already long chain of sheets and dialogs stays within the type-checker's budget.
struct NearbyOrganiserControls: ViewModifier {
    var location: LocationManager
    /// The session the controls apply to; nil when the person isn't its nearby organiser.
    let sessionId: String?
    @Binding var showSettings: Bool
    @Binding var showEndSession: Bool
    let note: String?
    let onEnd: () async -> Void

    func body(content: Content) -> some View {
        content
            .overlay(alignment: .top) {
                if let note {
                    Text(note)
                        .font(.app(.subheadline, weight: .semibold))
                        .foregroundStyle(.black)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        .background(Color.white.opacity(0.92), in: Capsule())
                        .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
                        .padding(.top, 70)
                        .transition(.opacity)
                        .allowsHitTesting(false)
                }
            }
            .sheet(isPresented: $showSettings) {
                if let sessionId {
                    NearbySettingsSheet(location: location, sessionId: sessionId) {
                        // Wait for the sheet to finish dismissing before asking, or the dialog is dropped.
                        Task {
                            try? await Task.sleep(for: .milliseconds(400))
                            showEndSession = true
                        }
                    }
                    .presentationDetents([.medium, .large])
                }
            }
            .confirmationDialog("End this session?", isPresented: $showEndSession, titleVisibility: .visible) {
                Button("End session", role: .destructive) {
                    Task { await onEnd() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("This ends it for everyone. Replay stays available.")
            }
    }
}

/// The organiser's nearby settings, from the map's join-location button: range, Ping, stop showing the
/// session nearby, and End session.
struct NearbySettingsSheet: View {
    var location: LocationManager
    let sessionId: String
    /// Asks the person to confirm before ending the session for everyone.
    let onEnd: () -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var radiusMeters: Int
    @State private var message: String?
    @State private var isBusy = false

    init(location: LocationManager, sessionId: String, onEnd: @escaping () -> Void) {
        self.location = location
        self.sessionId = sessionId
        self.onEnd = onEnd
        _radiusMeters = State(
            initialValue: location.nearbyStatuses[sessionId]?.radiusMeters ?? NearbyRadius.defaultMeters)
    }

    private var status: NearbyJoinStatus? { location.nearbyStatuses[sessionId] }

    var body: some View {
        ScrollView { content }
    }

    private var content: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Nearby join")
                .font(.app(.title2, weight: .bold))

            if let status, status.nearby {
                Text("People within \(status.radiusMeters) m of your join location can find this run and join. Tap the button on the map to move it, for example from the start to the finish.")
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text("Nearby join is off. Only people with the invite code can join.")
                    .foregroundStyle(.secondary)
            }

            Picker("Range", selection: $radiusMeters) {
                ForEach(NearbyRadius.options, id: \.meters) { option in
                    Text(option.label).tag(option.meters)
                }
            }
            .pickerStyle(.segmented)

            Button {
                Task { await setJoinLocation() }
            } label: {
                Text(isBusy ? "Working…" : "Set join location here")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .disabled(isBusy)

            if status?.nearby == true {
                Button("Stop showing this run nearby") {
                    Task { await stopShowing() }
                }
                .buttonStyle(.bordered)
                .disabled(isBusy)
            }

            if let message {
                Text(message)
                    .font(.app(.subheadline, weight: .semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }

            if location.hasGroups {
                Divider()
                EntryCodeSection(location: location, sessionId: sessionId)
            }

            Divider()

            Button("End session", role: .destructive) {
                dismiss()
                onEnd()
            }
            .frame(maxWidth: .infinity)
        }
        .padding(28)
    }

    private func setJoinLocation() async {
        isBusy = true
        message = await location.pingJoinLocation(sessionId: sessionId, radiusMeters: radiusMeters)
        isBusy = false
    }

    private func stopShowing() async {
        isBusy = true
        do {
            try await location.pingNearby(sessionId: sessionId, fix: nil, enabled: false)
            message = "No longer shown nearby"
        } catch {
            message = error.localizedDescription
        }
        isBusy = false
    }
}
