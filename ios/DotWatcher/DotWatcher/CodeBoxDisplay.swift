import SwiftUI

/// Read-only "Wordle" box display matching `CodeBoxField`'s look, but sized to fill whatever
/// width it's given instead of fixed-size boxes — used to show the current session name/code
/// inline in a card, which may be narrower or wider than the box field's original context.
struct CodeBoxDisplay: View {
    var text: String
    var length: Int = 8

    private let spacing: CGFloat = 6
    private let boxHeight: CGFloat = 44

    var body: some View {
        GeometryReader { proxy in
            let boxWidth = max(0, (proxy.size.width - spacing * CGFloat(length - 1)) / CGFloat(length))
            let chars = Array(text)
            HStack(spacing: spacing) {
                ForEach(0..<length, id: \.self) { i in
                    let char = chars.count > i ? String(chars[i]) : ""
                    ZStack {
                        RoundedRectangle(cornerRadius: 8)
                            .fill(Color(.tertiarySystemBackground))
                        RoundedRectangle(cornerRadius: 8)
                            .strokeBorder(Color(.separator), lineWidth: 1)
                        Text(char)
                            .font(.title3.bold().monospaced())
                            .minimumScaleFactor(0.5)
                    }
                    .frame(width: boxWidth)
                }
            }
        }
        .frame(height: boxHeight)
    }
}

#Preview {
    VStack(spacing: 20) {
        CodeBoxDisplay(text: "TEST0807")
        CodeBoxDisplay(text: "AB")
    }
    .padding()
}
