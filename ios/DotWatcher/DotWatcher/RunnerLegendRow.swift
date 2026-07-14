import SwiftUI

/// A row of small runner avatars pinned to the top-left of the map, shown once the drag sheet
/// is far enough open that the map fills most of the screen — mirrors the web client's
/// `Legend.tsx`, which keeps every participant's badge visible near the top of the map
/// regardless of where their pin currently is.
struct RunnerLegendRow: View {
    let names: [String]
    let currentRunnerName: String
    @Binding var followedRunnerName: String?
    var onFitAll: () -> Void

    private static let badgeSize: CGFloat = 32

    var body: some View {
        HStack(spacing: 8) {
            ForEach(names, id: \.self) { name in
                RunnerCircle(
                    name: name,
                    size: Self.badgeSize,
                    fontSize: Self.badgeSize * 0.34,
                    fillColor: name == currentRunnerName ? .black : RunnerColorPalette.color(for: name)
                )
                .overlay(
                    Circle()
                        .stroke(Color.accentColor, lineWidth: 2.5)
                        .opacity(followedRunnerName == name ? 1 : 0)
                        .padding(-3)
                )
                .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
                .onTapGesture {
                    followedRunnerName = followedRunnerName == name ? nil : name
                }
            }
            FitAllButton(size: Self.badgeSize, action: onFitAll)
                .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
        }
        .padding(8)
        .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 20))
    }
}
