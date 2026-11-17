import SwiftUI

/// Signed in: the Account sheet (who the person is: initials, crew name, blocked users, sign out,
/// delete account), opened from the person icon on the start screen. Signed out: the sign-in /
/// create-account screen. App preferences live in `SettingsSheet`.
struct AuthSheet: View {
    @Bindable var location: LocationManager
    var crew: CrewStore

    @State private var nicknameDraft = ""
    @State private var isSavingNickname = false
    @State private var mode: AuthMode = .signIn
    @State private var username: String = ""
    @State private var password: String = ""
    @State private var displayName: String = ""
    @State private var error: String?
    @State private var isBusy = false
    @State private var showDeleteConfirmation = false

    private var isValidEmail: Bool {
        username.trimmingCharacters(in: .whitespaces)
            .range(of: #"^[^@\s]+@[^@\s]+\.[^@\s]+$"#, options: .regularExpression) != nil
    }
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
                        LabeledContent("User", value: location.currentUser?.username ?? "")
                        LabeledContent("Initials") {
                            CodeBoxField(text: $location.runnerName, length: 2, lettersOnly: true)
                        }
                    }
                    crewSection
                    Section("Privacy") {
                        NavigationLink("Blocked Users") {
                            BlockedUsersView(location: location)
                        }
                    }
                    Section {
                        Button("Sign out", role: .destructive) {
                            Task { await location.signOut() }
                        }
                        Button("Delete account", role: .destructive) {
                            showDeleteConfirmation = true
                        }
                        .disabled(isBusy)
                    }
                } else {
                    Section {
                        Picker("Mode", selection: $mode) {
                            Text("Sign In").tag(AuthMode.signIn)
                            Text("Create").tag(AuthMode.register)
                        }
                        .pickerStyle(.segmented)

                        if mode == .register {
                            VStack(alignment: .leading, spacing: 6) {
                                Text("Your initials")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                                CodeBoxField(text: $displayName, length: 2, lettersOnly: true)
                            }
                            .padding(.vertical, 4)
                        }
                        TextField(mode == .register ? "Email address" : "Username or email", text: $username)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()
                            .keyboardType(mode == .register ? .emailAddress : .default)
                            .textContentType(mode == .register ? .emailAddress : .username)
                        if mode == .register {
                            if !username.isEmpty && !isValidEmail {
                                Text("Enter a valid email address")
                                    .font(.caption)
                                    .foregroundStyle(.orange)
                            }
                        } else if !username.isEmpty && username.count < 3 {
                            Text("Username must be at least 3 characters")
                                .font(.caption)
                                .foregroundStyle(.orange)
                        }
                        SecureField("Password", text: $password)
                            .textContentType(.password)
                    }

                    Section {
                        Button(mode == .signIn ? "Sign In" : "Create Account") {
                            Task { await submit() }
                        }
                        .disabled(isBusy || username.trimmingCharacters(in: .whitespaces).isEmpty || password.count < 8 || (mode == .register && (displayName.count != 2 || !isValidEmail)))
                    }
                    Section {
                        Link("Privacy Policy", destination: LegalLinks.privacy)
                        Link("Terms of Use", destination: LegalLinks.terms)
                    } footer: {
                        Text(LegalLinks.disclaimer)
                    }
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

    /// The name the person's crew sees. Only offered once the server has crew.
    @ViewBuilder
    private var crewSection: some View {
        if crew.isAvailable {
            Section {
                TextField("Name your crew sees", text: $nicknameDraft)
                    .textInputAutocapitalization(.words)
                    .onChange(of: nicknameDraft) { _, new in
                        crew.nicknameError = nil
                        if new.count > CrewFormat.maxNicknameLength {
                            nicknameDraft = String(new.prefix(CrewFormat.maxNicknameLength))
                        }
                    }
                Button("Save name") {
                    Task {
                        isSavingNickname = true
                        await crew.setNickname(nicknameDraft)
                        isSavingNickname = false
                    }
                }
                .disabled(isSavingNickname || nicknameDraft.trimmingCharacters(in: .whitespaces) == (crew.nickname ?? ""))
                if let error = crew.nicknameError {
                    Text(error).foregroundStyle(.red)
                }
            } header: {
                Text("Crew")
            } footer: {
                Text("Shown to people in your crew. Leave blank to show your initials.")
            }
            .onAppear { nicknameDraft = crew.nickname ?? "" }
            .onChange(of: crew.nickname) { _, new in nicknameDraft = new ?? "" }
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
