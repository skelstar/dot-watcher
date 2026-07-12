import MapKit
import SwiftUI

struct NativeMapView: View {
    var positions: [RunnerPositionResponse]
    var currentRunnerName: String

    @State private var cameraPosition: MapCameraPosition = .automatic

    var body: some View {
        Map(position: $cameraPosition) {
            ForEach(positions, id: \.runnerName) { position in
                Annotation(position.runnerName, coordinate: position.coordinate) {
                    RunnerCircle(name: position.runnerName, size: 36, isHighlighted: position.runnerName == currentRunnerName)
                }
            }
        }
        .onAppear { fitCamera() }
        .onChange(of: positions.map(\.runnerName)) { _, _ in fitCamera() }
    }

    private func fitCamera() {
        guard !positions.isEmpty else { return }
        if positions.count == 1 {
            cameraPosition = .region(
                MKCoordinateRegion(
                    center: positions[0].coordinate,
                    span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)
                )
            )
            return
        }

        let coordinates = positions.map(\.coordinate)
        let minLat = coordinates.map(\.latitude).min()!
        let maxLat = coordinates.map(\.latitude).max()!
        let minLon = coordinates.map(\.longitude).min()!
        let maxLon = coordinates.map(\.longitude).max()!

        let center = CLLocationCoordinate2D(
            latitude: (minLat + maxLat) / 2,
            longitude: (minLon + maxLon) / 2
        )
        let span = MKCoordinateSpan(
            latitudeDelta: max(0.01, (maxLat - minLat) * 1.4),
            longitudeDelta: max(0.01, (maxLon - minLon) * 1.4)
        )
        cameraPosition = .region(MKCoordinateRegion(center: center, span: span))
    }
}

private extension RunnerPositionResponse {
    var coordinate: CLLocationCoordinate2D {
        CLLocationCoordinate2D(latitude: latitude, longitude: longitude)
    }
}
