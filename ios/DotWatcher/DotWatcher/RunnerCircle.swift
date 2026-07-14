import SwiftUI

struct RunnerCircle: View {
    let name: String
    var size: CGFloat = 40
    var isHighlighted: Bool = false
    /// Overrides the label's font size, decoupling it from `size` (the circle's diameter).
    /// Defaults to the usual proportional sizing.
    var fontSize: CGFloat?
    /// Overrides the circle's fill color (and switches the label to white for contrast).
    /// Defaults to the usual accent/gray behavior driven by `isHighlighted`.
    var fillColor: Color?
    /// When true, renders a dashed outline with a transparent fill instead of `fillColor` —
    /// signals this runner hasn't sent a position in a while (see `NativeMapView.disconnectedAfter`).
    /// The initials stay visible, colored with `fillColor`, so who-is-who is still readable.
    var isDisconnected: Bool = false

    private static let highlightColor = Color.accentColor

    var body: some View {
        let color = fillColor ?? (isHighlighted ? Self.highlightColor : Color(.systemGray4))
        ZStack {
            if isDisconnected {
                Circle()
                    .fill(Color.clear)
                    .overlay(Circle().stroke(color, style: StrokeStyle(lineWidth: 2, dash: [4, 4])))
                    .frame(width: size, height: size)
            } else {
                Circle()
                    .fill(color)
                    .frame(width: size, height: size)
            }
            Text(name.trimmingCharacters(in: .whitespaces).isEmpty ? "?" : name)
                .font(.system(size: fontSize ?? size * 0.3, weight: .bold))
                .foregroundStyle(isDisconnected ? color : (fillColor != nil || isHighlighted ? .white : .primary))
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
