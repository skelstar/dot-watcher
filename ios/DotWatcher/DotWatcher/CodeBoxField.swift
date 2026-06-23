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
                            isActive ? Color.accentColor : Color(.separator),
                            lineWidth: isActive ? 2 : 1
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
                    let filtered: String
                    if lettersOnly {
                        filtered = String(new.uppercased().filter { $0.isLetter && $0.isASCII }.prefix(length))
                    } else {
                        filtered = String(new.uppercased().filter { $0.isLetter || $0.isNumber }.prefix(length))
                    }
                    if filtered != new { text = filtered }
                }
        )
        .contentShape(Rectangle())
        .onTapGesture { isFocused = true }
        .onAppear { if autoFocus { isFocused = true } }
    }
}
