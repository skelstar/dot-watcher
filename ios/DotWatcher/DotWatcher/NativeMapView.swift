import MapKit
import SwiftUI

struct NativeMapView: View {
    var positions: [RunnerPositionResponse]
    var currentRunnerName: String
    /// The phone's own live coordinate (from CoreLocation), rendered in place of whatever
    /// `positions` has for `currentRunnerName` — always current, not delayed by the last
    /// `POST /location` round-trip.
    var currentCoordinate: CLLocationCoordinate2D?
    var currentHeading: Double?

    @State private var cameraPosition: MapCameraPosition = .automatic

    private struct Pin: Identifiable {
        let id: String
        let coordinate: CLLocationCoordinate2D
        let heading: Double?
        let isCurrentRunner: Bool
    }

    private var pins: [Pin] {
        var result = positions
            .filter { $0.runnerName != currentRunnerName }
            .map { Pin(id: $0.runnerName, coordinate: $0.coordinate, heading: $0.heading, isCurrentRunner: false) }
        if let currentCoordinate {
            result.append(Pin(id: currentRunnerName, coordinate: currentCoordinate, heading: currentHeading, isCurrentRunner: true))
        }
        return result
    }

    var body: some View {
        Map(position: $cameraPosition) {
            ForEach(pins) { pin in
                Annotation("", coordinate: pin.coordinate) {
                    RunnerMapPin(name: pin.id, isHighlighted: pin.isCurrentRunner, heading: pin.heading)
                }
            }
        }
        .onAppear { fitCamera() }
        .onChange(of: pins.map(\.id)) { _, _ in fitCamera() }
    }

    private func fitCamera() {
        guard !pins.isEmpty else { return }
        if pins.count == 1 {
            cameraPosition = .region(
                MKCoordinateRegion(
                    center: pins[0].coordinate,
                    span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)
                )
            )
            return
        }

        let coordinates = pins.map(\.coordinate)
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
