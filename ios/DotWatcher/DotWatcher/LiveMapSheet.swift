import SwiftUI

struct LiveMapSheet: View {
    var location: LocationManager
    @Binding var followedRunnerName: String?

    var body: some View {
        if location.activeMembership != nil {
            NativeMapView(
                positions: location.runnerPositions,
                currentRunnerName: location.runnerName,
                currentCoordinate: location.currentCoordinate,
                currentHeading: location.currentHeading,
                followedRunnerName: $followedRunnerName
            )
        } else {
            Color.clear
        }
    }
}
