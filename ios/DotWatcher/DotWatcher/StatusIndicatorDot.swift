import SwiftUI

struct StatusIndicatorDot: View {
    let color: Color
    var countdownFraction: Double? = nil
    var size: CGFloat = 10
    /// Continuous ambient pulse (distinct from the per-post yellow `flash()` below) — used while
    /// location is actively being shared with other users, so sharing has a persistent visible
    /// signal rather than only a momentary flash on each post.
    var pulsing: Bool = false

    @State private var isFlashing = false
    @State private var wasAboveZero = true
    @State private var pulseScale: CGFloat = 1
    @State private var pulseOpacity: Double = 0.5

    var body: some View {
        Circle()
            .fill(isFlashing ? Color.yellow : color)
            .frame(width: isFlashing ? size * 1.2 : size, height: isFlashing ? size * 1.2 : size)
            .animation(.easeOut(duration: 0.15), value: isFlashing)
            .background(
                Circle()
                    .fill(color)
                    .frame(width: size, height: size)
                    .scaleEffect(pulsing ? pulseScale : 1)
                    .opacity(pulsing ? pulseOpacity : 0)
            )
            .onChange(of: countdownFraction) { _, newValue in
                guard let newValue else { return }
                let hitZero = newValue <= 0 && wasAboveZero
                wasAboveZero = newValue > 0
                if hitZero { flash() }
            }
            .onAppear { if pulsing { startPulse() } }
            .onChange(of: pulsing) { _, newValue in if newValue { startPulse() } }
    }

    private func startPulse() {
        pulseScale = 1
        pulseOpacity = 0.5
        withAnimation(.easeOut(duration: 1.6).repeatForever(autoreverses: false)) {
            pulseScale = 2.2
            pulseOpacity = 0
        }
    }

    private func flash() {
        isFlashing = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            isFlashing = false
        }
    }
}
