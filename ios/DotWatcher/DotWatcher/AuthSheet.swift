import SwiftUI

private let privacyURL = URL(string: "https://dot-watcher.skelstar.io/privacy")!
private let termsURL = URL(string: "https://dot-watcher.skelstar.io/terms")!

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
