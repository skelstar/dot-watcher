import SwiftUI
import UIKit

private let privacyURL = URL(string: "https://dot-watcher.skelstar.io/privacy")!
private let termsURL = URL(string: "https://dot-watcher.skelstar.io/terms")!

struct ContentView: View {
    @State private var location = LocationManager()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showSessionEntry: Bool = false
    @State private var showAuth: Bool = false
    @State private var showMembers: Bool = false

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                headerSection
                runnerRow
                sessionNameCard
                statusCard
                participantsCard
            }
            .padding()
        }
        .safeAreaInset(edge: .bottom) {
            bottomButton
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
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
        .sheet(isPresented: $showSessionEntry) {
            SessionEntrySheet(location: location)
        }
        .sheet(isPresented: $showAuth) {
            AuthSheet(location: location)
                .interactiveDismissDisabled(!location.isAuthenticated)
        }
        .sheet(isPresented: $showMembers) {
            MemberManagementSheet(location: location)
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
        RunnerCircle(name: location.runnerName, size: 56, isHighlighted: true)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
            .onTapGesture {
                nameInput = location.runnerName
                showNameEntry = true
            }
    }

    // MARK: - Session Name Card

    private var sessionNameCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("SESSION NAME")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)

            HStack(spacing: 0) {
                if location.sessionCode.isEmpty {
                    Text("Tap to set")
                        .font(.title3.monospaced())
                        .foregroundStyle(.tertiary)
                } else {
                    Text(location.sessionCode)
                        .font(.title3.bold().monospaced())
                        .foregroundStyle(.primary)
                }
                Spacer()
                if let role = location.activeMembership?.role {
                    Text(role.uppercased())
                        .font(.caption2.bold())
                        .foregroundStyle(.secondary)
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
            .onTapGesture { showSessionEntry = true }

            if !location.sessionCode.isEmpty,
               let url = URL(string: "https://dot-watcher.skelstar.io/\(location.fullSessionName)") {
                Link("Open map in browser", destination: url)
                    .font(.subheadline)
                    .foregroundStyle(Color.accentColor)
            }

            if location.activeMembership?.role == "owner",
               let inviteCode = location.activeMembership?.inviteCode {
                Text("Invite \(inviteCode)")
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
                Button("Manage members") {
                    showMembers = true
                }
                .font(.subheadline)
                .overlay(alignment: .topTrailing) {
                    if location.pendingJoinRequestCount > 0 {
                        Text("\(location.pendingJoinRequestCount)")
                            .font(.caption2.bold())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 5)
                            .padding(.vertical, 2)
                            .background(.red, in: Capsule())
                            .offset(x: 16, y: -10)
                    }
                }
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
            Text("PARTICIPANTS")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)
            if location.participants.isEmpty {
                Text("No participants yet")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
                    ForEach(location.participants, id: \.self) { participant in
                        RunnerCircle(
                            name: participant,
                            size: 56,
                            isHighlighted: participant == location.runnerName
                        )
                    }
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
        if location.isTracking {
            Button { location.stop() } label: {
                Text("Stop").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.red)
            .controlSize(.large)
        } else if !location.isAuthenticated {
            Button { showAuth = true } label: {
                Text("Sign in").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
        } else {
            Button {
                if location.canTrackSelectedSession {
                    location.start()
                } else {
                    showSessionEntry = true
                }
            } label: {
                Text(location.canTrackSelectedSession ? "Start tracking" : "Choose tracking session")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.green)
            .controlSize(.large)
        }
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

// MARK: - Auth Sheet

struct AuthSheet: View {
    var location: LocationManager

    @State private var mode: AuthMode = .signIn
    @State private var username: String = ""
    @State private var password: String = ""
    @State private var displayName: String = ""
    @State private var error: String?
    @State private var isBusy = false
    @State private var showDeleteConfirmation = false
    @Environment(\.dismiss) private var dismiss

    enum AuthMode: Hashable {
        case signIn
        case register
    }

    var body: some View {
        NavigationStack {
            Form {
                if location.isAuthenticated {
                    Section {
                        LabeledContent("User", value: location.currentUser?.displayName ?? location.currentUser?.username ?? "")
                        Button("Sign out", role: .destructive) {
                            Task { await location.signOut() }
                        }
                        Button("Delete account", role: .destructive) {
                            showDeleteConfirmation = true
                        }
                        .disabled(isBusy)
                    }
                    legalSection
                } else {
                    Section {
                        Picker("Mode", selection: $mode) {
                            Text("Sign In").tag(AuthMode.signIn)
                            Text("Create").tag(AuthMode.register)
                        }
                        .pickerStyle(.segmented)

                        if mode == .register {
                            TextField("Display name", text: $displayName)
                                .textContentType(.name)
                        }
                        TextField("Username", text: $username)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .textContentType(.username)
                        SecureField("Password", text: $password)
                            .textContentType(.password)
                    }

                    Section {
                        Button(mode == .signIn ? "Sign In" : "Create Account") {
                            Task { await submit() }
                        }
                        .disabled(isBusy || username.trimmingCharacters(in: .whitespaces).isEmpty || password.count < 8 || (mode == .register && displayName.trimmingCharacters(in: .whitespaces).isEmpty))
                    }
                    legalSection
                }

                if let error {
                    Section {
                        Text(error)
                            .foregroundStyle(.red)
                    }
                }
            }
            .navigationTitle("Account")
            .toolbar {
                if location.isAuthenticated {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Done") { dismiss() }
                    }
                }
            }
            .alert("Delete account?", isPresented: $showDeleteConfirmation) {
                Button("Delete", role: .destructive) {
                    Task { await deleteAccount() }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("This removes your account, memberships, owned sessions, and stored location rows linked to your account. This cannot be undone.")
            }
        }
    }

    private var legalSection: some View {
        Section {
            Link("Privacy Policy", destination: privacyURL)
            Link("Terms of Use", destination: termsURL)
        } footer: {
            Text("Dot Watcher is a beta service. Location data can be delayed, inaccurate, or unavailable.")
        }
    }

    private func submit() async {
        isBusy = true
        error = nil
        do {
            switch mode {
            case .signIn:
                try await location.signIn(username: username, password: password)
            case .register:
                try await location.register(username: username, password: password, displayName: displayName)
            }
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }

    private func deleteAccount() async {
        isBusy = true
        error = nil
        do {
            try await location.deleteAccount()
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }
}

// MARK: - Session Entry Sheet

struct SessionEntrySheet: View {
    var location: LocationManager

    @State private var createCode: String = ""
    @State private var inviteCode: String = ""
    @State private var error: String?
    @State private var isBusy = false
    @State private var browsableSessions: [BrowsableSession] = []
    @State private var isBrowseLoading = false
    @State private var requestedSessionCodes: Set<String> = []
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                if !location.memberships.isEmpty {
                    Section("Sessions") {
                        ForEach(location.memberships) { membership in
                            Button {
                                location.selectSession(membership)
                                dismiss()
                            } label: {
                                HStack {
                                    VStack(alignment: .leading, spacing: 3) {
                                        Text(membership.sessionCode)
                                            .font(.body.monospaced().bold())
                                        Text(membership.displayName)
                                            .font(.caption)
                                            .foregroundStyle(.secondary)
                                        if membership.role == "owner" {
                                            Text("Invite \(membership.inviteCode)")
                                                .font(.caption2.monospaced())
                                                .foregroundStyle(.tertiary)
                                        }
                                    }
                                    Spacer()
                                    Text(membership.role.uppercased())
                                        .font(.caption2.bold())
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }

                Section("Create") {
                    HStack(spacing: 6) {
                        CodeBoxField(text: $createCode)
                        Text(location.dateSuffix)
                            .font(.title2.bold().monospaced())
                            .foregroundStyle(.secondary)
                    }
                    Button("Create Session") {
                        Task { await createSession() }
                    }
                    .disabled(isBusy)
                }

                Section("Join") {
                    TextField("Invite code", text: $inviteCode)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .onChange(of: inviteCode) { _, new in
                            let filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber || $0 == "-" || $0 == "_" }.prefix(32))
                            if filtered != new { inviteCode = filtered }
                        }
                    Button("Join Session") {
                        Task { await joinSession() }
                    }
                    .disabled(isBusy || inviteCode.trimmingCharacters(in: .whitespaces).isEmpty)
                }

                Section {
                    HStack {
                        Text("Browse Active Sessions")
                            .font(.headline)
                        Spacer()
                        if isBrowseLoading {
                            ProgressView()
                        } else {
                            Button("Refresh") {
                                Task { await loadBrowseSessions() }
                            }
                            .font(.subheadline)
                        }
                    }
                    let nonMemberSessions = browsableSessions.filter { s in
                        !location.memberships.contains { $0.sessionCode == s.sessionCode }
                    }
                    if nonMemberSessions.isEmpty && !isBrowseLoading {
                        Text("No active sessions found")
                            .foregroundStyle(.secondary)
                            .font(.subheadline)
                    } else {
                        ForEach(nonMemberSessions) { session in
                            HStack {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(session.sessionCode)
                                        .font(.body.monospaced().bold())
                                    Text("\(session.ownerDisplayName) · \(session.memberCount) member\(session.memberCount == 1 ? "" : "s")")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                                Spacer()
                                if requestedSessionCodes.contains(session.sessionCode) {
                                    Text("Requested")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                } else {
                                    Button("Request") {
                                        Task { await requestJoin(session) }
                                    }
                                    .disabled(isBusy)
                                }
                            }
                        }
                    }
                }

                if let error {
                    Section {
                        Text(error)
                            .foregroundStyle(.red)
                    }
                }
            }
            .navigationTitle("Session")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .task {
                await location.loadSessions()
                if createCode.isEmpty {
                    createCode = suggestedCode
                }
                if displayName.isEmpty {
                    displayName = location.runnerName
                }
                await loadBrowseSessions()
            }
            .task {
                while !Task.isCancelled {
                    try? await Task.sleep(for: .seconds(10))
                    guard !Task.isCancelled else { break }
                    await location.loadSessions()
                }
            }
        }
    }

    private var suggestedCode: String {
        let trimmed = location.runnerName.trimmingCharacters(in: .whitespacesAndNewlines)
        return String((trimmed.isEmpty ? "RUN" : trimmed.uppercased()).prefix(6))
    }

    private func createSession() async {
        isBusy = true
        error = nil
        do {
            let fullCode = createCode.isEmpty ? "" : createCode + location.dateSuffix
            try await location.createSession(code: fullCode, displayName: location.runnerName)
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }

    private func joinSession() async {
        isBusy = true
        error = nil
        do {
            try await location.joinInvite(code: inviteCode, displayName: location.runnerName)
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }

    private func loadBrowseSessions() async {
        isBrowseLoading = true
        do {
            browsableSessions = try await location.browseSessions()
        } catch {
            // ignore browse errors silently
        }
        isBrowseLoading = false
    }

    private func requestJoin(_ session: BrowsableSession) async {
        isBusy = true
        error = nil
        let name = displayName.trimmingCharacters(in: .whitespacesAndNewlines)
        do {
            _ = try await location.requestToJoin(sessionCode: session.sessionCode, displayName: name.isEmpty ? nil : name)
            requestedSessionCodes.insert(session.sessionCode)
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }
}

// MARK: - Code Box Field

private struct CodeBoxField: View {
    @Binding var text: String
    @FocusState private var isFocused: Bool

    var body: some View {
        HStack(spacing: 6) {
            ForEach(0..<6, id: \.self) { i in
                let chars = Array(text)
                let char = chars.count > i ? String(chars[i]) : ""
                let isActive = isFocused && chars.count == i
                ZStack {
                    RoundedRectangle(cornerRadius: 8)
                        .fill(Color(.tertiarySystemBackground))
                    RoundedRectangle(cornerRadius: 8)
                        .strokeBorder(
                            isActive ? Color.accentColor : Color(.separator),
                            lineWidth: isActive ? 2 : 1
                        )
                    Text(char)
                        .font(.title2.bold().monospaced())
                }
                .frame(width: 36, height: 44)
            }
        }
        .overlay(
            TextField("", text: $text)
                .focused($isFocused)
                .opacity(0.01)
                .keyboardType(.asciiCapable)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .onChange(of: text) { _, new in
                    let filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber }.prefix(6))
                    if filtered != new { text = filtered }
                }
        )
        .contentShape(Rectangle())
        .onTapGesture { isFocused = true }
    }
}

// MARK: - Member Management Sheet

struct MemberManagementSheet: View {
    var location: LocationManager

    @State private var error: String?
    @State private var busyUserId: String?
    @State private var busyRequestId: String?
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                if let membership = location.activeMembership {
                    Section("Session") {
                        LabeledContent("Code", value: membership.sessionCode)
                        LabeledContent("Invite", value: membership.inviteCode)
                    }
                }

                if !location.pendingJoinRequests.isEmpty {
                    Section("Join Requests") {
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
                                Button("Approve") {
                                    Task { await approve(request) }
                                }
                                .disabled(busyRequestId == request.requestId)
                                .tint(.green)
                                Button("Deny") {
                                    Task { await deny(request) }
                                }
                                .disabled(busyRequestId == request.requestId)
                                .tint(.red)
                            }
                        }
                    }
                }

                Section("Members") {
                    if location.selectedSessionMembers.isEmpty {
                        Text("No members yet")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(location.selectedSessionMembers) { member in
                            HStack {
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(member.displayName)
                                    Text(member.role.uppercased())
                                        .font(.caption2.bold())
                                        .foregroundStyle(.secondary)
                                }
                                Spacer()
                                if member.role != "owner" {
                                    Button(member.role == "runner" ? "Make Viewer" : "Make Runner") {
                                        Task { await update(member) }
                                    }
                                    .disabled(busyUserId == member.userId)
                                }
                            }
                        }
                    }
                }

                if let error {
                    Section {
                        Text(error)
                            .foregroundStyle(.red)
                    }
                }
            }
            .navigationTitle("Members")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .task {
                await location.loadSelectedSessionMembers()
            }
        }
    }

    private func update(_ member: SessionMember) async {
        busyUserId = member.userId
        error = nil
        do {
            try await location.updateMemberRole(
                member,
                role: member.role == "runner" ? "viewer" : "runner")
        } catch {
            self.error = error.localizedDescription
        }
        busyUserId = nil
    }

    private func approve(_ request: JoinRequest) async {
        busyRequestId = request.requestId
        error = nil
        do {
            try await location.approveJoinRequest(request)
        } catch {
            self.error = error.localizedDescription
        }
        busyRequestId = nil
    }

    private func deny(_ request: JoinRequest) async {
        busyRequestId = request.requestId
        error = nil
        do {
            try await location.denyJoinRequest(request)
        } catch {
            self.error = error.localizedDescription
        }
        busyRequestId = nil
    }
}

