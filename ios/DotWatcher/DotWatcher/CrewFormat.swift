import Foundation

/// Wording and small derivations for the crew UI, kept free of SwiftUI so they are easy to reason
/// about (and to unit-test once the project has a test target). Mirrors `crew/CrewFormat.kt` on Android.
enum CrewFormat {
    /// How many faces the start-screen row shows before collapsing the rest into "+N".
    static let rowFaces = 4
    static let maxNicknameLength = 30

    /// What crew members see for a person: the nickname they chose, else their account initials.
    static func label(nickname: String?, displayName: String) -> String {
        let trimmed = nickname?.trimmingCharacters(in: .whitespaces) ?? ""
        return trimmed.isEmpty ? displayName : trimmed
    }

    struct RowSummary {
        let faces: [CrewMember]
        let extra: Int
        let title: String
        let meta: String
    }

    /// What the slim crew row on the start screen shows. `faces` keep the order given (the server lists
    /// the most recently added first).
    static func rowSummary(_ members: [CrewMember]) -> RowSummary {
        guard !members.isEmpty else {
            return RowSummary(faces: [], extra: 0, title: "Your crew", meta: "Add people that you know")
        }
        return RowSummary(
            faces: Array(members.prefix(rowFaces)),
            extra: max(0, members.count - rowFaces),
            title: "Your crew",
            meta: members.count == 1 ? "1 person" : "\(members.count) people"
        )
    }

    /// The server writes ISO 8601 with up to 7 fractional digits and an offset ("…10:00:00.1234567+00:00").
    /// `ISO8601DateFormatter` is unreliable with long fractions, so drop them first; the second is
    /// precision nobody needs here.
    static func parseServerDate(_ value: String) -> Date? {
        let withoutFraction = value.replacingOccurrences(of: "\\.\\d+", with: "", options: .regularExpression)
        return ISO8601DateFormatter().date(from: withoutFraction)
    }

    /// "since March" for this year, "since March 2025" otherwise.
    static func sinceLabel(_ since: String, now: Date = Date(), calendar: Calendar = .current) -> String {
        guard let date = parseServerDate(since) else { return "in your crew" }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US")
        formatter.calendar = calendar
        formatter.dateFormat = "LLLL"
        let month = formatter.string(from: date)
        let sameYear = calendar.component(.year, from: date) == calendar.component(.year, from: now)
        return sameYear ? "since \(month)" : "since \(month) \(calendar.component(.year, from: date))"
    }

    /// "Expires in 23 h" / "Expires in 40 min" / "Expired", for the code the person is about to send.
    static func expiryLabel(_ expiresAt: String, now: Date = Date()) -> String {
        guard let expiry = parseServerDate(expiresAt) else { return "" }
        let seconds = expiry.timeIntervalSince(now)
        if seconds <= 0 { return "Expired" }
        let hours = Int(seconds / 3600)
        if hours >= 1 { return "Expires in \(hours) h" }
        return "Expires in \(max(1, Int(seconds / 60))) min"
    }

    /// The server's answers to a preview or redeem, in words. 404 is deliberately vague: an unknown,
    /// expired, used or blocked code all look the same.
    static func failureMessage(status: Int) -> String {
        switch status {
        case 400: return "That's your own crew code."
        case 404: return "That code has already been used or has expired. Ask them to send you a new one."
        case 409: return "Your crew, or theirs, is full."
        case 429: return "Too many attempts. Try again later."
        default: return "Something went wrong (error \(status)). Try again."
        }
    }

    /// Trimmed; blank means "clear it" (nil).
    static func cleanNickname(_ raw: String) -> String? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }

    /// Matches the server: 1-30 characters, no control characters. Blank is valid (it clears the name).
    static func isValidNickname(_ raw: String) -> Bool {
        guard let cleaned = cleanNickname(raw) else { return true }
        return cleaned.count <= maxNicknameLength
            && !cleaned.unicodeScalars.contains { CharacterSet.controlCharacters.contains($0) }
    }
}

extension CrewMember {
    /// The name shown for this person.
    var label: String { CrewFormat.label(nickname: nickname, displayName: displayName) }
}

extension CrewCodePreview {
    var label: String { CrewFormat.label(nickname: nickname, displayName: displayName) }
}
