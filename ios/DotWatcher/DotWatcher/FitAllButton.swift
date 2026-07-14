import SwiftUI

/// Sits at the end of the participants grid, sized and shaped like a `RunnerCircle` avatar so
/// it reads as part of the same row. Tapping it snaps the live map back to fit every runner in
/// frame, clearing any single runner currently being followed.
struct FitAllButton: View {
    var size: CGFloat = 38
    var action: () -> Void

    var body: some View {
        Button(action: action) {
            Circle()
                .fill(Color(.systemGray4))
                .overlay(Circle().stroke(Color.primary, lineWidth: 1.5))
                .frame(width: size, height: size)
                .overlay {
                    Image(systemName: "arrow.up.left.and.arrow.down.right")
                        .font(.system(size: size * 0.32, weight: .bold))
                        .foregroundStyle(Color.primary)
                }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Fit all runners")
    }
}

#Preview {
    HStack {
        FitAllButton {}
    }
    .padding()
}
