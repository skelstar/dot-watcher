import SwiftUI

struct PostCountdownRing: View {
    let fraction: Double
    var size: CGFloat = 16

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color(.systemGray4), lineWidth: 1)
            PieSlice(fraction: fraction)
                .fill(Color(.systemGray2))
        }
        .frame(width: size, height: size)
    }
}

private struct PieSlice: Shape {
    var fraction: Double

    var animatableData: Double {
        get { fraction }
        set { fraction = newValue }
    }

    func path(in rect: CGRect) -> Path {
        var path = Path()
        guard fraction > 0 else { return path }

        let center = CGPoint(x: rect.midX, y: rect.midY)
        let radius = min(rect.width, rect.height) / 2
        let startAngle = Angle.degrees(-90)
        let endAngle = Angle.degrees(-90 + 360 * fraction)

        path.move(to: center)
        path.addArc(center: center, radius: radius, startAngle: startAngle, endAngle: endAngle, clockwise: false)
        path.closeSubpath()
        return path
    }
}
