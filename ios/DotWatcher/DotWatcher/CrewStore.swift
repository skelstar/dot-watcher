import Foundation
import SwiftUI
import Observation

// Crew (server: Controllers/CrewController.cs). A crew is the people you have chosen: mutual, and tied
// to the account. People are added only by redeeming a one-time code the other person sent them. The
// server never returns usernames here, so people are shown by nickname, falling back to initials.
// See .ai/plans/crew-management.md.

struct CrewMember: Codable, Identifiable, Hashable {
    var id: String { userId }

    let userId: String
    let nickname: String?
    let displayName: String
    /// ISO-8601 UTC time the two became crew.
    let since: String
}

/// A one-time code for the caller to send to one person; usable once, for 24 hours.
struct CrewCode: Codable, Equatable {
    let code: String
    let expiresAt: String
}

/// Whose code this is, for the confirm screen. Previewing adds no one.
struct CrewCodePreview: Codable {
    let userId: String
    let nickname: String?
    let displayName: String
    let alreadyCrew: Bool
}

private struct CrewOverviewResponse: Decodable {
    let crew: [CrewMember]
}

private struct ProfileResponse: Decodable {
    let nickname: String?
}

/// The "Add Priya to your crew?" confirmation and what follows it. A link or a typed code leads to
/// `.loading` (previewing the code), then `.ready`; nobody is added until the person taps Add.
enum CrewConfirm {
    case loading
    case ready(code: String, owner: CrewCodePreview)
    case adding(owner: CrewCodePreview)
    case done(CrewMember)
    case failed(String)
}

/// Crew state and actions for the signed-in account. Shares `LocationManager`'s network stack
/// (headers, token, the 426 version gate) rather than having its own.
@Observable
@MainActor
final class CrewStore {
    enum Availability { case unknown, available, unavailable }

    /// Whether this server has crew for this account. Until the server is updated, or while the account
    /// hasn't been given the crew feature, `/me/crew` is a 404, and the UI then shows nothing rather
    /// than an error. Re-checked on sign-in and whenever the app comes to the front, so a newly
    /// granted account sees crew without signing in again.
    private(set) var availability: Availability = .unknown
    private(set) var members: [CrewMember] = []
    /// The dot colour for someone in your crew; see `CrewColorPalette`.
    func color(for userId: String) -> Color {
        CrewColorPalette.assign(members.map(\.userId) + [userId])[userId] ?? .gray
    }
    /// The name this account's crew sees; nil until chosen (they then see the initials).
    private(set) var nickname: String?
    var nicknameError: String?
    /// The code the person is currently offering, if any.
    private(set) var myCode: CrewCode?
    private(set) var isLoadingMyCode = false
    private(set) var myCodeError: String?
    var removeError: String?
    private(set) var confirm: CrewConfirm?
    /// Drives the crew sheet, which is presented once at the top of `ContentView` (see `CrewHost`)
    /// so a crew link can open it from any screen.
    var isFlowPresented = false

    var isAvailable: Bool { availability == .available }

    /// A code that arrived (as a link) before anyone was signed in; handled once they are.
    @ObservationIgnored private var pendingLinkCode: String?
    /// True when a link, not the person, opened the sheet, so it closes again after the confirmation.
    @ObservationIgnored private var openedByLink = false
    @ObservationIgnored private weak var location: LocationManager?

    func attach(_ location: LocationManager) {
        self.location = location
    }

    // MARK: - Loading

    /// Loads the crew and the person's own nickname. Safe to call often (on appear, on becoming
    /// active, on pull-to-refresh): a network failure keeps what is already shown.
    func refresh() async {
        guard let location, location.isAuthenticated else { return }
        do {
            let overview: CrewOverviewResponse = try await location.send(path: "/me/crew")
            availability = .available
            members = overview.crew
            if let profile: ProfileResponse = try? await location.send(path: "/me/profile") {
                nickname = profile.nickname
            }
        } catch DotWatcherAPIError.badResponse(let status, _) {
            // 401 means the session ended and 426 is handled app-wide; neither says anything about
            // whether the server has crew. Anything else (404 from a server without it) hides the
            // feature quietly.
            if status == 401 || status == 426 { return }
            availability = .unavailable
            members = []
        } catch {
            // Network trouble: keep what is already shown.
        }
    }

    /// Forgets everything: signing out or deleting the account must not leave the last person's crew
    /// behind for whoever signs in next.
    func clear() {
        availability = .unknown
        members = []
        nickname = nil
        nicknameError = nil
        myCode = nil
        myCodeError = nil
        removeError = nil
        confirm = nil
        pendingLinkCode = nil
        openedByLink = false
        isFlowPresented = false
    }

    // MARK: - Nickname