// MARK: - Name Entry Sheet

struct NameEntryView: View {
    @Binding var name: String
    let isFirstLaunch: Bool
    let onConfirm: () -> Void
    @Environment(\.dismiss) private var dismiss
    @FocusState private var focused: Bool

    private var trimmedName: String { name.trimmingCharacters(in: .whitespaces) }

    var body: some View {
        VStack(spacing: 24) {
            RunnerCircle(name: name, size: 72)

            Text(isFirstLaunch ? "Welcome to DotWatcher" : "Enter Initials")
                .font(.title2.bold())

            Text("Enter your initials so others can find you on the map.")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)

            HStack(spacing: 6) {
                ForEach(0..<3, id: \.self) { i in
                    CodeBox(
                        character: character(at: i),
                        isActive: focused && name.count == i
                    )
                }
            }
            .onTapGesture { focused = true }
            .overlay {
                TextField("", text: $name)
                    .focused($focused)
                    .keyboardType(.alphabet)
                    .autocorrectionDisabled()
                    .textInputAutocapitalization(.characters)
                    .opacity(0)
                    .frame(width: 1, height: 1)
                    .onChange(of: name) { _, new in
                        let filtered = String(new.uppercased().filter { $0.isLetter && $0.isASCII }.prefix(3))
                        if filtered != new { name = filtered }
                    }
            }

            Button("Save") {
                onConfirm()
            }
            .buttonStyle(.borderedProminent)
            .disabled(trimmedName.isEmpty)

            if !isFirstLaunch {
                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                .foregroundStyle(.secondary)
            }
        }
        .padding(32)
        .onAppear { focused = true }
    }

    private func character(at index: Int) -> Character? {
        guard index < name.count else { return nil }
        return name[name.index(name.startIndex, offsetBy: index)]
    }
}

