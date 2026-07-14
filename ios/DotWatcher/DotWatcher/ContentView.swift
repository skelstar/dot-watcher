import SwiftUI
import UIKit

struct ContentView: View {
    @Environment(\.scenePhase) private var scenePhase
    @State private var location = LocationManager()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showAuth: Bool = false
    @State private var showUpdateRequired: Bool = false
    @State private var showLeaveConfirm: Bool = false
    @State private var showCreateSession: Bool = false
    @State private var noSessionCreateCode = ""
    @State private var noSessionInviteCode = ""
    @State private var isBusy = false
    @State private var formError: String?

    var body: some View {
        mainContent
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
                    Task { await location.loadRecentSessions() }
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
                    isBusy: isBusy,
                    isOffline: location.isOffline
                ) {
                    Task { await noSessionCreateSession() }
                }
                .presentationDetents([.height(560)])
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .fullScreenCover(isPresented: $showAuth) {
                AuthSheet(location: location)
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
            }
            .fullScreenCover(isPresented: $showUpdateRequired) {
                UpdateRequiredView()
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .onChange(of: location.updateRequired) { _, updateRequired in
                if updateRequired { showUpdateRequired = true }
            }
            .alert("Leave session?", isPresented: $showLeaveConfirm) {
                Button("Leave", role: .destructive) {
                    Task { await leaveOrDeleteSession() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if location.activeMembership != nil {
                    let suffix = location.isTracking ? " This will also stop tracking." : ""
                    Text("Are you sure you want to leave this session?\(suffix) You can rejoin later using the invite code.")
                }
            }
    }

    // MARK: - Main Content

    @ViewBuilder
    private var mainContent: some View {
        if location.isAuthenticated && location.memberships.isEmpty {
            noSessionView
        } else {
            sessionView
        }
    }

    // MARK: - No Session View

    private var noSessionView: some View {
        VStack(spacing: 0) {
            if location.isOffline {
                offlineBanner
            }
            ScrollView {
                VStack(spacing: 16) {
                    headerSection
                    runnerRow
                    noSessionJoinCard
                    if !location.recentSessions.isEmpty {
                        recentSessionsCard
                    }
                    noSessionCreateLink
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
                await location.loadSessions()
                await location.loadRecentSessions()
            }
        }
    }

    private var offlineBanner: some View {
        Label("No internet connection", systemImage: "wifi.slash")
            .font(.headline)
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 10)
            .background(Color.red)
    }

    private var noSessionCreateLink: some View {
        Button {
            showCreateSession = true
        } label: {
            Text("Session doesn't exist yet? Create one")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
    }

    private var noSessionJoinCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Join a Session")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, 4)
            VStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 10) {
                    HStack {
                        Text("Enter your invite code")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button {
                            if let pasted = UIPasteboard.general.string {
                                noSessionInviteCode = pasted
                            }
                        } label: {
                            Label("Paste", systemImage: "doc.on.clipboard")
                                .font(.subheadline)
                                .foregroundStyle(.primary)
                        }
                        .buttonStyle(.plain)
                    }
                    CodeBoxField(text: $noSessionInviteCode, length: 6, autoFocus: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 16)
                .padding(.vertical, 14)
                Divider().padding(.leading, 16)
                Button {
                    Task { await noSessionJoinSession() }
                } label: {
                    Text("Join Session")
                        .fontWeight(.semibold)
                        .foregroundStyle(isBusy || noSessionInviteCode.count < 6 ? Color.secondary : Color.primary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 14)
                }
                .buttonStyle(.plain)
                .disabled(isBusy || noSessionInviteCode.count < 6)
            }
            .background(Color(.systemGray5))
            .clipShape(RoundedRectangle(cornerRadius: 14))
        }
    }

