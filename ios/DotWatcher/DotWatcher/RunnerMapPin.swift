import SwiftUI

/// A map marker for a runner: the existing `RunnerCircle` avatar, upright and never rotating,
/// with a small chevron that orbits the circle's edge to show compass heading — mirrors the
/// web client's `Arrow.tsx` marker (upright dot + orbiting chevron), scaled to `size`.
///
/// When `isStale` (no position update for `NativeMapView.staleAfter`), renders the web
/// client's "sleep" style instead: white fill, colored outline/initials, gently pulsing, no
/// chevron — signals the runner may have stopped moving or lost signal.
struct RunnerMapPin: View {
    let name: String
    var size: CGFloat = 36
    var isHighlighted: Bool = false
    var heading: Double?
    var isStale: Bool = false

    /// The circle itself renders smaller than `size` while the label keeps its normal
    /// proportional font size (computed from the full `size`), so shrinking the dot doesn't
    /// shrink the initials.
    private var circleSize: CGFloat { size * 0.75 }

    /// The local user's own pin is always black so it's unambiguous at a glance; every other
    /// runner gets a stable color hashed from their name (`RunnerColorPalette`), matching the
    /// web client's per-runner coloring. Also used for the chevron so it reads as part of the
    /// same dot instead of a separate white shape that's hard to see against light map
    /// backgrounds.
    private var dotColor: Color { isHighlighted ? .black : RunnerColorPalette.color(for: name) }

    var body: some View {
        if isStale {
            SleepPin(name: name, size: circleSize, color: dotColor)
        } else {
            ZStack {
                RunnerCircle(name: name, size: circleSize, fontSize: size * 0.3, fillColor: dotColor)
                if let heading {
                    // The chevron sits near the top of a full-pin-size transparent frame, then
                    // the whole frame rotates around its own center — which is the pin's
                    // center — so the chevron orbits the dot's edge exactly like the web's
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
}

private struct SleepPin: View {
    let name: String
    let size: CGFloat
    let color: Color

    @State private var isPulsing = false

    var body: some View {
        Circle()
            .fill(Color.white)
            .overlay(Circle().stroke(color, lineWidth: 2))
            .frame(width: size, height: size)
            .shadow(color: .black.opacity(0.45), radius: 2, y: 1)
            .overlay {
                Text(name.trimmingCharacters(in: .whitespaces).isEmpty ? "?" : name)
                    .font(.system(size: size * 0.3, weight: .bold))
                    .foregroundStyle(color)
            }
            .scaleEffect(isPulsing ? 1.05 : 0.85)
            .opacity(isPulsing ? 0.7 : 0.35)
            .onAppear {
                withAnimation(.easeInOut(duration: 1.4).repeatForever(autoreverses: true)) {
                    isPulsing = true
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
