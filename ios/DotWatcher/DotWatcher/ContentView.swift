import SwiftUI
import UIKit

struct ContentView: View {
    @State private var location = LocationManager()
    @State private var batteryLevel: Float = UIDevice.current.batteryLevel
    @State private var showNameEntry: Bool = false
    @State private var nameInput: String = ""
    @State private var showHelp: Bool = false
    @State private var showSessionEntry: Bool = false
    @State private var showAuth: Bool = false
    @State private var busyRequestId: String?
    @State private var requestPendingDeny: JoinRequest?
    @State private var memberError: String?

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                headerSection
                runnerRow
                sessionNameCard
                if location.activeMembership?.role == "owner" && !location.pendingJoinRequests.isEmpty {
                    joinRequestsCard
                }
                statusCard
                if !location.pendingJoinRequests.isEmpty {
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
        .alert("Deny request?", isPresented: Binding(
            get: { requestPendingDeny != nil },
            set: { if !$0 { requestPendingDeny = nil } }
        )) {
            Button("Deny", role: .destructive) {
                if let request = requestPendingDeny {
                    Task { try? await location.denyJoinRequest(request) }
                }
                requestPendingDeny = nil
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            if let request = requestPendingDeny {
                Text("Deny \(request.displayName)'s request to join?")
            }
        }
        .sheet(isPresented: $showHelp) {
            HelpView()
        }
        .sheet(isPresented: $showNameEntry, onDismiss: {
            if pendingSessionEntry {
                pendingSessionEntry = false
                showSessionEntry = true
            }
        }) {
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
        .fullScreenCover(isPresented: $showAuth) {
            AuthSheet(location: location)
        }
        .onChange(of: location.isAuthenticated) { _, isAuthenticated in
            guard isAuthenticated else {
                showAuth = true
                return
            }
            if location.runnerName.trimmingCharacters(in: .whitespaces).isEmpty {
                pendingSessionEntry = true
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
        .onChange(of: location.pendingJoinRequestCount) { _, newCount in
            if newCount > 0 {
                Task { await location.loadJoinRequests() }
            }
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
        RunnerCircle(name: location.isAuthenticated ? location.runnerName : "??", size: 56, isHighlighted: true)
            .onTapGesture {
                nameInput = location.runnerName
                showNameEntry = true
            }
            .frame(maxWidth: .infinity)
    }

    // MARK: - Session Name Card

    private var sessionNameCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("SESSION NAME")
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
            .onTapGesture {
                if location.isAuthenticated { showSessionEntry = true }
                else { showAuth = true }
            }

            if !location.sessionId.isEmpty,
               let url = URL(string: "https://dot-watcher.skelstar.io/\(location.sessionId)") {
                HStack(spacing: 10) {
                    Spacer()
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

            if let inviteCode = location.activeMembership?.inviteCode,
               location.activeMembership?.role == "owner" {
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
                    Button("Approve") {
                        Task {
                            busyRequestId = request.requestId
                            try? await location.approveJoinRequest(request)
                            busyRequestId = nil
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(.green)
                    .disabled(busyRequestId == request.requestId)
                    Button("Deny") {
                        requestPendingDeny = request
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(.red)
                    .disabled(busyRequestId == request.requestId)
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
            Text("PARTICIPANTS")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundStyle(.secondary)
            if location.sessionRunnerNames.isEmpty {
                Text("No participants yet")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
                    ForEach(location.sessionRunnerNames, id: \.self) { name in
                        RunnerCircle(
                            name: name,
                            size: 56,
                            isHighlighted: location.participants.contains(name) || name == location.runnerName
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
            Button { showStopConfirm = true } label: {
                Text("Stop").frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(.red)
            .controlSize(.large)
            .alert("Stop tracking?", isPresented: $showStopConfirm) {
                Button("Stop & Leave Session", role: .destructive) {
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
