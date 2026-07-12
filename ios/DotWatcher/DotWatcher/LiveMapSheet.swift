import SwiftUI

struct LiveMapSheet: View {
    static let peekHeight: CGFloat = 80

    var location: LocationManager

    var body: some View {
        if location.activeMembership != nil {
            NativeMapView(positions: location.runnerPositions, currentRunnerName: location.runnerName)
        } else {
            Color.clear
        }
    }
}
