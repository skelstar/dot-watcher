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
    @State private var busyRequestId: String?
    @State private var requestPendingDeny: JoinRequest?
    @State private var memberError: String?
    @State private var noSessionCreateCode = ""
    @State private var noSessionInviteCode = ""
    @State private var browsableSessions: [BrowsableSession] = []
    @State private var isBrowseLoading = false
    @State private var requestedSessionIds: Set<String> = []
    @State private var isBusy = false
    @State private var formError: String?

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
            .alert("Deny request?", isPresented: Binding(
                get: { requestPendingDeny != nil },
                set: { if !$0 { requestPendingDeny = nil } }
            )) {
                Button("Deny", role: .destructive) {
                    if let request = requestPendingDeny {
                        Task { await deny(request) }
                    }
                    requestPendingDeny = nil
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if let request = requestPendingDeny {
                    Text("Deny \(request.displayName)'s request to join?")
                }
            }
            .alert("Leave session?", isPresented: $showLeaveConfirm) {
                Button("Leave", role: .destructive) {
                    Task { await leaveOrDeleteSession() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if let membership = location.activeMembership {
                    Text("Are you sure you want to leave \(membership.sessionName)?")
                }
            }
            .onChange(of: location.pendingJoinRequestCount) { _, newCount in
                if newCount > 0 {
                    Task { await location.loadJoinRequests() }
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
                noSessionCreateCard
                noSessionBrowseCard
                noSessionJoinCard
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
            await loadBrowsableSessions()
        }
        .task { await loadBrowsableSessions() }
    }

    private var noSessionCreateCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Create")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, 4)
            VStack(spacing: 0) {
                CodeBoxField(text: $noSessionCreateCode, length: 8)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 14)
                Divider().padding(.leading, 16)
                Button {
                    Task { await noSessionCreateSession() }
                } label: {
                    Text("Create Session")
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 14)
                }
                .disabled(isBusy || noSessionCreateCode.count < 4)
            }
            .background(Color(.secondarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 14))
        }
    }

    private var noSessionBrowseCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Browse Active Sessions")
                    .font(.headline)
                Spacer()
                Button {
                    Task { await loadBrowsableSessions() }
                } label: {
                    if isBrowseLoading {
                        ProgressView()
                    } else {
                        Image(systemName: "arrow.clockwise")
                            .foregroundStyle(.tint)
                    }
                }
                .disabled(isBrowseLoading)
            }
            if browsableSessions.isEmpty && !isBrowseLoading {
                Text("No active sessions found")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(browsableSessions) { session in
                    HStack {
                        VStack(alignment: .leading, spacing: 3) {
                            Text(session.sessionName)
                                .font(.body.monospaced().bold())
                            Text("\(session.ownerDisplayName) · \(session.memberCount) member\(session.memberCount == 1 ? "" : "s")")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        if requestedSessionIds.contains(session.sessionId) {
                            Text("Requested")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        } else {
                            Button("Request") {
                                Task { await noSessionRequestJoin(session) }
                            }
                            .disabled(isBusy)
                        }
                    }
                }
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    private var noSessionJoinCard: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Join")
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.leading, 4)
            VStack(spacing: 0) {
                TextField("Invite code", text: $noSessionInviteCode)
                    .textInputAutocapitalization(.characters)
                    .autocorrectionDisabled()
                    .onChange(of: noSessionInviteCode) { _, new in
                        let filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }.prefix(32))
                        if filtered != new { noSessionInviteCode = filtered }
                    }
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
                .disabled(isBusy || noSessionInviteCode.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .background(Color(.secondarySystemBackground))
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
                if location.activeMembership?.role == "runner" && !location.pendingJoinRequests.isEmpty {
                    joinRequestsCard
                }
                participantsCard
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
            if location.isAuthenticated {
                Button { showAuth = true } label: {
                    Image(systemName: "person.crop.circle")
                        .font(.title2)
                        .foregroundStyle(.secondary)
                }
            }
            Button { showHelp = true } label: {
                Image(systemName: "questionmark.circle")
                    .font(.title2)
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
            if !location.sessionId.isEmpty,
               let url = URL(string: "https://dot-watcher.skelstar.io/\(location.sessionId)") {
                ShareLink(item: url) {
                    Image(systemName: "square.and.arrow.up")
                        .font(.system(size: 20))
                        .foregroundStyle(.white)
                        .frame(width: 48, height: 48)
                        .background(Color(.systemBlue), in: RoundedRectangle(cornerRadius: 12))
                }
                Link(destination: url) {
                    Image(systemName: "map.fill")
                        .font(.system(size: 20))
                        .foregroundStyle(.white)
                        .frame(width: 48, height: 48)
                        .background(Color(red: 0.2, green: 0.78, blue: 0.35), in: RoundedRectangle(cornerRadius: 12))
                }
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

            HStack(spacing: 0) {
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
                if location.activeMembership != nil {
                    Button("Leave") { showLeaveConfirm = true }
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 6)
                        .background(.red, in: Capsule())
                } else {
                    Image(systemName: "lock")
                        .font(.subheadline)
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 14)
            .background(Color(.tertiarySystemBackground))
            .clipShape(RoundedRectangle(cornerRadius: 10))
            .contentShape(RoundedRectangle(cornerRadius: 10))

            if let inviteCode = location.activeMembership?.inviteCode {
                Text("Invite \(inviteCode)")
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
            }
        }
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

    // MARK: - Join Requests Card

    private var joinRequestsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("JOIN REQUESTS")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)
            ForEach(location.pendingJoinRequests) { request in
                HStack {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(request.displayName)
                            .font(.body)
                        Text("Requested \(request.formattedDate)")
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                    Spacer()
                    HStack(spacing: 4) {
                        Button {
                            Task { await approve(request) }
                        } label: {
                            Image(systemName: "checkmark.circle.fill")
                                .font(.system(size: 44))
                                .foregroundStyle(.green)
                        }
                        .buttonStyle(.plain)
                        .disabled(busyRequestId == request.requestId)
                        Button {
                            requestPendingDeny = request
                        } label: {
                            Image(systemName: "xmark.circle.fill")
                                .font(.system(size: 44))
                                .foregroundStyle(.red)
                        }
                        .buttonStyle(.plain)
                        .disabled(busyRequestId == request.requestId)
                    }
                }
            }
            if let memberError {
                Text(memberError)
                    .font(.caption)
                    .foregroundStyle(.red)
            }
        }
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

    private var participantsCard: some View {
        VStack(alignment: .leading, spacing: 10) {
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
                    ZStack {
                        Circle()
                            .strokeBorder(Color(.systemGray3), lineWidth: 2)
                        Text("??")
                            .font(.system(size: 56 * 0.3, weight: .bold))
                            .foregroundStyle(Color(.systemGray3))
                    }
                    .frame(width: 56, height: 56)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .padding(16)
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }

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

    private func noSessionRequestJoin(_ session: BrowsableSession) async {
        isBusy = true
        formError = nil
        do {
            _ = try await location.requestToJoin(sessionId: session.sessionId, displayName: nil)
            requestedSessionIds.insert(session.sessionId)
        } catch {
            formError = error.localizedDescription
        }
        isBusy = false
    }

    private func loadBrowsableSessions() async {
        guard !isBrowseLoading else { return }
        isBrowseLoading = true
        do {
            browsableSessions = try await location.browseSessions()
        } catch {
            // ignore browse errors silently
        }
        isBrowseLoading = false
    }

    // MARK: - Leave / Delete Session

    private func leaveOrDeleteSession() async {
        guard let membership = location.activeMembership else { return }
        if location.isTracking { location.stop() }
        do {
            try await location.leaveSession(sessionId: membership.sessionId)
        } catch {
            memberError = error.localizedDescription
        }
    }

    // MARK: - Join Request Actions

    private func approve(_ request: JoinRequest) async {
        busyRequestId = request.requestId
        memberError = nil
        do {
            try await location.approveJoinRequest(request)
        } catch {
            memberError = error.localizedDescription
        }
        busyRequestId = nil
    }

    private func deny(_ request: JoinRequest) async {
        busyRequestId = request.requestId
        memberError = nil
        do {
            try await location.denyJoinRequest(request)
        } catch {
            memberError = error.localizedDescription
        }
        busyRequestId = nil
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
