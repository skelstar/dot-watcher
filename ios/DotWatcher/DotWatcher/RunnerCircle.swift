import SwiftUI

struct RunnerCircle: View {
    let name: String
    var size: CGFloat = 40
    var isHighlighted: Bool = false

    private static let highlightColor = Color.accentColor

    var body: some View {
        ZStack {
            Circle()
                .fill(isHighlighted ? Self.highlightColor : Color(.systemGray4))
                .frame(width: size, height: size)
            Text(name.trimmingCharacters(in: .whitespaces).isEmpty ? "?" : name)
                .font(.system(size: size * 0.3, weight: .bold))
                .foregroundStyle(isHighlighted ? .white : .primary)
        }
    }
}

#Preview("Participants Dots") {
    let names = ["SKE", "JOH", "CHQ", "ALI", "ROS", "TOM", "BEA", "WIL", "ZOE", "MAX"]
    VStack(alignment: .leading, spacing: 10) {
        Text("PARTICIPANTS")
            .font(.caption)
            .fontWeight(.semibold)
            .foregroundStyle(.secondary)
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 56))], spacing: 10) {
            ForEach(names, id: \.self) { name in
                RunnerCircle(name: name, size: 56, isHighlighted: name == "SKE")
            }
        }
    }
    .frame(maxWidth: .infinity, alignment: .topLeading)
    .padding(16)
    .background(Color(.secondarySystemBackground))
    .clipShape(RoundedRectangle(cornerRadius: 14))
    .padding()
}