// MARK: - Code Box

struct CodeBox: View {
    let character: Character?
    let isActive: Bool

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 8)
                .fill(Color(.systemBackground))
            RoundedRectangle(cornerRadius: 8)
                .stroke(isActive ? Color.accentColor : Color.secondary.opacity(0.4), lineWidth: isActive ? 2 : 1.5)

            if let char = character {
                Text(String(char))
                    .font(.title.bold())
            } else if isActive {
                Rectangle()
                    .frame(width: 2, height: 22)
                    .foregroundStyle(Color.accentColor)
            }
        }
        .frame(width: 40, height: 54)
    }
}

// MARK: - Help View

struct HelpView: View {
    @Environment(\.dismiss) private var dismiss

    private var rawMarkdown: String {
        guard let url = Bundle.main.url(forResource: "Help", withExtension: "md"),
              let content = try? String(contentsOf: url, encoding: .utf8)
        else { return "Help unavailable." }
        return content
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(rawMarkdown.components(separatedBy: "\n").enumerated()), id: \.offset) { _, line in
                    if line.hasPrefix("# ") {
                        Text(String(line.dropFirst(2)))
                            .font(.title2.bold())
                            .padding(.top, 8)
                            .padding(.bottom, 4)
                    } else if line.hasPrefix("## ") {
                        Text(String(line.dropFirst(3)))
                            .font(.headline)
                            .underline()
                            .padding(.top, 16)
                            .padding(.bottom, 2)
                    } else if line.isEmpty {
                        Color.clear.frame(height: 4)
                    } else {
                        Text((try? AttributedString(markdown: line)) ?? AttributedString(line))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(24)
        }
        .safeAreaInset(edge: .bottom) {
            Button("Done") { dismiss() }
                .buttonStyle(.borderedProminent)
                .padding()
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
    }
}

// MARK: - Runner Circle

struct RunnerCircle: View {
    let name: String
    var size: CGFloat = 40
    var isHighlighted: Bool = false

