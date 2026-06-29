import SwiftUI

struct MemberManagementSheet: View {
    var location: LocationManager

    @State private var error: String?
    @State private var busyUserId: String?
    @State private var busyRequestId: String?
    @State private var requestPendingDeny: JoinRequest?
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Form {
                if let membership = location.activeMembership {
                    Section("Session") {
                        LabeledContent("Name", value: membership.sessionName)
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
                                if member.role != "owner" && location.activeMembership?.role == "owner" {
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
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if !isAuthenticated { dismiss() }
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
