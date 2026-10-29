import CoreImage.CIFilterBuiltins
import SwiftUI
import UIKit

// The crew UI: the "Your crew" row on the start screen, and one sheet (presented once at the top of
// `ContentView` by `CrewHost`, so a crew link can open it from any screen) holding the crew, the add
// screen, the "your code" screen and the "Add X to your crew?" confirmation. See
// .ai/plans/crew-management.md. Kept self-contained (state in `CrewStore`) so the row moves unchanged
// when iOS adopts Android's main-screen layout.

// MARK: - Avatar

/// A person as a coloured circle with their initials. The dark ring around it is the "crew mark": a
/// circle with a ring is someone in your crew, wherever it appears (the start-screen row, the crew
/// sheet and, later, the map). The ring is never the only signal: callers give it an accessibility
/// label naming the person.
struct CrewDot: View {
    let initials: String
    let color: Color
    var size: CGFloat = 34
    var isCrew = true

    private static let ring: CGFloat = 2
    private static let gap: CGFloat = 2

    var body: some View {
        let face = Circle()
            .fill(color)
            .frame(width: size, height: size)
            .overlay(
                Text(initials)
                    .font(.system(size: size * 0.34, weight: .bold))
                    .foregroundStyle(.white)
            )
        if isCrew {
            face
                .padding(Self.gap + Self.ring)
                .overlay(Circle().strokeBorder(Color.primary, lineWidth: Self.ring))
        } else {
            face
        }
    }
}

private func crewDot(for member: CrewMember, color: Color, size: CGFloat, ringed: Bool = true) -> some View {
    CrewDot(
        initials: RunnerCircle.initials(for: member.displayName),
        color: color,
        size: size,
        isCrew: ringed
    )
    .accessibilityElement(children: .ignore)
    .accessibilityLabel("\(member.label), in your crew")
}

// MARK: - Start-screen row

/// The slim "Your crew" row: up to four faces, "+N" for the
/// rest, and a count. Tapping opens the crew sheet. Draws nothing until the server has said it has
/// crew, so an app running against an older server shows no trace of it.
struct CrewRow: View {
    var crew: CrewStore

    var body: some View {
        if crew.isAvailable {
            let summary = CrewFormat.rowSummary(crew.members)
            Button {
                crew.isFlowPresented = true
            } label: {
                HStack(spacing: 12) {
                    if summary.faces.isEmpty {
                        Circle()
                            .strokeBorder(Color.secondary, lineWidth: 2)
                            .frame(width: 34, height: 34)
                            .overlay(Image(systemName: "plus").foregroundStyle(.secondary))
                            .accessibilityHidden(true)
                    } else {
                        HStack(spacing: -10) {
                            ForEach(summary.faces) { member in
                                // No ring here: the row is already "Your crew", and rings overlapping
                                // looked messy. A card-coloured edge keeps overlapped dots apart, and
                                // 38 is the outer size the ringed 30 dot used to have.
                                crewDot(for: member, color: crew.color(for: member.userId), size: 38, ringed: false)
                                    .overlay(Circle().strokeBorder(Color(.systemGray5), lineWidth: 2))
                            }
                        }
                        if summary.extra > 0 {
                            Text("+\(summary.extra)")
                                .font(.caption.weight(.bold))
                                .foregroundStyle(.secondary)
                        }
                    }
                    VStack(alignment: .leading, spacing: 2) {
                        Text(summary.title)
                            .font(.headline)
                        if !summary.meta.isEmpty {
                            Text(summary.meta)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        ForEach(CrewFormat.rowNotes(newMembers: crew.newMemberIds.count, pendingInvites: crew.pendingInvites), id: \.self) { note in
                            Text(note)
                                .font(.caption.weight(.semibold))
                                .foregroundStyle(Color.accentColor)
                        }
                    }
                    Spacer()
                    Image(systemName: "chevron.right")
                        .foregroundStyle(.tertiary)
                        .accessibilityHidden(true)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 10)
                .frame(maxWidth: .infinity)
                .background(Color(.systemGray5))
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .contentShape(RoundedRectangle(cornerRadius: 14))
            }
            .buttonStyle(.plain)
            .accessibilityHint("Opens your crew")
        }
    }
}

// MARK: - Host

/// Presents the crew sheet from the top of the app and keeps the crew in step with sign-in and with
/// the app coming to the front (there are no push notifications, so a code someone redeems shows up
/// on the next load).
struct CrewHost: ViewModifier {
    @Bindable var crew: CrewStore
    var location: LocationManager
    /// True while the casual map covers the screen. A sheet can't present from beneath a full-screen
    /// cover, so the map presents the crew sheet itself and this one stands down.
    var yieldsToCasualMap = false
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: Binding(
                get: { crew.isFlowPresented && !yieldsToCasualMap },
                set: { crew.isFlowPresented = $0 }
            )) {
                CrewFlowView(crew: crew, webBaseURL: location.webBaseURL)
                    .presentationDetents([.large])
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .task {
                crew.attach(location)
                await crew.refresh()
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if isAuthenticated {
                    // A crew link opened before sign-in is handled now, as session invite links are.
                    crew.consumePendingLink()
                    Task { await crew.refresh() }
                } else {
                    crew.clear()
                }
            }
            .onChange(of: scenePhase) { _, phase in
                if phase == .active { Task { await crew.refresh() } }
            }
    }
}

