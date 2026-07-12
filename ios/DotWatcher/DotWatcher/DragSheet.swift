import SwiftUI

/// A custom bottom sheet that snaps between half and full height via drag.
///
/// Native `.sheet` + `.presentationDetents` has a known UIKit hit-testing bug where its
/// invisible dismiss-overlay blocks taps on the presenting view (especially when the
/// presenting root is a `ScrollView`), even with `.presentationBackgroundInteraction(.enabled)`.
/// This container avoids that entirely: it's a plain view in our own `ZStack`, so only its own
/// frame ever intercepts touches.
///
/// `content` is always laid out at its full (large-detent) size and never resized during a
/// drag or detent change — only clipped and vertically offset to show the currently visible
/// slice. This keeps drags perfectly smooth even when `content` embeds something expensive to
/// relayout (like a `WKWebView`), since that view's own frame never changes after first layout.
struct DragSheet<Content: View>: View {
    @ViewBuilder var content: () -> Content

    @State private var detent: Detent = .medium
    @GestureState private var dragTranslation: CGFloat = 0

    private enum Detent {
        case medium, large
    }

    var body: some View {
        GeometryReader { proxy in
            let bottomInset = proxy.safeAreaInsets.bottom
            let containerHeight = proxy.size.height + bottomInset
            let mediumHeight = containerHeight * 0.5
            let largeHeight = containerHeight - 60

            let settledHeight: CGFloat = detent == .medium ? mediumHeight : largeHeight
            let cardHeight = min(largeHeight, max(mediumHeight, settledHeight - dragTranslation))

            VStack(spacing: 0) {
                Capsule()
                    .fill(Color(.systemGray3))
                    .frame(width: 36, height: 5)
                    .padding(.vertical, 8)
                // Always laid out at a constant, full (large) size — never resized during a
                // drag or detent change — so this subtree's own layout (and any WKWebView
                // inside it) never triggers relayout jank. The card around it clips down to
                // `cardHeight` below, so only the top slice is ever visible.
                content()
                    .frame(height: max(0, largeHeight - 21 - bottomInset), alignment: .top)
                Spacer(minLength: 0)
            }
            .frame(maxWidth: .infinity)
            .frame(height: cardHeight, alignment: .top)
            .clipped()
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
            .shadow(color: .black.opacity(0.15), radius: 12, y: -2)
            .contentShape(Rectangle())
            .gesture(
                DragGesture(minimumDistance: 1, coordinateSpace: .global)
                    .updating($dragTranslation) { value, state, _ in
                        state = value.translation.height
                    }
                    .onEnded { value in
                        let projected = settledHeight - value.translation.height - value.velocity.height * 0.15
                        let candidates: [(Detent, CGFloat)] = [(.medium, mediumHeight), (.large, largeHeight)]
                        detent = candidates.min { abs($0.1 - projected) < abs($1.1 - projected) }!.0
                    }
            )
            .animation(.spring(response: 0.35, dampingFraction: 0.85), value: detent)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            .ignoresSafeArea(edges: .bottom)
        }
    }
}
