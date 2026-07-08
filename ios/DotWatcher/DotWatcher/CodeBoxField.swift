import SwiftUI

struct CodeBoxField: View {
    @Binding var text: String
    var length: Int = 6
    var lettersOnly: Bool = false
    var autoFocus: Bool = false
    @FocusState private var isFocused: Bool

    var body: some View {
        HStack(spacing: 6) {
            ForEach(0..<length, id: \.self) { i in
                let chars = Array(text)
                let char = chars.count > i ? String(chars[i]) : ""
                let isActive = isFocused && chars.count == i
                ZStack {
                    RoundedRectangle(cornerRadius: 8)
                        .fill(Color(.tertiarySystemBackground))
                    RoundedRectangle(cornerRadius: 8)
                        .strokeBorder(
                            isActive ? Color.primary : Color(.separator),
                            lineWidth: isActive ? 3 : 1
                        )
                    Text(char)
                        .font(.title2.bold().monospaced())
                }
                .frame(width: 36, height: 44)
            }
        }
        .overlay(
            TextField("", text: $text)
                .focused($isFocused)
                .opacity(0.01)
                .keyboardType(.asciiCapable)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .onChange(of: text) { _, new in
                    let filtered = Self.sanitize(new, length: length, lettersOnly: lettersOnly)
                    if filtered != new { text = filtered }
                }
        )
        .contentShape(Rectangle())
        .onTapGesture { isFocused = true }
        .onAppear { if autoFocus { isFocused = true } }
    }

    // Typing produces valid input incrementally, so a plain filter+truncate is enough there.
    // Pasting (e.g. a whole WhatsApp share message with the code embedded in a sentence) can
    // carry surrounding words, so look for a whole word of exactly `length` matching characters
    // rather than just taking the first matching characters in sequence. Invite codes are hex
    // (0-9A-F), which ordinary English words practically never are, so prefer a hex-only word
    // when one exists to avoid matching a coincidentally-6-letter word like "INVITE".
    private static func sanitize(_ raw: String, length: Int, lettersOnly: Bool) -> String {
        let isMatch: (Character) -> Bool = lettersOnly
            ? { ($0.isLetter && $0.isASCII) || $0.isNumber }
            : { $0.isLetter || $0.isNumber }

        let uppercased = raw.uppercased()
        if uppercased.count <= length {
            return String(uppercased.filter(isMatch).prefix(length))
        }

        let words = uppercased.split(whereSeparator: { !isMatch($0) }).filter { $0.count == length }
        let isHexDigit: (Character) -> Bool = { $0.isNumber || ("A"..."F").contains($0) }
        if let hexWord = words.first(where: { $0.allSatisfy(isHexDigit) }) {
            return String(hexWord)
        }
        if let word = words.first {
            return String(word)
        }
        return String(uppercased.filter(isMatch).prefix(length))
    }
}
