import SwiftUI
import UIKit

struct ContentView: View {
    @Environment(\.scenePhase) private var scenePhase
    @State private var location = LocationManager()
    /// Crew (the people you have chosen). Self-contained: see `CrewStore` and `CrewViews.swift`.
    @State private var crew = CrewStore()
    /// Casual tracking (start tracking with no session). Self-contained: see `CasualStore` and `CasualViews.swift`.
    @State private var casual = CasualStore()
    /// Public events near the person; the start screen shows a sign for the first one. See `EventsStore`.
    @State private var events = EventsStore()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showAuth: Bool = false
    @State private var showUpdateRequired: Bool = false
    @State private var showShareConsent: Bool = false
    @State private var showCreateSession: Bool = false
    @State private var noSessionCreateCode = ""
    @State private var noSessionCreateLengthHours = CreateSessionView.defaultLengthHours
    @State private var noSessionInviteCode = ""
    @State private var showJoinSheet = false
    /// Invite code from a tapped `/join/{CODE}` link (see `InviteLink`), held until it's been prefilled
    /// or confirmed. Survives the sign-in sheet and the initial sessions load.
    @State private var pendingLinkCode: String?
    @State private var isBusy = false
    @State private var formError: String?
    /// Counts consecutive taps on the build/SHA label (`headerSection`) toward the hidden
    /// gesture that flips `LocationManager.debugForceUltraConstrained` — the only way to trigger
    /// satellite-mode testing on a TestFlight/Release build, which has no visible debug UI.
    @State private var versionTapCount = 0
    @State private var lastVersionTap: Date?

