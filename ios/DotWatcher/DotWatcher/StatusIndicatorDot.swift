import SwiftUI

struct StatusIndicatorDot: View {
    let color: Color
    var countdownFraction: Double? = nil
    var size: CGFloat = 10

    @State private var isFlashing = false
    @State private var wasAboveZero = true

    var body: some View {
        Circle()
            .fill(isFlashing ? Color.yellow : color)
            .frame(width: isFlashing ? size * 1.2 : size, height: isFlashing ? size * 1.2 : size)
            .animation(.easeOut(duration: 0.15), value: isFlashing)
            .onChange(of: countdownFraction) { _, newValue in
                guard let newValue else { return }
                let hitZero = newValue <= 0 && wasAboveZero
                wasAboveZero = newValue > 0
                if hitZero { flash() }
            }
    }

    private func flash() {
        isFlashing = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
            isFlashing = false
        }
    }
}
