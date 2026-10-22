import Foundation

/// Crew codes and the links that carry them. A crew code is 8 characters, one-time, and names a
/// person; it is shared as a message with a `https://<web host>/crew/{CODE}` link (also what the QR
/// code encodes). A tap or a camera-app scan opens the app through a universal link once `/crew/*`
/// is in the web host's apple-app-site-association file (held until this build is in the App Store);
/// a recipient without the app lands on the web fallback page. The server's rules live in
/// `SessionStore.NormalizeCrewCode`; `Android`'s twin of this is `crew/CrewLink.kt`.
enum CrewLink {
    static let codeLength = 8

    // No 0/O/1/I/L, matching the server's alphabet exactly.
    private static let alphabet = Set("ABCDEFGHJKMNPQRSTUVWXYZ23456789")

    /// The code in canonical form (upper case, no spaces or dashes), or nil if `raw` isn't a
    /// well-formed crew code. Same rules as the server, so a code that passes is worth asking it about.
    static func normalize(_ raw: String?) -> String? {
        guard let raw else { return nil }
        let code = raw
            .replacingOccurrences(of: "-", with: "")
            .replacingOccurrences(of: " ", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .uppercased()
        guard code.count == codeLength, code.allSatisfy({ alphabet.contains($0) }) else { return nil }
        return code
    }

    /// The code in a `/crew/{CODE}` link, or nil for any other URL or a malformed code. The host isn't
    /// checked: universal links already limit which hosts reach the app, and a pasted link from another
    /// host carries nothing but a code.
    static func code(from url: URL) -> String? {
        let parts = url.pathComponents.filter { $0 != "/" }
        guard parts.count == 2, parts[0].lowercased() == "crew" else { return nil }
        return normalize(parts[1])
    }

    /// Finds a crew code in pasted text: the link out of a whole shared message, a "Crew code: X"
    /// line, or the text itself if that is just a code. Looks for the structure rather than any
    /// 8-letter word, because a shared message contains plenty of those ("skelstar").
    static func extractFromPaste(_ text: String) -> String? {
        if let found = firstGroup(in: text, pattern: "/crew/([A-Za-z0-9]{\(codeLength)})(?![A-Za-z0-9])"),
           let code = normalize(found) {
            return code
        }
        if let found = firstGroup(in: text, pattern: "crew code:\\s*([A-Za-z0-9-]{\(codeLength),\(codeLength + 1)})", caseInsensitive: true),
           let code = normalize(found) {
            return code
        }
        return normalize(text)
    }

    static func url(base: URL, code: String) -> URL {
        base.appendingPathComponent("crew/\(code)")
    }

    /// The message sent through the share sheet. Mirrors the session invite message: a tappable link
    /// first, then the plain code for anyone whose phone doesn't open the link.
    static func shareMessage(base: URL, code: String) -> String {
        """
        Add me to my DotWatcher crew!

        Tap to add me in the app:
        \(url(base: base, code: code).absoluteString)

        Crew code: \(code)

        This code works once and expires in 24 hours.
        """
    }

    private static func firstGroup(in text: String, pattern: String, caseInsensitive: Bool = false) -> String? {
        let options: NSRegularExpression.Options = caseInsensitive ? [.caseInsensitive] : []
        guard let regex = try? NSRegularExpression(pattern: pattern, options: options) else { return nil }
        let range = NSRange(text.startIndex..., in: text)
        guard let match = regex.firstMatch(in: text, options: [], range: range),
              match.numberOfRanges > 1,
              let groupRange = Range(match.range(at: 1), in: text)
        else { return nil }
        return String(text[groupRange])
    }
}