    /// The first half of `body`'s modifier chain. Split from `body` because one chain this long
    /// exceeds the Swift type-checker's budget ("unable to type-check this expression in
    /// reasonable time").
    private var bodyWithSheets: some View {
        mainContent
            .modifier(CrewHost(crew: crew, location: location, yieldsToCasualMap: casual.isMapPresented))
            .modifier(CasualHost(casual: casual, crew: crew, location: location))
            .modifier(EventsHost(events: events, location: location))
            .modifier(SessionMapCover(
                location: location,
                casualMapPresented: casual.isMapPresented,
                showShareConsent: $showShareConsent
            ))
            .preferredColorScheme(location.appearanceMode.colorScheme)
            .onAppear {
                UIDevice.current.isBatteryMonitoringEnabled = true
                batteryLevel = UIDevice.current.batteryLevel
                location.startConnectivityPolling()
                if location.isAuthenticated {
                    if location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                        showNameEntry = true
                    }
                    Task { await location.loadSessions() }
                } else {
                    showAuth = true
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: UIDevice.batteryLevelDidChangeNotification)) { _ in
                batteryLevel = UIDevice.current.batteryLevel
            }
            .onChange(of: scenePhase) { _, newPhase in
                if newPhase == .active {
                    location.startConnectivityPolling()
                } else {
                    location.stopConnectivityPolling()
                }
            }
            .sheet(isPresented: $showHelp) {
                HelpView()
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .sheet(isPresented: $showNameEntry) {
                NameEntryView(
                    name: $nameInput,
                    isFirstLaunch: location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty
                ) {
                    location.runnerName = nameInput.trimmingCharacters(in: .whitespaces)
                    showNameEntry = false
                }
                .interactiveDismissDisabled(location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty)
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .sheet(isPresented: $showCreateSession) {
                CreateSessionView(
                    sessionCode: $noSessionCreateCode,
                    maxLengthHours: $noSessionCreateLengthHours,
                    isBusy: isBusy,
                    isOffline: location.isOffline
                ) {
                    Task { await noSessionCreateSession() }
                }
                .presentationDetents([.height(560)])
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .sheet(isPresented: $showJoinSheet) {
                JoinCodeSheet(code: $noSessionInviteCode, isBusy: isBusy) {
                    Task { await noSessionJoinSession() }
                }
                .presentationDetents([.height(380)])
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .fullScreenCover(isPresented: $showAuth) {
                AuthSheet(location: location, crew: crew)
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                guard isAuthenticated else {
                    showAuth = true
                    return
                }
                if location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                    showNameEntry = true
                }
                resolvePendingLink()
            }
    }

    var body: some View {
        bodyWithSheets
            .onOpenURL { url in
                // A crew link (https://…/crew/{CODE}): from a tap, or the camera app scanning a crew QR
                // code. Only leads to the "Add X to your crew?" confirmation; never adds anyone itself.
                if let crewCode = CrewLink.code(from: url) {
                    crew.attach(location)
                    crew.onLink(crewCode, signedIn: location.isAuthenticated)
                    return
                }
                guard let code = InviteLink.code(from: url) else { return }
                pendingLinkCode = code
                resolvePendingLink()
            }
            // Keeps the route in step with the active session (switching, joining, leaving).
            .task(id: location.activeMembership?.inviteCode) {
                await location.loadRoute()
            }
            .fullScreenCover(isPresented: $showUpdateRequired) {
                UpdateRequiredView()
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .onChange(of: location.updateRequired) { _, updateRequired in
                if updateRequired { showUpdateRequired = true }
            }
    }

    // MARK: - Main Content

    /// There is one screen: the landing screen. A session lives on the map, which comes up over it while
    /// tracking (see `SessionMapCover`); stopping, or declining to share, leaves the session and comes back here.
    private var mainContent: some View {
        noSessionView
    }

    // MARK: - No Session View

    private var noSessionView: some View {
        VStack(spacing: 0) {
            if location.isOffline {
                offlineBanner
            }
            // Header, crew and any error scroll; the sign buttons stay pinned to the bottom where a
            // thumb reaches them.
            ScrollView {
                VStack(spacing: 16) {
                    headerSection
                    CrewRow(crew: crew)
                    if casual.isAvailable, !crew.members.isEmpty, let line = crewOutLine(casual.crewOut) {
                        Text(line)
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    if let error = casual.startError {
                        Text(error)
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(.red)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    if let formError {
                        Label(formError, systemImage: "exclamationmark.triangle.fill")
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(.red)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(12)
                            .background(Color.red.opacity(0.12))
                            .clipShape(RoundedRectangle(cornerRadius: 10))
                    }
                }
                .padding()
            }
            .refreshable {
                // Detached: `.refreshable` cancels its closure's Task once the pull gesture is released, which
                // cut off every request after the first (see `sessionView`'s refresh).
                await Task {
                    await location.loadSessions()
                    await crew.refresh()
                    await casual.refresh()
                    await events.refresh()
                }.value
            }
            noSessionSigns
        }
    }

    private var noSessionSigns: some View {
        VStack(spacing: 14) {
            if casual.isAvailable {
                HoldSignButton(
                    title: "Go",
                    subtitle: "Hold to start"
                ) { casual.isBeforeYouGoPresented = true }
            }
            // Only when the server has events for this account (the "events" feature).
            if let event = events.events.first {
                EventSignButton(eyebrow: event.eyebrow, title: event.title, subtitle: event.subtitle) {}
            }
            SignButton(title: "Invite code", subtitle: "Join a run you've been invited to") {
                showJoinSheet = true
            }
            .disabled(isBusy)
            SignButton(title: "Create", subtitle: "Set up a new run for others") {
                showCreateSession = true
            }
            .disabled(isBusy)
        }
        .padding(.horizontal)
        .padding(.top, 8)
        .padding(.bottom)
    }

    private var offlineBanner: some View {
        Label("No internet connection", systemImage: "wifi.slash")
            .font(.headline)
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 10)
            .background(Color.red)
    }

    // MARK: - Header

    private var headerSection: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 2) {
                    Text("d")
                        .font(.largeTitle.bold())
                    if location.isAuthenticated, !location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                        Button {
                            nameInput = location.runnerName
                            showNameEntry = true
                        } label: {
                            Text(location.runnerName.uppercased())
                                .font(.callout.bold())
                                .foregroundStyle(.white)
                                .frame(width: 38, height: 38)
                                .background(Circle().fill(RunnerColorPalette.currentUser))
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Edit your name")
                    } else {
                        Text("o")
                            .font(.largeTitle.bold())
                    }
                    Text("t-watchr")
                        .font(.largeTitle.bold())
                }
                let build = Bundle.main.infoDictionary?["CFBundleVersion"] as? String
                let sha = Bundle.main.infoDictionary?["GitCommitSHA"] as? String
                if build != nil || sha != nil {
                    Text([build.map { "build \($0)" }, sha].compactMap { $0 }.joined(separator: " · "))
                        .font(.caption2)
                        .foregroundStyle(location.debugForceUltraConstrained ? Color.orange : Color(.tertiaryLabel))
                        .contentShape(Rectangle())
                        .onTapGesture { registerVersionTap() }
                }
            }
            Spacer()
            if location.isAuthenticated {
                Menu {
                    Button {
                        nameInput = location.runnerName
                        showNameEntry = true
                    } label: {
                        Label("Edit name", systemImage: "pencil")
                    }
                    Button { showAuth = true } label: {
                        Label(location.isAuthenticated ? "Settings" : "Account", systemImage: location.isAuthenticated ? "gearshape" : "person.crop.circle")
                    }
                    Button { showHelp = true } label: {
                        Label("Help", systemImage: "questionmark.circle")
                    }
                } label: {
                    Image(systemName: "ellipsis.circle")
                        .font(.title)
                        .foregroundStyle(.primary)
                }
            } else {
                Button { showHelp = true } label: {
                    Image(systemName: "questionmark.circle")
                        .font(.title)
                        .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
            }
        }
    }

    /// 10 taps within 2s of each other flips `debugForceUltraConstrained` — a deliberately
    /// undiscoverable way to test the satellite UI on TestFlight/Release builds, which have no
    /// visible debug menu. A single stray tap resets the count rather than accumulating forever,
    /// so it can't be triggered by accident over unrelated taps spread through a session.
    private func registerVersionTap() {
        let now = Date()
        if let lastVersionTap, now.timeIntervalSince(lastVersionTap) > 2 {
            versionTapCount = 0
        }
        lastVersionTap = now
        versionTapCount += 1
        guard versionTapCount >= 10 else { return }
        versionTapCount = 0
        location.debugForceUltraConstrained.toggle()
        UINotificationFeedbackGenerator().notificationOccurred(.success)
    }

    // MARK: - No Session Actions

    private func noSessionCreateSession() async {
        isBusy = true
        formError = nil
        do {
            try await location.createSession(
                name: noSessionCreateCode,
                displayName: location.runnerName,
                maxLengthHours: noSessionCreateLengthHours)
            noSessionCreateCode = ""
            noSessionCreateLengthHours = CreateSessionView.defaultLengthHours
            offerSharingAfterJoin()
        } catch {
            formError = error.localizedDescription
        }
        isBusy = false
    }

    private func noSessionJoinSession() async {
        isBusy = true
        formError = nil
        do {
            try await location.joinInvite(code: noSessionInviteCode, displayName: location.runnerName)
            noSessionInviteCode = ""
            pendingLinkCode = nil
            offerSharingAfterJoin()
        } catch {
            formError = error.localizedDescription
        }
        isBusy = false
    }

    /// Acts on a tapped invite link: opens the join sheet with the code filled in, and the person taps Join.
    /// The link never joins on its own. `pendingLinkCode` stays set until then, so the code isn't lost if the
    /// link arrives before sign-in or while the app is still starting.
    private func resolvePendingLink() {
        guard let code = pendingLinkCode, location.isAuthenticated else { return }
        noSessionInviteCode = code
        showJoinSheet = true
    }

    /// Just joined or created: ask how long to share for straight away, so confirming starts tracking and the
    /// map opens. Declining leaves the session and comes back to this screen.
    private func offerSharingAfterJoin() {
        showShareConsent = true
    }
}

#Preview {
    ContentView()
}

#Preview("First Launch") {
    let _ = UserDefaults.standard.removeObject(forKey: "runnerName")
    ContentView()
}
