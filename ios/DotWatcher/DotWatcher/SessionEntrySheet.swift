import SwiftUI

struct SessionEntrySheet: View {
    var location: LocationManager

    @State private var createCode: String = ""
    @State private var inviteCode: String = ""
    @State private var error: String?
    @State private var isBusy = false
    @State private var browsableSessions: [BrowsableSession] = []
    @State private var isBrowseLoading = false
    @State private var requestedSessionIds: Set<String> = []
    @State private var sessionToDelete: SessionMembership?
    @State private var sessionToLeave: SessionMembership?
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
                                        Text(membership.sessionName)
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
                                    if membership.role != "owner" {
                                        Button {
                                            sessionToLeave = membership
                                        } label: {
                                            Image(systemName: "xmark.circle.fill")
                                                .font(.system(size: 28))
                                                .foregroundStyle(.red)
                                        }
                                        .buttonStyle(.plain)
                                    }
                                }
                            }
                            .swipeActions(edge: .trailing) {
                                if membership.role == "owner" {
                                    Button(role: .destructive) {
                                        sessionToDelete = membership
                                    } label: {
                                        Label("Delete", systemImage: "trash")
                                    }
                                }
                            }
                        }
                    }
                }

                if location.memberships.isEmpty {
                    Section("Create") {
                        HStack(spacing: 6) {
                            CodeBoxField(text: $createCode, length: 8)
                        }
                        if !createCode.isEmpty && createCode.count < 4 {
                            Text("Session name must be at least 4 characters")
                                .font(.caption)
                                .foregroundStyle(.orange)
                        }
                        Button("Create Session") {
                            Task { await createSession() }
                        }
                        .disabled(isBusy || createCode.count < 4)
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
                }

                Section {
                    HStack {
                        Text("Sessions")
                            .font(.headline)
                        Spacer()
                        Button {
                            Task {
                                async let sessions: () = location.loadSessions()
                                async let browse: () = loadBrowseSessions()
                                await sessions
                                await browse
                            }
                        } label: {
                            if isBrowseLoading {
                                ProgressView()
                            } else {
                                Image(systemName: "arrow.clockwise")
                            }
                        }
                        .disabled(isBrowseLoading)
                    }
                    let visibleSessions = browsableSessions.filter { s in
                        !location.memberships.contains { $0.sessionId == s.sessionId && $0.role != "owner" }
                    }
                    if visibleSessions.isEmpty && !isBrowseLoading {
                        Text("No sessions found")
                            .foregroundStyle(.secondary)
                            .font(.subheadline)
                    } else {
                        ForEach(visibleSessions) { session in
                            let isOwned = location.memberships.contains { $0.sessionId == session.sessionId && $0.role == "owner" }
                            HStack {
                                VStack(alignment: .leading, spacing: 3) {
                                    HStack(spacing: 4) {
                                        Text(session.sessionName)
                                            .font(.body.monospaced().bold())
                                        if isOwned {
                                            Image(systemName: "star.fill")
                                                .font(.caption)
                                                .foregroundStyle(.yellow)
                                        }
                                    }
                                    Text("\(session.ownerDisplayName) · \(session.memberCount) member\(session.memberCount == 1 ? "" : "s")")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                                Spacer()
                                if isOwned {
                                    Text("Owner")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                } else if requestedSessionIds.contains(session.sessionId) {
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
            .alert("Delete session?", isPresented: .init(
                get: { sessionToDelete != nil },
                set: { if !$0 { sessionToDelete = nil } }
            )) {
                Button("Delete", role: .destructive) {
                    if let s = sessionToDelete {
                        Task { await deleteSession(s) }
                    }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if let s = sessionToDelete {
                    Text("This permanently deletes \(s.sessionName) and all its members, location data, and join requests. This cannot be undone.")
                }
            }
            .alert("Leave session?", isPresented: .init(
                get: { sessionToLeave != nil },
                set: { if !$0 { sessionToLeave = nil } }
            )) {
                Button("Leave", role: .destructive) {
                    if let s = sessionToLeave {
                        Task { await leaveSession(s) }
                    }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                if let s = sessionToLeave {
                    Text("Are you sure you want to leave \(s.sessionName)?")
                }
            }
            .task {
                async let sessions: () = location.loadSessions()
                async let browse: () = loadBrowseSessions()
                await sessions
                await browse
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if !isAuthenticated { dismiss() }
            }
        }
    }

    private func createSession() async {
        isBusy = true
        error = nil
        do {
            try await location.createSession(name: createCode, displayName: location.runnerName)
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

    private func leaveSession(_ membership: SessionMembership) async {
        isBusy = true
        error = nil
        sessionToLeave = nil
        do {
            try await location.leaveSession(sessionId: membership.sessionId)
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }

    private func deleteSession(_ membership: SessionMembership) async {
        isBusy = true
        error = nil
        sessionToDelete = nil
        do {
            try await location.deleteSession(sessionId: membership.sessionId)
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }

    private func loadBrowseSessions() async {
        guard !isBrowseLoading else { return }
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
        do {
            _ = try await location.requestToJoin(sessionId: session.sessionId, displayName: nil)
            requestedSessionIds.insert(session.sessionId)
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }
}


#Preview("Session Entry") {
    SessionEntrySheet(location: LocationManager())
}