    private var recentSessionsCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Recent Sessions")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, 4)
            VStack(spacing: 0) {
                ForEach(Array(location.recentSessions.enumerated()), id: \.element.id) { index, membership in
                    if index > 0 {
                        Divider().padding(.leading, 16)
                    }
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(membership.sessionName)
                                .font(.subheadline.weight(.medium))
                                .foregroundStyle(.primary)
                            Text("Invite \(membership.inviteCode)")
                                .font(.caption.monospaced())
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button {
                            Task { await rejoinSession(inviteCode: membership.inviteCode) }
                        } label: {
                            Text("Rejoin")
                                .font(.subheadline.weight(.semibold))
                                .foregroundStyle(.white)
                                .padding(.horizontal, 14)
                                .padding(.vertical, 8)
                                .background(Color.accentColor, in: RoundedRectangle(cornerRadius: 8))
                        }
                        .buttonStyle(.plain)
                        .disabled(isBusy)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 14)
                }
            }
            .background(Color(.systemGray5))
            .clipShape(RoundedRectangle(cornerRadius: 14))
        }
    }

    // MARK: - Session View

    private var sessionView: some View {
        ZStack(alignment: .bottom) {
            ScrollView {
                VStack(spacing: 16) {
                    headerSection
                    runnerRow
                    sessionNameCard
                }
                .padding()
            }
            .refreshable {
                // Detached: `.refreshable` cancels its closure's Task as soon as the pull
                // gesture's spinner dismisses, which on a fast local server can race ahead of
                // the network calls actually completing and cancel them mid-flight (seen as
                // NSURLErrorDomain -999 "cancelled" even though the server had already
                // responded). A detached Task isn't a child of that Task, so releasing the
                // gesture early no longer aborts the underlying requests.
                await Task {
                    await location.loadSessions()
                    await location.loadLatestPositions()
                }.value
            }
            .safeAreaInset(edge: .bottom) {
                if !location.isTracking {
                    bottomButton
                        .padding(.horizontal, 16)
                        .padding(.vertical, 12)
                        .frame(maxWidth: .infinity)
                        .background(.regularMaterial)
                }
            }
            if location.isTracking {
                DragSheet {
                    LiveMapSheet(location: location)
                }
            }
        }
    }

    // MARK: - Header

    private var headerSection: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 2) {
                    Text("d")
                        .font(.largeTitle.bold())
                    if location.isAuthenticated, !location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                        Text(location.runnerName.uppercased())
                            .font(.callout.bold())
                            .foregroundStyle(.white)
                            .frame(width: 38, height: 38)
                            .background(Circle().fill(Color.blue))
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
                        .foregroundStyle(.tertiary)
                }
            }
            Spacer()
            if let inviteCode = location.activeMembership?.inviteCode {
                let sessionUrl = location.webBaseURL.appendingPathComponent("code/\(inviteCode)")
                let shareMessage = "Join my DotWatcher session!\n\nInvite code: \(inviteCode)\n\n\(sessionUrl.absoluteString)"
                ShareLink(item: shareMessage) {
                    Image(systemName: "square.and.arrow.up")
                        .font(.title2)
                        .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
            }
            if location.isAuthenticated {
                Button { showAuth = true } label: {
                    Image(systemName: "person.crop.circle")
                        .font(.title)
                        .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
            }
            Button { showHelp = true } label: {
                Image(systemName: "questionmark.circle")
                    .font(.title)
                    .foregroundStyle(.primary)
                }
                .buttonStyle(.plain)
        }
    }

    // MARK: - Runner Row

    private var runnerRow: some View {
        HStack(spacing: 12) {
            if location.isTracking {
                TimelineView(.periodic(from: .now, by: 1.0 / 10.0)) { context in
                    let nextPostAt = location.nextPostAt(from: location.lastSent ?? Date())
                    let remaining = max(0, nextPostAt.timeIntervalSince(context.date))
                    let fraction = location.interval > 0 ? remaining / location.interval : 0

                    HStack(spacing: 8) {
                        StatusIndicatorDot(color: statusDotColor, countdownFraction: fraction)
                        Text("Sending")
                            .font(.headline)
                            .fontWeight(.bold)
                        PostCountdownRing(fraction: fraction)
                    }
                    .onTapGesture {
                        nameInput = location.runnerName
                        showNameEntry = true
                    }
                }
            } else {
                HStack(spacing: 8) {
                    StatusIndicatorDot(color: statusDotColor)
                    Text(location.status)
                        .font(.headline)
                        .fontWeight(.bold)
                }
                .onTapGesture {
                    nameInput = location.runnerName
                    showNameEntry = true
                }
            }
            Spacer()
            if location.activeMembership != nil {
                Button { showLeaveConfirm = true } label: {
                    Text("Leave")
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(.white)
                        .frame(height: 48)
                        .padding(.horizontal, 16)
                        .background(Color.red, in: RoundedRectangle(cornerRadius: 12))
                }
                .buttonStyle(.plain)
            }
            if location.activeMembership != nil {
                Button {
                    Task {
                        await location.loadSessions()
                        await location.loadLatestPositions()
                    }
                } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 20))
                        .foregroundStyle(.secondary)
                        .frame(width: 48, height: 48)
                        .background(Color(.secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))
                }
                .buttonStyle(.plain)
            } else if location.isAuthenticated {
                Button {
                    Task {
                        await location.loadSessions()
                        await location.loadRecentSessions()
                    }
                } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 20))
                        .foregroundStyle(.secondary)
                        .frame(width: 48, height: 48)
                        .background(Color(.secondarySystemBackground), in: RoundedRectangle(cornerRadius: 12))
                }
                .buttonStyle(.plain)
            }
        }
    }

    // MARK: - Session Name Card

    private var sessionNameCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Session")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)

            HStack {
                if location.sessionId.isEmpty {
                    Text("Tap to set")
                        .font(.title3.monospaced())
                        .foregroundStyle(.tertiary)
                } else {
                    Text(location.activeMembership?.sessionName ?? "")
                        .font(.title3.bold().monospaced())
                        .foregroundStyle(.primary)
                }
                Spacer()
                if location.activeMembership == nil {
                    Image(systemName: "lock")
                        .font(.subheadline)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 28)
            .padding(.vertical, 14)
            .background(Color(.tertiarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 10))

            if let inviteCode = location.activeMembership?.inviteCode {
                HStack(spacing: 6) {
                    Text("Invite")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                    Text(inviteCode)
                        .font(.title3.weight(.medium).monospaced())
                        .foregroundStyle(.primary)
                }
            }

            Divider()
                .padding(.vertical, 4)

            Text("Participants")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)

            LazyVGrid(columns: [GridItem(.adaptive(minimum: 38))], spacing: 10) {
                if location.participants.isEmpty {
                    RunnerCircle(name: location.runnerName, size: 38, fillColor: Color(.systemGray3))
                } else {
                    ForEach(location.participants, id: \.self) { name in
                        // In lobby = joined but not actually tracking: either no position posted
                        // yet at all, or (0, 0) — the placeholder Start posts when no real GPS
                        // fix is available yet.
                        let position = location.runnerPositions.first { $0.runnerName == name }
                        let isInLobby = position == nil || (position!.latitude == 0 && position!.longitude == 0)
                        RunnerCircle(
                            name: name,
                            size: 38,
                            fillColor: isInLobby ? Color(.systemGray3) : (name == location.runnerName ? .black : RunnerColorPalette.color(for: name))
                        )
                    }
                }
                let filledCount = max(1, location.participants.count)
                ForEach(0..<max(0, 5 - filledCount), id: \.self) { _ in
                    Circle()
                        .stroke(Color(.systemGray3), style: StrokeStyle(lineWidth: 2, dash: [4, 4]))
                        .frame(width: 38, height: 38)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Bottom Button

    @ViewBuilder
    private var bottomButton: some View {
        if !location.isAuthenticated {
            Button { showAuth = true } label: {
                Text("Sign in").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
        } else {
            Button {
                location.start()
            } label: {
                Text("Start tracking")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .controlSize(.large)
        }
    }

    // MARK: - No Session Actions

    private func noSessionCreateSession() async {
        isBusy = true
        formError = nil
        do {
            try await location.createSession(name: noSessionCreateCode, displayName: location.runnerName)
            noSessionCreateCode = ""
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
        } catch {
            formError = error.localizedDescription
        }
        isBusy = false
    }

    private func rejoinSession(inviteCode: String) async {
        isBusy = true
        formError = nil
        do {
            try await location.joinInvite(code: inviteCode, displayName: location.runnerName)
        } catch {
            formError = error.localizedDescription
        }
        isBusy = false
    }

    // MARK: - Leave / Delete Session

    private func leaveOrDeleteSession() async {
        guard let membership = location.activeMembership else { return }
        if location.isTracking { location.stop() }
        try? await location.leaveSession(sessionId: membership.sessionId)
    }

    // MARK: - Helpers

    private var statusDotColor: Color {
        let s = location.status
        if s == "Idle" { return .gray }
        if s == "Stopped" { return .red }
        if s.hasPrefix("Sent") || s.hasPrefix("Tracking") { return .green }
        return .orange
    }

}

#Preview {
    ContentView()
}

#Preview("First Launch") {
    let _ = UserDefaults.standard.removeObject(forKey: "runnerName")
    ContentView()
}