// MARK: - Sheet

private enum CrewRoute: Hashable {
    case add
    case myCode
}

struct CrewFlowView: View {
    var crew: CrewStore
    let webBaseURL: URL
    @State private var path: [CrewRoute] = []

    var body: some View {
        ZStack {
            NavigationStack(path: $path) {
                CrewListView(
                    crew: crew,
                    onAdd: { path.append(.add) },
                    onDone: { crew.isFlowPresented = false }
                )
                .navigationDestination(for: CrewRoute.self) { route in
                    switch route {
                    case .add:
                        AddCrewView(crew: crew, onShowMyCode: { path.append(.myCode) })
                    case .myCode:
                        MyCrewCodeView(crew: crew, webBaseURL: webBaseURL)
                    }
                }
            }
            if let confirm = crew.confirm {
                CrewConfirmOverlay(crew: crew, confirm: confirm, onFinished: { path = [] })
            }
        }
        .onChange(of: crew.isFlowPresented) { _, isPresented in
            if !isPresented { path = [] }
        }
    }
}

// MARK: - Crew list

struct CrewListView: View {
    var crew: CrewStore
    let onAdd: () -> Void
    let onDone: () -> Void

    @State private var selectedId: String?
    @State private var memberToRemove: CrewMember?

    private var selected: CrewMember? {
        crew.members.first { $0.userId == selectedId }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                // Opening the crew is what clears the start screen's "joined" note.
                Color.clear.frame(height: 0).onAppear { crew.markMembersSeen() }
                Text("People you have chosen. They see you in their crew too, and you can remove each other at any time. Removing someone takes effect straight away.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                Text("In your crew · \(crew.members.count)")
                    .font(.headline)

                if crew.members.isEmpty {
                    Text("No one yet. Ask someone to send you a crew code, or send them yours.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 72), spacing: 8)], spacing: 8) {
                        ForEach(crew.members) { member in
                            memberButton(member)
                        }
                    }
                    if let selected {
                        detailCard(selected)
                    } else {
                        Text("Tap someone to see their details.")
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }

                if let error = crew.removeError {
                    Text(error)
                        .font(.subheadline)
                        .foregroundStyle(.red)
                }

                Button(action: onAdd) {
                    Text("Add someone")
                        .fontWeight(.semibold)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
            }
            .padding(24)
        }
        .navigationTitle("Your crew")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Done", action: onDone)
            }
        }
        .refreshable { await crew.refresh() }
        .confirmationDialog(
            "Remove \(memberToRemove?.label ?? "") from your crew?",
            isPresented: Binding(
                get: { memberToRemove != nil },
                set: { if !$0 { memberToRemove = nil } }
            ),
            titleVisibility: .visible
        ) {
            if let member = memberToRemove {
                Button("Remove", role: .destructive) {
                    selectedId = nil
                    Task { await crew.remove(member.userId) }
                }
                Button("Cancel", role: .cancel) {}
            }
        } message: {
            Text("They'll no longer be in your crew and you won't be in theirs. They aren't told.")
        }
    }

    private func memberButton(_ member: CrewMember) -> some View {
        let isSelected = member.userId == selectedId
        return Button {
            selectedId = isSelected ? nil : member.userId
        } label: {
            VStack(spacing: 4) {
                crewDot(for: member, color: crew.color(for: member.userId), size: 38)
                Text(member.label)
                    .font(.caption2)
                    .lineLimit(1)
                    .foregroundStyle(isSelected ? Color.primary : Color.secondary)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 8)
            .background(isSelected ? Color(.systemGray5) : Color.clear, in: RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
    }

    private func detailCard(_ member: CrewMember) -> some View {
        HStack(spacing: 12) {
            crewDot(for: member, color: crew.color(for: member.userId), size: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(member.label)
                    .font(.headline)
                Text("Crew \(CrewFormat.sinceLabel(member.since))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("Remove", role: .destructive) {
                memberToRemove = member
            }
            .font(.subheadline.weight(.semibold))
        }
        .padding(12)
        .background(Color(.systemGray5), in: RoundedRectangle(cornerRadius: 12))
    }
}

// MARK: - Add

/// Enter a code someone sent you, or go and show your own. There is no scanner here: to scan a QR
/// code people open their phone's camera, which opens the app through the link the code holds.
struct AddCrewView: View {
    var crew: CrewStore
    let onShowMyCode: () -> Void

    @State private var code = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("Ask them to send you their crew code, then enter it here. If they show you a QR code, just open your camera and point it at the code.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack {
                    Text("Enter their crew code")
                        .font(.subheadline.weight(.semibold))
                    Spacer()
                    Button {
                        if let pasted = UIPasteboard.general.string {
                            code = CrewLink.extractFromPaste(pasted)
                                ?? String(pasted.trimmingCharacters(in: .whitespacesAndNewlines).prefix(CrewLink.codeLength)).uppercased()
                        }
                    } label: {
                        Label("Paste", systemImage: "doc.on.clipboard")
                            .font(.subheadline)
                    }
                    .buttonStyle(.plain)
                }

                CodeBoxField(
                    text: $code,
                    length: CrewLink.codeLength,
                    autoFocus: true,
                    extractCode: CrewLink.extractFromPaste
                )

                Button {
                    Task { await crew.beginAdd(code) }
                } label: {
                    Text("Add")
                        .fontWeight(.semibold)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(code.count != CrewLink.codeLength || crew.confirm != nil)

                Button("Show my code instead", action: onShowMyCode)
                    .frame(maxWidth: .infinity)
            }
            .padding(20)
        }
        .navigationTitle("Add to your crew")
        .navigationBarTitleDisplayMode(.inline)
    }
}

