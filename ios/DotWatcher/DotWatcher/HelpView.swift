import SwiftUI

struct HelpView: View {
    @Environment(\.dismiss) private var dismiss

    private var rawMarkdown: String {
        guard let url = Bundle.main.url(forResource: "Help", withExtension: "md"),
              let content = try? String(contentsOf: url, encoding: .utf8)
        else { return "Help unavailable." }
        return content
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 4) {
                ForEach(Array(rawMarkdown.components(separatedBy: "\n").enumerated()), id: \.offset) { _, line in
                    if line.hasPrefix("# ") {
                        Text(String(line.dropFirst(2)))
                            .font(.title2.bold())
                            .padding(.top, 8)
                            .padding(.bottom, 4)
                    } else if line.hasPrefix("## ") {
                        Text(String(line.dropFirst(3)))
                            .font(.headline)
                            .underline()
                            .padding(.top, 16)
                            .padding(.bottom, 2)
                    } else if line.isEmpty {
                        Color.clear.frame(height: 4)
                    } else {
                        Text((try? AttributedString(markdown: line)) ?? AttributedString(line))
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(24)
        }
        .safeAreaInset(edge: .bottom) {
            Button("Done") { dismiss() }
                .buttonStyle(.borderedProminent)
                .padding()
                .frame(maxWidth: .infinity)
                .background(.regularMaterial)
        }
    }
}

#Preview("Help") {
    HelpView()
}
