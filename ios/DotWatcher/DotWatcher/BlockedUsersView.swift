import SwiftUI

/// App Store 5.1.2(i): lets a user manage who they've blocked, account-level and cross-session.
/// Unblocking doesn't restore any session membership the block ended — same as any other
/// voluntary leave, the unblocked user would need a fresh invite to rejoin.
struct BlockedUsersView: View {
    @Bindable var location: LocationManager
    @State private var isLoading = false
    @State private var error: String?
    @State private var pendingUnblockUserId: String?

    var body: some View {
        Form {
            if location.blockedUsers.isEmpty && !isLoading {
                Section {
                    Text("You haven't blocked anyone.")
                        .foregroundStyle(.secondary)
                }
            } else {
                Section {
                    ForEach(sortedBlockedUsers) { blocked in
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(blocked.displayName)
                                Text("Blocked \(blocked.blockedAt.formattedAsDate)")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Button("Unblock") {
                                pendingUnblockUserId = blocked.userId
                            }
                            .font(.subheadline.weight(.medium))
                            .disabled(pendingUnblockUserId != nil)
                        }
                    }
                } footer: {
                    Text("Blocked users can't see your location, join sessions you own, or appear in sessions you're both a member of.")
                }
            }

            if let error {
                Section {
                    Text(error).foregroundStyle(.red)
                }
            }
        }
        .navigationTitle("Blocked Users")
        .task { await refresh() }
        .refreshable { await refresh() }
        .alert("Unblock this user?", isPresented: Binding(
            get: { pendingUnblockUserId != nil },
            set: { if !$0 { pendingUnblockUserId = nil } }
        )) {
            Button("Unblock") {
                if let userId = pendingUnblockUserId {
                    Task { await unblock(userId: userId) }
                }
            }
            Button("Cancel", role: .cancel) { pendingUnblockUserId = nil }
        } message: {
            Text("This doesn't restore any session they were removed from — they'd need a new invite to rejoin.")
        }
    }

    private var sortedBlockedUsers: [BlockedUser] {
        location.blockedUsers.sorted { $0.blockedAt > $1.blockedAt }
    }

    private func refresh() async {
        isLoading = true
        await location.loadBlockedUsers()
        isLoading = false
    }

    private func unblock(userId: String) async {
        error = nil
        do {
            try await location.unblockUser(userId: userId)
        } catch {
            self.error = error.localizedDescription
        }
        pendingUnblockUserId = nil
    }
}

private extension String {
    var formattedAsDate: String {
        guard let date = ISO8601DateFormatter().date(from: self) else { return self }
        return date.formatted(date: .abbreviated, time: .omitted)
    }
}
