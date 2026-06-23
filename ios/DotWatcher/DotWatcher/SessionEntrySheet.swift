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

                Section("Create") {
                    HStack(spacing: 6) {
                        CodeBoxField(text: $createCode)
                    }
                    if !createCode.isEmpty && createCode.count < 3 {
                        Text("Session name must be at least 3 characters")
                            .font(.caption)
                            .foregroundStyle(.orange)
                    }
                    Button("Create Session") {
                        Task { await createSession() }
                    }
                    .disabled(isBusy || createCode.count < 3)
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
                        !location.memberships.contains { $0.sessionId == s.sessionId }
                    }
                    if nonMemberSessions.isEmpty && !isBrowseLoading {
                        Text("No active sessions found")
                            .foregroundStyle(.secondary)
                            .font(.subheadline)
                    } else {
                        ForEach(nonMemberSessions) { session in
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
            .task {
                await location.loadSessions()
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
        } catch {
            self.error = error.localizedDescription
        }
        isBusy = false
    }
}


#Preview("Session Entry") {
    SessionEntrySheet(location: LocationManager())
}
