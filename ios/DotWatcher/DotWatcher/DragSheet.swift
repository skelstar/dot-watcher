import SwiftUI

/// A custom bottom sheet that snaps between peek/medium/large heights via drag.
///
/// Native `.sheet` + `.presentationDetents` has a known UIKit hit-testing bug where its
/// invisible dismiss-overlay blocks taps on the presenting view (especially when the
/// presenting root is a `ScrollView`), even with `.presentationBackgroundInteraction(.enabled)`.
/// This container avoids that entirely: it's a plain view in our own `ZStack`, so only its own
/// frame ever intercepts touches.
struct DragSheet<Content: View>: View {
    /// Height of `content` itself while peeking (grabber + safe-area inset are added on top).
    var peekHeight: CGFloat
    @ViewBuilder var content: Content

    private let grabberHeight: CGFloat = 21

    @State private var detent: Detent = .peek
    @GestureState private var dragOffset: CGFloat = 0

    private enum Detent {
        case peek, medium, large
    }

    var body: some View {
        GeometryReader { proxy in
            let bottomInset = proxy.safeAreaInsets.bottom
            let containerHeight = proxy.size.height + bottomInset
            let peekCardHeight = peekHeight + grabberHeight + bottomInset
            let mediumHeight = containerHeight * 0.5
            let largeHeight = containerHeight - 60

            let baseHeight: CGFloat = {
                switch detent {
                case .peek: return peekCardHeight
                case .medium: return mediumHeight
                case .large: return largeHeight
                }
            }()
            let cardHeight = min(largeHeight, max(peekCardHeight, baseHeight - dragOffset))

            VStack(spacing: 0) {
                Capsule()
                    .fill(Color(.systemGray3))
                    .frame(width: 36, height: 5)
                    .padding(.vertical, 8)
                content
                    .frame(height: max(0, cardHeight - grabberHeight - bottomInset))
                    .clipped()
                Spacer(minLength: bottomInset)
            }
            .frame(maxWidth: .infinity)
            .frame(height: cardHeight, alignment: .top)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
            .shadow(color: .black.opacity(0.15), radius: 12, y: -2)
            .gesture(
                DragGesture(minimumDistance: 5)
                    .updating($dragOffset) { value, state, _ in
                        state = value.translation.height
                    }
                    .onEnded { value in
                        let projected = baseHeight - value.translation.height - value.velocity.height * 0.15
                        let candidates: [(Detent, CGFloat)] = [
                            (.peek, peekCardHeight), (.medium, mediumHeight), (.large, largeHeight),
                        ]
                        detent = candidates.min { abs($0.1 - projected) < abs($1.1 - projected) }!.0
                    }
            )
            .animation(.interactiveSpring(), value: dragOffset)
            .animation(.spring(response: 0.35, dampingFraction: 0.85), value: detent)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            .ignoresSafeArea(edges: .bottom)
        }
    }
}
