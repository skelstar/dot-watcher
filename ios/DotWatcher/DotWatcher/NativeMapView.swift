import MapKit
import SwiftUI

struct NativeMapView: View {
    /// A runner with no position update for longer than this is considered stale/stationary
    /// and rendered with the dimmed "sleep" pin style, matching the web client's 30s threshold.
    static let staleAfter: TimeInterval = 30

    var positions: [RunnerPositionResponse]
    var currentRunnerName: String
    /// The phone's own live coordinate (from CoreLocation), rendered in place of whatever
    /// `positions` has for `currentRunnerName` — always current, not delayed by the last
    /// `POST /location` round-trip.
    var currentCoordinate: CLLocationCoordinate2D?
    var currentHeading: Double?
    /// The runner the map should stay centered on, set by tapping their avatar in the
    /// participants grid. Cleared (by this view) once that runner no longer has a live pin,
    /// so the selection UI upstream never points at a runner who's stopped tracking.
    @Binding var followedRunnerName: String?
    /// Bumped by `FitAllButton` to force a fit-all-runners recenter on demand, independent of
    /// whether a follow is currently active — a plain equality check on `followedRunnerName`
    /// wouldn't fire if it's already `nil` (e.g. after manually panning the map).
    var fitAllTrigger: Int

    @State private var cameraPosition: MapCameraPosition = .automatic
    @State private var followSpan = MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)

    private struct Pin: Identifiable {
        let id: String
        let coordinate: CLLocationCoordinate2D
        let heading: Double?
        let isCurrentRunner: Bool
        /// `nil` for the local user's own live pin, which is never considered stale.
        let timestamp: Date?
    }

    private var pins: [Pin] {
        var result = positions
            .filter { $0.runnerName != currentRunnerName }
            .map {
                Pin(id: $0.runnerName, coordinate: $0.coordinate, heading: $0.heading, isCurrentRunner: false, timestamp: $0.parsedTimestamp)
            }
        if let currentCoordinate {
            result.append(Pin(id: currentRunnerName, coordinate: currentCoordinate, heading: currentHeading, isCurrentRunner: true, timestamp: nil))
        }
        return result
    }

    private var followedPin: Pin? {
        guard let followedRunnerName else { return nil }
        return pins.first { $0.id == followedRunnerName }
    }

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            Map(position: $cameraPosition) {
                ForEach(pins) { pin in
                    Annotation("", coordinate: pin.coordinate) {
                        let isStale = pin.timestamp.map { context.date.timeIntervalSince($0) > Self.staleAfter } ?? false
                        RunnerMapPin(name: pin.id, isHighlighted: pin.isCurrentRunner, heading: pin.heading, isStale: isStale)
                    }
                }
            }
            .onChange(of: context.date) { _, _ in followCameraIfNeeded() }
        }
        .onAppear { fitCamera() }
        .onChange(of: pins.map(\.id)) { _, _ in
            if followedRunnerName != nil && followedPin == nil {
                // Followed runner dropped out of the pin list (e.g. left the session) — hand
                // back to fit-all via the followedRunnerName change handler below.
                followedRunnerName = nil
            } else if followedRunnerName == nil {
                fitCamera()
            }
        }
        .onChange(of: followedRunnerName) { _, newValue in
            if newValue != nil {
                followSpan = MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)
                followCameraIfNeeded()
            } else {
                fitCamera()
            }
        }
        .onChange(of: fitAllTrigger) { _, _ in
            followedRunnerName = nil
            fitCamera()
        }
    }

    /// Re-centers on the followed runner's current pin, keeping whatever zoom `followSpan`
    /// holds — called every second from the map's own timer so movement is picked up as soon
    /// as a new position lands, without a separate polling loop.
    private func followCameraIfNeeded() {
        guard let followedPin else { return }
        cameraPosition = .region(MKCoordinateRegion(center: followedPin.coordinate, span: followSpan))
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