// MARK: - My code

/// The code to send to one person: a QR for in person, the code itself, and a Share button. The first
/// time, it asks what name the crew should see (skippable; the account's initials are used otherwise).
struct MyCrewCodeView: View {
    var crew: CrewStore
    let webBaseURL: URL

    @State private var naming: Bool
    @State private var draft = ""
    @State private var isSaving = false

    init(crew: CrewStore, webBaseURL: URL) {
        self.crew = crew
        self.webBaseURL = webBaseURL
        // Naming comes first only when no nickname is set, and only once per opening of the screen.
        _naming = State(initialValue: crew.nickname == nil)
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 14) {
                if naming {
                    namePrompt
                } else {
                    codeContent
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity)
        }
        .navigationTitle("Your crew code")
        .navigationBarTitleDisplayMode(.inline)
        .task(id: naming) {
            if !naming { await crew.loadMyCode() }
        }
    }

    private var namePrompt: some View {
        VStack(spacing: 14) {
            Text("What should your crew call you?")
                .font(.title3.bold())
                .multilineTextAlignment(.center)
            Text("People you add see this name. If you skip it they see your initials. You can change it later in Settings.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            TextField("Your name", text: $draft)
                .textFieldStyle(.roundedBorder)
                .textInputAutocapitalization(.words)
                .onChange(of: draft) { _, new in
                    crew.nicknameError = nil
                    if new.count > CrewFormat.maxNicknameLength {
                        draft = String(new.prefix(CrewFormat.maxNicknameLength))
                    }
                }
            if let error = crew.nicknameError {
                Text(error)
                    .font(.footnote)
                    .foregroundStyle(.red)
            }
            Button {
                Task {
                    isSaving = true
                    if await crew.setNickname(draft) { naming = false }
                    isSaving = false
                }
            } label: {
                Text("Continue")
                    .fontWeight(.semibold)
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(isSaving || CrewFormat.cleanNickname(draft) == nil)
            Button("Skip") { naming = false }
                .foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private var codeContent: some View {
        if let code = crew.myCode {
            let link = CrewLink.url(base: webBaseURL, code: code.code)
            let spaced = code.code.map(String.init).joined(separator: " ")
            QRCodeView(text: link.absoluteString)
                .frame(width: 220, height: 220)
                .accessibilityLabel("QR code for crew code \(spaced)")
            Text(String(code.code.prefix(4)) + "  " + String(code.code.dropFirst(4)))
                .font(.system(size: 30, weight: .bold, design: .monospaced))
                .accessibilityLabel("Crew code \(spaced)")
            Text(CrewFormat.expiryLabel(code.expiresAt))
                .font(.footnote)
                .foregroundStyle(.secondary)
            Text("Send this to one person. It works once.")
                .font(.subheadline.weight(.semibold))
                .multilineTextAlignment(.center)
            Text("In person, they can scan the QR code with their camera. Otherwise share it as a message.")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            ShareLink(item: CrewLink.shareMessage(base: webBaseURL, code: code.code)) {
                Label("Share code", systemImage: "square.and.arrow.up")
                    .fontWeight(.semibold)
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            Button("Make a new code") {
                Task { await crew.newMyCode() }
            }
            .disabled(crew.isLoadingMyCode)
            Text("Making a new code stops this one working.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        } else if crew.isLoadingMyCode {
            ProgressView()
                .padding(32)
        } else {
            Text(crew.myCodeError ?? "Couldn't make a code. Try again.")
                .foregroundStyle(.red)
                .multilineTextAlignment(.center)
            Button("Try again") {
                Task { await crew.loadMyCode() }
            }
        }
    }
}

/// A QR code drawn black on white whatever the app theme (a scanner needs the contrast), on a white
/// rounded plate with its own quiet zone. It is only ever shown, never scanned by the app: people scan
/// it with the phone's camera app, which opens the link it holds.
struct QRCodeView: View {
    let text: String

    var body: some View {
        if let image = Self.makeImage(text) {
            Image(uiImage: image)
                .interpolation(.none)
                .resizable()
                .scaledToFit()
                .padding(10)
                .background(Color.white, in: RoundedRectangle(cornerRadius: 12))
        }
    }

    private static func makeImage(_ text: String) -> UIImage? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(text.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage else { return nil }
        let scaled = output.transformed(by: CGAffineTransform(scaleX: 12, y: 12))
        guard let cgImage = CIContext().createCGImage(scaled, from: scaled.extent) else { return nil }
        return UIImage(cgImage: cgImage)
    }
}

// MARK: - Confirmation

/// "Add Priya to your crew? They'll see you too." The one place anyone is added: a crew link, a pasted
/// code and a typed code all end here, and nothing happens until the person taps Add. It is drawn over
/// the crew sheet (rather than as an alert) so it can show progress and results without fighting
/// SwiftUI's rule about presenting from a view that is already presenting.
struct CrewConfirmOverlay: View {
    var crew: CrewStore
    let confirm: CrewConfirm
    let onFinished: () -> Void

    var body: some View {
        ZStack {
            Color.black.opacity(0.35)
                .ignoresSafeArea()
            VStack(spacing: 14) {
                content
            }
            .padding(24)
            .frame(maxWidth: 320)
            .background(Color(.systemBackground), in: RoundedRectangle(cornerRadius: 20))
            .shadow(radius: 20)
            .padding(24)
            .accessibilityAddTraits(.isModal)
        }
    }

    @ViewBuilder
    private var content: some View {
        switch confirm {
        case .loading:
            ProgressView()
            Text("Checking the code…")
                .font(.headline)

        case .adding:
            ProgressView()
            Text("Adding to your crew…")
                .font(.headline)

        case .ready(_, let owner):
            if owner.alreadyCrew {
                title("\(owner.label) is already in your crew")
                Text("You don't need to use this code.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                okButton
            } else {
                title("Add \(owner.label) to your crew?")
                Text("They'll see you in their crew too. You can each remove the other at any time.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                Button {
                    Task { await crew.confirmAdd() }
                } label: {
                    Text("Add")
                        .fontWeight(.semibold)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                Button("Cancel") { crew.dismissConfirm() }
                    .foregroundStyle(.secondary)
            }

        case .done(let member):
            title("\(member.label) is in your crew")
            Button {
                crew.dismissConfirm()
                onFinished()
            } label: {
                Text("OK")
                    .fontWeight(.semibold)
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)

        case .failed(let message):
            title("Couldn't add them")
            Text(message)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
            okButton
        }
    }

    private func title(_ text: String) -> some View {
        Text(text)
            .font(.headline)
            .multilineTextAlignment(.center)
    }

    private var okButton: some View {
        Button {
            crew.dismissConfirm()
        } label: {
            Text("OK")
                .fontWeight(.semibold)
                .frame(maxWidth: .infinity)
        }
        .buttonStyle(.borderedProminent)
        .controlSize(.large)
    }
}
