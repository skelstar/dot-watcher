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
    @State private var showSettings: Bool = false
    @State private var showUpdateRequired: Bool = false
    @State private var showShareConsent: Bool = false
    @State private var showCreateSession: Bool = false
    @State private var noSessionCreateCode = ""
    @State private var noSessionCreateLengthHours = CreateSessionView.defaultLengthHours
    @State private var noSessionNearbyDraft = NearbyCreateDraft()
    /// A nearby run the person tapped, showing its join sheet; nil when none is up.
    @State private var nearbyCandidate: NearbyEvent?
    /// The id of the session that stopped a new one being created (409), while the person decides
    /// whether to end it; nil when no prompt is up.
    @State private var activeSessionConflict: String?
    @State private var noSessionInviteCode = ""
    @State private var showJoinSheet = false
    /// Invite code from a tapped `/join/{CODE}` link (see `InviteLink`), held until it's been prefilled
    /// or confirmed. Survives the sign-in sheet and the initial sessions load.
    @State private var pendingLinkCode: String?
    @State private var isBusy = false
    @State private var formError: String?
    /// Counts consecutive taps on the build/SHA label (`headerSection`) toward the hidden
    /// gesture that unlocks the Developer section in Settings (`LocationManager.developerOptionsUnlocked`)
    /// — the only way to reach satellite-mode testing on a TestFlight/Release build.
    @State private var versionTapCount = 0
    @State private var lastVersionTap: Date?
    /// For the groups' end times on the start screen; ticks once a minute.
    @State private var now = Date()

    /// Whether the session map covers the screen; the same test `SessionMapCover` uses. The crew sheet is then
    /// presented by that map, since a sheet can't present from beneath a full-screen cover.
    private var isSessionMapPresented: Bool {
        location.activeMembership != nil && (location.isTracking || showShareConsent) && !casual.isMapPresented
            && !location.isCasualTracking
    }

    /// The first half of `body`'s modifier chain. Split from `body` because one chain this long
    /// exceeds the Swift type-checker's budget ("unable to type-check this expression in
    /// reasonable time").
    private var bodyWithSheets: some View {
        mainContent
            .modifier(CrewHost(crew: crew, location: location, yieldsToCasualMap: casual.isMapPresented || isSessionMapPresented))
            .modifier(CasualHost(casual: casual, crew: crew, location: location))
            .modifier(EventsHost(events: events, location: location))
            .modifier(SessionMapCover(
                location: location,
                crew: crew,
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
                    Task {
                        await location.loadSessions()
                        await leaveStaleSessions()
                    }
                    fetchPalette()
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
                    // Groups change on other phones (an organiser ends one); pick that up on coming back.
                    if location.hasGroups { Task { await location.loadSessions() } }
                    // Use the palette the last fetch saved, then fetch the server's and fade it in if it changed.
                    PaletteStore.apply()
                    fetchPalette()
                } else {
                    location.stopConnectivityPolling()
                }
            }
            .sheet(isPresented: $showHelp) {
                HelpView()
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .sheet(isPresented: $showSettings) {
                SettingsSheet(location: location)
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
                    nearby: $noSessionNearbyDraft,
                    nearbyAvailable: events.isAvailable,
                    groupsAvailable: location.hasGroups,
                    locationState: location.nearbyLocationState,
                    onAllowLocation: { location.requestWhenInUseAuthorizationIfNeeded() },
                    isBusy: isBusy,
                    isOffline: location.isOffline
                ) {
                    Task { await noSessionCreateSession() }
                }
                .presentationDetents(events.isAvailable ? [.large] : [.height(560)])
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
                Task {
                    await location.loadSessions()
                    await leaveStaleSessions()
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
                    // The groups the person is in (only with the "groups" feature): joined ahead of Go, or while out.
                    ForEach(location.groupMemberships) { group in
                        GroupInCard(group: group, isOut: location.isCasualTracking, now: now, onLeave: {
                            Task {
                                do {
                                    try await location.leaveGroup(sessionId: group.sessionId)
                                } catch {
                                    formError = "Couldn't leave the group. Check your connection and try again."
                                }
                            }
                        }, organiser: location)
                    }
                    if casual.isAvailable, !crew.members.isEmpty, let line = crewOutLine(casual.crewOut) {
                        Text(line)
                            .font(.app(.subheadline))
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    if let error = casual.startError {
                        Text(error)
                            .font(.app(.subheadline, weight: .semibold))
                            .foregroundStyle(.red)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    if let formError {
                        Label(formError, systemImage: "exclamationmark.triangle.fill")
                            .font(.app(.subheadline, weight: .semibold))
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
        .background {
            if let screen = AppliedPalette.shared.colors?.screen { screen.ignoresSafeArea() }
        }
        .task {
            while !Task.isCancelled {
                now = Date()
                try? await Task.sleep(for: .seconds(60))
            }
        }
    }

    private var noSessionSigns: some View {
        VStack(spacing: 14) {
            if location.isCasualTracking {
                // Reached from the Go map's "‹ Home" (groups): tracking carries on.
                EventSignButton(
                    eyebrow: "You're out", title: "Back to the map",
                    subtitle: "Tracking carries on while you're here"
                ) { casual.isMapPresented = true }
            } else if casual.isAvailable {
                HoldSignButton(
                    title: "Go",
                    subtitle: "Hold to start"
                ) { casual.isBeforeYouGoPresented = true }
            }
            // Only when the server has events for this account (the "events" feature): the runs people
            // nearby can join, or the placeholder while there's no position to look near.
            ForEach(events.joinable) { event in
                EventSignButton(
                    eyebrow: event.needsCode == true ? "\(event.eyebrow) · Code needed" : event.eyebrow,
                    title: event.title, subtitle: event.subtitle, organiser: event.organiser,
                    nudging: true
                ) {
                    nearbyCandidate = event
                }
            }
            if events.joinable.isEmpty, let event = events.events.first {
                EventSignButton(eyebrow: event.eyebrow, title: event.title, subtitle: event.subtitle) {}
            }
            if events.isAvailable, location.nearbyLocationState == .needsPermission {
                EventSignButton(
                    eyebrow: "Nearby", title: "Find runs near you",
                    subtitle: "Allow location to see who is starting close by",
                    nudging: true
                ) { location.requestWhenInUseAuthorizationIfNeeded() }
            }
            SignButton(title: "Join", subtitle: "Enter an invite code", icon: .enter) {
                showJoinSheet = true
            }
            .disabled(isBusy)
            if location.canCreate {
                SignButton(title: "Create", subtitle: "Set up a new run for others", icon: .plus) {
                    showCreateSession = true
                }
                .disabled(isBusy)
            }
        }
        .padding(.horizontal)
        .padding(.top, 8)
        .padding(.bottom)
        .sheet(item: $nearbyCandidate) { event in
            NearbyJoinSheet(
                event: event, location: location,
                onJoined: { afterNearbyJoin() },
                onJoinAndGo: location.hasGroups && !location.isCasualTracking && casual.isAvailable
                    ? { goAfterJoin() } : nil
            )
                .presentationDetents([.medium, .large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
        }
        .alert(
            "You already have an active session",
            isPresented: Binding(get: { activeSessionConflict != nil }, set: { if !$0 { activeSessionConflict = nil } }),
            presenting: activeSessionConflict
        ) { sessionId in
            Button("End it and create this one", role: .destructive) {
                Task { await endActiveSessionThenCreate(sessionId) }
            }
            Button("Cancel", role: .cancel) {}
        } message: { _ in
            Text("Only one session can run at a time. Ending it removes everyone from it. Its replay stays available.")
        }
    }

    private var offlineBanner: some View {
        Label("No internet connection", systemImage: "wifi.slash")
            .font(.app(.headline))
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 10)
            .background(Color.red)
    }

    /// Fetches the server's start-screen palette and applies it as soon as it arrives, fading the colours in.
    /// Applying an unchanged palette redraws nothing visible.
    private func fetchPalette() {
        Task { @MainActor in
            await location.refreshPalette()
            withAnimation(.easeInOut(duration: 0.6)) { PaletteStore.apply() }
        }
    }

    // MARK: - Header

    private var headerSection: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 2) {
                    Text("d")
                        .font(.app(.largeTitle, weight: .bold))
                    if location.isAuthenticated, !location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                        Button {
                            nameInput = location.runnerName
                            showNameEntry = true
                        } label: {
                            // With a server palette the dot is the Go colour and the initials the sign background.
                            Text(location.runnerName.uppercased())
                                .font(.app(.callout, weight: .bold))
                                .foregroundStyle(AppliedPalette.shared.colors?.altBg ?? .white)
                                .frame(width: 38, height: 38)
                                .background(Circle().fill(AppliedPalette.shared.colors?.goBg ?? RunnerColorPalette.currentUser))
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Edit your name")
                    } else {
                        Text("o")
                            .font(.app(.largeTitle, weight: .bold))
                    }
                    Text("t-watchr")
                        .font(.app(.largeTitle, weight: .bold))
                }
                // The version and the palette's name, which is the release name for now. No build number or commit.
                let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String
                let paletteName = AppliedPalette.shared.colors?.name
                if version != nil || paletteName != nil {
                    Text([version.map { "v\($0)" }, paletteName].compactMap { $0 }.joined(separator: " · "))
                        .font(.app(.caption2))
                        .foregroundStyle(location.debugForceUltraConstrained ? Color.orange : Color(.tertiaryLabel))
                        .contentShape(Rectangle())
                        .onTapGesture { registerVersionTap() }
                }
            }
            Spacer()
            HStack(spacing: 16) {
                // Account (who you are). Editing the name stays on the initials dot in the wordmark.
                if location.isAuthenticated {
                    Button { showAuth = true } label: {
                        Image(systemName: "person.circle.fill")
                            .font(.app(.title))
                            .foregroundStyle(.primary)
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Account")
                }
                // App preferences; the same sheet as the gear on the map rail.
                Button { showSettings = true } label: {
                    Image(systemName: "gearshape")
                        .font(.app(.title))
                        .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Settings")
                Button { showHelp = true } label: {
                    Image(systemName: "questionmark.circle")
                        .font(.app(.title))
                        .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Help")
            }
        }
    }

    /// 10 taps within 2s of each other unlocks the Developer section in Settings — a deliberately
    /// undiscoverable way to reach test tools (force satellite mode, test notification, post
    /// stats) on TestFlight/Release builds. A single stray tap resets the count rather than
    /// accumulating forever, so it can't be triggered by accident over unrelated taps spread
    /// through a session.
    private func registerVersionTap() {
        let now = Date()
        if let lastVersionTap, now.timeIntervalSince(lastVersionTap) > 2 {
            versionTapCount = 0
        }
        lastVersionTap = now
        versionTapCount += 1
        guard versionTapCount >= 10 else { return }
        versionTapCount = 0
        location.developerOptionsUnlocked = true
        UINotificationFeedbackGenerator().notificationOccurred(.success)
    }

    // MARK: - Stale sessions

    /// A session doesn't carry over a relaunch or a sign-in: tracking never resumes by itself, so any session
    /// the server still lists is left over from an earlier run. Leave it, so the others stop seeing someone who
    /// isn't sharing any more, and the person rejoins with the invite code. Skipped while tracking, or while the
    /// share dialog is up, which means a session was only just joined.
    private func leaveStaleSessions() async {
        // With groups a membership is a group joined ahead of Go, which waits for it: keep it.
        guard !location.hasGroups else { return }
        for membership in location.memberships {
            guard !location.isTracking, !showShareConsent else { return }
            try? await location.leaveSession(sessionId: membership.sessionId)
        }
    }

    // MARK: - No Session Actions

    private func noSessionCreateSession() async {
        isBusy = true
        formError = nil
        defer { isBusy = false }

        var name = noSessionCreateCode
        var nearbySettings: NearbyJoinSettings?
        if events.isAvailable, noSessionNearbyDraft.isOn {
            guard let fix = await location.currentFix(), isUsableNearbyFix(fix) else {
                formError = "Couldn't get an accurate location, so the run can't be shown nearby. Try again with a clear view of the sky."
                return
            }
            name = noSessionNearbyDraft.trimmedTitle
            nearbySettings = NearbyJoinSettings(radiusMeters: noSessionNearbyDraft.radiusMeters, fix: fix)
        }

        do {
            try await location.createSession(
                name: name,
                displayName: location.runnerName,
                maxLengthHours: noSessionCreateLengthHours,
                nearby: nearbySettings,
                entryCode: nearbySettings == nil ? nil : noSessionNearbyDraft.entryCodeToSend)
            noSessionCreateCode = ""
            noSessionCreateLengthHours = CreateSessionView.defaultLengthHours
            noSessionNearbyDraft = NearbyCreateDraft()
            afterNearbyJoin()
        } catch DotWatcherAPIError.activeSessionExists(let sessionId, _) {
            // The sheet's entries are kept, so ending the old session can carry straight on.
            activeSessionConflict = sessionId
        } catch {
            formError = error.localizedDescription
        }
    }

    /// The person chose to end the session that was in the way, then create the new one with what they entered.
    private func endActiveSessionThenCreate(_ sessionId: String) async {
        isBusy = true
        do {
            try await location.endSession(sessionId: sessionId)
        } catch {
            formError = error.localizedDescription
            isBusy = false
            return
        }
        await noSessionCreateSession()
    }

    private func noSessionJoinSession() async {
        isBusy = true
        formError = nil
        do {
            try await location.joinInvite(code: noSessionInviteCode, displayName: location.runnerName)
            noSessionInviteCode = ""
            pendingLinkCode = nil
            afterNearbyJoin()
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

    /// After joining (a nearby tile or an invite code) or creating. With groups nothing starts: someone already out goes back to the Go map
    /// (where the group now sees them), anyone else stays here with the "You're in" card. Without groups, ask
    /// how long to share for, as before.
    private func afterNearbyJoin() {
        guard location.hasGroups else {
            offerSharingAfterJoin()
            return
        }
        Task { await events.refresh() }
        if location.isCasualTracking { casual.isMapPresented = true }
    }

    /// "Join and Go now": once the join sheet has gone, carry on to "Before you go" as holding Go would.
    private func goAfterJoin() {
        Task {
            await events.refresh()
            // Wait for the join sheet to finish dismissing, or the next sheet is dropped.
            try? await Task.sleep(for: .milliseconds(400))
            casual.isBeforeYouGoPresented = true
        }
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
