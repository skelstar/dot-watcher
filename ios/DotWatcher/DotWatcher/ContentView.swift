import SwiftUI
import UIKit

struct ContentView: View {
    @State private var location = LocationManager()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showAuth: Bool = false
    @State private var showStopConfirm: Bool = false
    @State private var showLeaveConfirm: Bool = false
    @State private var showCreateSession: Bool = false
    @State private var noSessionCreateCode = ""
    @State private var noSessionInviteCode = ""
    @State private var isBusy = false
    @State private var formError: String?
    @State private var sessionRowSwipeOffset: CGFloat = 0
    @GestureState private var sessionRowDragOffset: CGFloat = 0

    var body: some View {
        mainContent
            .onAppear {
                UIDevice.current.isBatteryMonitoringEnabled = true
                batteryLevel = UIDevice.current.batteryLevel
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
            .sheet(isPresented: $showHelp) {
                HelpView()
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
            }
            .sheet(isPresented: $showCreateSession) {
                CreateSessionView(
                    sessionCode: $noSessionCreateCode,
                    isBusy: isBusy
                ) {
                    Task { await noSessionCreateSession() }
                }
            }
            .fullScreenCover(isPresented: $showAuth) {
                AuthSheet(location: location)
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
            .alert("Leave session?", isPresented: $showLeaveConfirm) {
                Button("Leave", role: .destructive) {
                    Task { await leaveOrDeleteSession() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if let membership = location.activeMembership {
                    Text("Are you sure you want to leave this session? You can rejoin later using the invite code.")
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
        ScrollView {
            VStack(spacing: 16) {
                headerSection
                runnerRow
                noSessionJoinCard
                noSessionCreateLink
                if let formError {
                    Text(formError)
                        .font(.caption)
                        .foregroundStyle(.red)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 4)
                }
            }
            .padding()
        }
        .refreshable {
            await location.loadSessions()
        }
    }

    private var noSessionCreateLink: some View {
        Button {
            showCreateSession = true
        } label: {
            Text("Have your own session? Create one")
                .font(.footnote)
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
                CodeBoxField(text: $noSessionInviteCode, length: 6, autoFocus: true)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 14)
                Divider().padding(.leading, 16)
                Button {
                    Task { await noSessionJoinSession() }
                } label: {
                    Text("Join Session")
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 14)
                }
                .disabled(isBusy || noSessionInviteCode.count < 6)
            }
            .background(Color(.systemGray5))
            .clipShape(RoundedRectangle(cornerRadius: 14))
        }
    }

    // MARK: - Session View

    private var sessionView: some View {
        ScrollView {
            VStack(spacing: 16) {
                headerSection
                runnerRow
                sessionNameCard
            }
            .padding()
        }
        .refreshable {
            await location.loadSessions()
            await location.loadSessionRunners()
        }
        .safeAreaInset(edge: .bottom) {
            bottomButton
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
    }

    // MARK: - Header

    private var headerSection: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 4) {
                Text("DotWatcher")
                    .font(.largeTitle.bold())
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
                let sessionUrl = "https://dot-watcher.skelstar.io/code/\(inviteCode)"
                let shareMessage = "Join my DotWatcher session!\n\nInvite code: \(inviteCode)\n\n\(sessionUrl)"
                ShareLink(item: shareMessage) {
                    Image(systemName: "square.and.arrow.up")
                        .font(.title2)
                        .foregroundStyle(.secondary)
                }
            }
            if location.isAuthenticated {
                Button { showAuth = true } label: {
                    Image(systemName: "person.crop.circle")
                        .font(.title)
                        .foregroundStyle(.secondary)
                }
            }
            Button { showHelp = true } label: {
                Image(systemName: "questionmark.circle")
                    .font(.title)
                    .foregroundStyle(.secondary)
                }
        }
    }

    // MARK: - Runner Row