    private static let highlightColor = Color.accentColor

    var body: some View {
        ZStack {
            Circle()
                .fill(isHighlighted ? Self.highlightColor : Color(.systemGray4))
                .frame(width: size, height: size)
            Text(name.trimmingCharacters(in: .whitespaces).isEmpty ? "?" : name)
                .font(.system(size: size * 0.3, weight: .bold))
                .foregroundStyle(isHighlighted ? .white : .primary)
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

#Preview("Session Entry") {
    SessionEntrySheet(location: LocationManager())
}

#Preview("Help") {
    HelpView()
}

#Preview("Participants Dots") {
    let names = ["SKE", "JOH", "CHQ", "ALI", "ROS", "TOM", "BEA", "WIL", "ZOE", "MAX"]
    VStack(alignment: .leading, spacing: 10) {
        Text("PARTICIPANTS")
            .font(.caption)
            .fontWeight(.semibold)
            .foregroundStyle(.secondary)
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
            ForEach(names, id: \.self) { name in
                RunnerCircle(name: name, size: 56, isHighlighted: name == "SKE")
            }
        }
    }
    .frame(maxWidth: .infinity, alignment: .topLeading)
    .padding(16)
    .background(Color(.secondarySystemBackground))
    .clipShape(RoundedRectangle(cornerRadius: 14))
    .padding()
}
