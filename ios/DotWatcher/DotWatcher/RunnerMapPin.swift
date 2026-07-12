import SwiftUI

/// A map marker for a runner: the existing `RunnerCircle` avatar, upright and never rotating,
/// with a small chevron that orbits the circle's edge to show compass heading — mirrors the
/// web client's `Arrow.tsx` marker (upright dot + orbiting chevron), scaled to `size`.
struct RunnerMapPin: View {
    let name: String
    var size: CGFloat = 36
    var isHighlighted: Bool = false
    var heading: Double?

    /// The circle itself renders smaller than `size` while the label keeps its normal
    /// proportional font size (computed from the full `size`), so shrinking the dot doesn't
    /// shrink the initials.
    private var circleSize: CGFloat { size * 0.75 }

    /// Matches `RunnerCircle`'s own fill color so the chevron reads as part of the same dot
    /// instead of a separate white shape that's hard to see against light map backgrounds.
    private var dotColor: Color { isHighlighted ? .accentColor : Color(.systemGray4) }

    var body: some View {
        ZStack {
            RunnerCircle(name: name, size: circleSize, isHighlighted: isHighlighted, fontSize: size * 0.3)
            if let heading {
                // The chevron sits near the top of a full-pin-size transparent frame, then the
                // whole frame rotates around its own center — which is the pin's center — so
                // the chevron orbits the dot's edge exactly like the web's
                // `rotate(heading, CX, CY)` transform.
                ChevronShape()
                    .stroke(dotColor, style: StrokeStyle(lineWidth: size * 0.09, lineCap: .round, lineJoin: .round))
                    .frame(width: size * 0.27, height: size * 0.14)
                    .shadow(color: .black.opacity(0.4), radius: 1, y: 1)
                    .padding(.top, -size * 0.12)
                    .frame(width: size, height: size, alignment: .top)
                    .rotationEffect(.degrees(heading))
            }
        }
    }
}

private struct ChevronShape: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: rect.minX, y: rect.maxY))
        path.addLine(to: CGPoint(x: rect.midX, y: rect.minY))
        path.addLine(to: CGPoint(x: rect.maxX, y: rect.maxY))
        return path
    }
}