    @discardableResult
    func setNickname(_ raw: String) async -> Bool {
        guard CrewFormat.isValidNickname(raw) else {
            nicknameError = "Use up to \(CrewFormat.maxNicknameLength) characters, with no line breaks."
            return false
        }
        guard let location else { return false }
        let value: Any
        if let cleaned = CrewFormat.cleanNickname(raw) { value = cleaned } else { value = NSNull() }
        do {
            let response: ProfileResponse = try await location.send(
                path: "/me/profile", method: "PUT", body: ["nickname": value])
            nickname = response.nickname
            nicknameError = nil
            return true
        } catch {
            nicknameError = error.localizedDescription
            return false
        }
    }

    // MARK: - My code

    /// Shows the code already on offer, or makes one, so reopening the sheet doesn't burn a new code
    /// each time.
    func loadMyCode() async {
        guard let location, !isLoadingMyCode else { return }
        isLoadingMyCode = true
        myCodeError = nil
        defer { isLoadingMyCode = false }
        if let existing: [CrewCode] = try? await location.send(path: "/me/crew/codes"), let first = existing.first {
            myCode = first
            return
        }
        await makeCode(location)
    }

    /// "Make a new code": the old one stops working, so whoever has it can no longer use it.
    func newMyCode() async {
        guard let location, !isLoadingMyCode else { return }
        isLoadingMyCode = true
        myCodeError = nil
        defer { isLoadingMyCode = false }
        if let old = myCode {
            try? await location.sendAuthorizedEmpty(path: "/me/crew/codes/\(old.code)", method: "DELETE")
        }
        await makeCode(location)
    }

    private func makeCode(_ location: LocationManager) async {
        do {
            myCode = try await location.send(path: "/me/crew/codes", method: "POST")
        } catch {
            myCodeError = error.localizedDescription
        }
    }

    // MARK: - Adding someone with a code

    /// A crew link was opened. If nobody is signed in yet it is kept and handled after sign-in, as
    /// session invite links are. Never adds anyone by itself: it only leads to the confirmation.
    func onLink(_ code: String, signedIn: Bool) {
        if signedIn {
            Task { await beginAdd(code, viaLink: true) }
        } else {
            pendingLinkCode = code
        }
    }

    /// Call once the person is signed in (after sign-in or register) to handle a link opened earlier.
    func consumePendingLink() {
        guard let code = pendingLinkCode else { return }
        pendingLinkCode = nil
        Task { await beginAdd(code, viaLink: true) }
    }

    /// Looks up whose code this is and asks the person to confirm. Adds no one.
    func beginAdd(_ raw: String, viaLink: Bool = false) async {
        if viaLink && !isFlowPresented {
            openedByLink = true
            isFlowPresented = true
        }
        guard let code = CrewLink.normalize(raw) else {
            confirm = .failed("That doesn't look like a crew code. It has 8 letters and numbers.")
            return
        }
        guard let location else {
            confirm = .failed("Network error. Try again.")
            return
        }
        confirm = .loading
        // Crew is switched on per account, and a lookup for an account without it 404s just like a dead
        // code. A link can arrive before the first refresh has answered, so find out first.
        if availability == .unknown { await refresh() }
        if availability == .unavailable {
            confirm = .failed(CrewFormat.notAvailableMessage)
            return
        }
        do {
            let owner: CrewCodePreview = try await location.send(path: "/me/crew/codes/\(code)")
            confirm = .ready(code: code, owner: owner)
        } catch DotWatcherAPIError.badResponse(let status, _) {
            confirm = .failed(CrewFormat.failureMessage(status: status))
        } catch {
            confirm = .failed("Network error. Try again.")
        }
    }

    /// The person tapped Add on the confirmation.
    func confirmAdd() async {
        guard case .ready(let code, let owner) = confirm, let location else { return }
        confirm = .adding(owner: owner)
        do {
            let member: CrewMember = try await location.send(
                path: "/me/crew/codes/\(code)/redeem", method: "POST")
            members = [member] + members.filter { $0.userId != member.userId }
            confirm = .done(member)
            await refresh()
        } catch DotWatcherAPIError.badResponse(let status, _) {
            confirm = .failed(CrewFormat.failureMessage(status: status))
        } catch {
            confirm = .failed("Network error. Try again.")
        }
    }

    func dismissConfirm() {
        confirm = nil
        if openedByLink {
            openedByLink = false
            isFlowPresented = false
        }
    }

    // MARK: - Removing someone

    /// Takes effect straight away for both people; the other person isn't told.
    func remove(_ userId: String) async {
        guard let location else { return }
        do {
            try await location.sendAuthorizedEmpty(path: "/me/crew/\(userId)", method: "DELETE")
            members.removeAll { $0.userId == userId }
            removeError = nil
        } catch DotWatcherAPIError.badResponse(404, _) {
            members.removeAll { $0.userId == userId }
        } catch {
            removeError = error.localizedDescription
        }
    }
}