    private var runnerRow: some View {
        HStack(spacing: 12) {
            RunnerCircle(name: location.isAuthenticated ? location.runnerName : "??", size: 56, isHighlighted: true)
                .padding(.vertical, 8)
                .onTapGesture {
                    nameInput = location.runnerName
                    showNameEntry = true
                }
            HStack(spacing: 8) {
                Circle()
                    .fill(statusDotColor)
                    .frame(width: 10, height: 10)
                Text(location.status)
                    .font(.headline)
                    .fontWeight(.bold)
            }
            Spacer()
            if location.activeMembership != nil {
                Button {
                    Task {
                        await location.loadSessions()
                        await location.loadSessionRunners()
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

            let rightActionW: CGFloat = 72
            let btnW: CGFloat = 64
            let leftActionW: CGFloat = btnW * 2
            ZStack(alignment: .leading) {
                // Single background: share + map on left, Leave on right
                HStack(spacing: 0) {
                    if !location.sessionId.isEmpty,
                       let inviteCode = location.activeMembership?.inviteCode {
                        let sessionUrl = "https://dot-watcher.skelstar.io/code/\(inviteCode)"
                        let shareMessage = "Join my DotWatcher session!\n\nInvite code: \(inviteCode)\n\n\(sessionUrl)"
                        ShareLink(item: shareMessage) {
                            Image(systemName: "square.and.arrow.up")
                                .font(.system(size: 22))
                                .foregroundStyle(Color(.label))
                                .frame(width: btnW)
                                .frame(maxHeight: .infinity)
                                .background(Color(.systemBackground))
                        }
                        .simultaneousGesture(TapGesture().onEnded {
                            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { sessionRowSwipeOffset = 0 }
                        })
                        if let url = URL(string: sessionUrl) {
                            Button {
                                withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { sessionRowSwipeOffset = 0 }
                                UIApplication.shared.open(url)
                            } label: {
                                Image("MapIcon")
                                    .resizable()
                                    .scaledToFit()
                                    .frame(width: 36, height: 36)
                                    .foregroundStyle(.white)
                                    .frame(width: btnW)
                                    .frame(maxHeight: .infinity)
                                    .background(Color(.systemGray))
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    Spacer()
                    // Leave
                    Button {
                        withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { sessionRowSwipeOffset = 0 }
                        showLeaveConfirm = true
                    } label: {
                        Image(systemName: "rectangle.portrait.and.arrow.right")
                            .font(.system(size: 22))
                            .foregroundStyle(.white)
                            .frame(width: rightActionW)
                            .frame(maxHeight: .infinity)
                            .background(Color(.systemRed))
                    }
                    .buttonStyle(.plain)
                }
                .frame(maxHeight: .infinity)

                // Foreground row content
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
                .overlay(alignment: .leading) {
                    let opacity = max(0.0, 1.0 - abs(sessionRowSwipeOffset + sessionRowDragOffset) / 20.0)
                    Image(systemName: "chevron.left")
                        .font(.system(size: 16, weight: .light))
                        .foregroundStyle(Color(.systemGray2))
                        .padding(.leading, 8)
                        .opacity(opacity)
                }
                .overlay(alignment: .trailing) {
                    let opacity = max(0.0, 1.0 - abs(sessionRowSwipeOffset + sessionRowDragOffset) / 20.0)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 16, weight: .light))
                        .foregroundStyle(Color(.systemGray2))
                        .padding(.trailing, 8)
                        .opacity(opacity)
                }
                .offset(x: max(-rightActionW, min(leftActionW, sessionRowSwipeOffset + sessionRowDragOffset)))
                .gesture(
                    DragGesture(minimumDistance: 10)
                        .updating($sessionRowDragOffset) { value, state, _ in
                            state = value.translation.width
                        }
                        .onEnded { value in
                            let current = max(-rightActionW, min(leftActionW, sessionRowSwipeOffset + value.translation.width))
                            let projected = current + value.velocity.width * 0.15
                            sessionRowSwipeOffset = current
                            withAnimation(.spring(response: 0.3, dampingFraction: 0.9)) {
                                if projected < -20 {
                                    sessionRowSwipeOffset = -rightActionW
                                } else if projected > 20 {
                                    sessionRowSwipeOffset = leftActionW
                                } else {
                                    sessionRowSwipeOffset = 0
                                }
                            }
                        }
                )
            }
            .clipShape(RoundedRectangle(cornerRadius: 10))

            if let inviteCode = location.activeMembership?.inviteCode {
                Text("Invite \(inviteCode)")
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
            }

            Divider()
                .padding(.vertical, 4)

            Text("Participants")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)

            LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
                if location.sessionRunnerNames.isEmpty {
                    RunnerCircle(name: location.runnerName, size: 56, isHighlighted: true)
                } else {
                    ForEach(location.sessionRunnerNames, id: \.self) { name in
                        RunnerCircle(
                            name: name,
                            size: 56,
                            isHighlighted: location.participants.contains(name) || name == location.runnerName
                        )
                    }
                }
                let filledCount = max(1, location.sessionRunnerNames.count)
                ForEach(0..<max(0, 5 - filledCount), id: \.self) { _ in
                    Circle()
                        .stroke(Color(.systemGray3), style: StrokeStyle(lineWidth: 2, dash: [4, 4]))
                        .frame(width: 56, height: 56)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Status Card

    private var statusCard: some View {
        VStack(spacing: 10) {
            HStack {
                HStack(spacing: 8) {
                    Circle()
                        .fill(statusDotColor)
                        .frame(width: 10, height: 10)
                    Text(location.status)
                        .font(.headline)
                }
                Spacer()
                Text("every 15s")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Button {
                    Task {
                        await location.loadSessions()
                        await location.loadSessionRunners()
                    }
                } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 18, weight: .semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 40, height: 40)
                        .background(Color(.white), in: Circle())
                }
                .buttonStyle(.plain)
            }
            HStack {
                if batteryLevel >= 0 {
                    Label("\(Int(batteryLevel * 100))%", systemImage: batteryIcon)
                        .font(.caption)
                        .foregroundStyle(batteryLevel < 0.2 ? .red : .secondary)
                }
                Spacer()
                if let sent = location.lastSent {
                    Text("Last sent \(sent.formatted(date: .omitted, time: .standard))")
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Participants Card

    // MARK: - Status Bar (no-session bottom)

    private var statusBarContent: some View {
        HStack {
            HStack(spacing: 8) {
                Circle()
                    .fill(statusDotColor)
                    .frame(width: 10, height: 10)
                Text(location.status)
                    .font(.headline)
            }
            Spacer()
            Text("every 15s")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Bottom Button

    @ViewBuilder
    private var bottomButton: some View {
        if location.isTracking {
            Button { showStopConfirm = true } label: {
                Text("Stop").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.red)
            .controlSize(.large)
            .alert("Stop tracking?", isPresented: $showStopConfirm) {
                Button("Stop & Leave", role: .destructive) {
                    Task { await location.stopAndLeave() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                let name = location.activeMembership?.sessionName ?? "this session"
                Text("This will stop tracking and remove you from \(name). You can rejoin later using the invite code.")
            }
        } else if !location.isAuthenticated {
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

    private var batteryIcon: String {
        switch batteryLevel {
        case ..<0.25: return "battery.25"
        case ..<0.50: return "battery.50"
        case ..<0.75: return "battery.75"
        default:      return "battery.100"
        }
    }
}

#Preview {
    ContentView()
}

#Preview("First Launch") {
    let _ = UserDefaults.standard.removeObject(forKey: "runnerName")
    ContentView()
}
