import Foundation

/// Universal links for session invites. `/join/{CODE}` opens the app (claimed through the
/// `applinks:` entries in DotWatcher.entitlements and the web host's apple-app-site-association);
/// `/code/{CODE}` is the web viewer and is deliberately not claimed by the app.
enum InviteLink {
    static let codeLength = 6

    /// The invite code in a `/join/{CODE}` link, uppercased, or nil for any other URL or a malformed code.
    static func code(from url: URL) -> String? {
        let parts = url.pathComponents.filter { $0 != "/" }
        guard parts.count == 2, parts[0].lowercased() == "join" else { return nil }
        let code = parts[1].uppercased()
        guard code.count == codeLength, code.allSatisfy({ $0.isASCII && ($0.isLetter || $0.isNumber) }) else { return nil }
        return code
    }

    /// The link that opens the app on a phone that has it, or the web fallback page otherwise.
    static func joinURL(base: URL, code: String) -> URL {
        base.appendingPathComponent("join/\(code)")
    }

    /// The web viewer link, for people who only want to watch.
    static func watchURL(base: URL, code: String) -> URL {
        base.appendingPathComponent("code/\(code)")
    }

    static func shareMessage(base: URL, code: String) -> String {
        """
        Join my DotWatcher session!

        Tap to join in the app:
        \(joinURL(base: base, code: code).absoluteString)

        Invite code: \(code)

        Just want to watch?
        \(watchURL(base: base, code: code).absoluteString)
        """
    }
}
