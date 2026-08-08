import MapKit
import SwiftUI

struct NativeMapView: View {
    /// A runner with no position update for longer than this is considered stale/stationary
    /// and rendered with the dimmed "sleep" pin style, matching the web client's 30s threshold.
    static let staleAfter: TimeInterval = 30
    /// A runner with no position update for longer than this is considered disconnected —
    /// rendered with a dashed outline and transparent fill instead of the pulsing sleep style.
    static let disconnectedAfter: TimeInterval = 60

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
    /// Fraction of this view's own height currently visible above the enclosing `DragSheet`'s
    /// clip (1.0 = fully expanded, ~0.5 at the medium detent). The map is always laid out at
    /// its full large-detent size regardless of detent, so without this, centering math for a
    /// half-open sheet would place pins under the clipped-off bottom half. Used to bias the
    /// fitted/followed region so pins land within the visible top slice instead.
    var visibleFraction: CGFloat
    /// True while the device's only path is a carrier satellite (Direct-to-Cell) connection —
    /// see `LocationManager.isUltraConstrained`. The map freezes on its last-known frame (camera
    /// stops re-fitting/following, pins stop moving) and dims under a status badge, since
    /// `GET /locations` polling is skipped entirely while this is true and pins would otherwise
    /// silently go stale with no explanation. Auto-clears (unfreezes, undims) the moment this
    /// flips back to `false` — no user action either direction.
    var isUltraConstrained: Bool = false
    /// The local device's own last successful `POST /location` time, shown in the satellite
    /// status badge so the runner has a concrete "still working" signal beyond the message text.
    var lastSent: Date?

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
                        let age = pin.timestamp.map { context.date.timeIntervalSince($0) }
                        let isStale = age.map { $0 > Self.staleAfter } ?? false
                        let isDisconnected = age.map { $0 > Self.disconnectedAfter } ?? false
                        RunnerMapPin(name: pin.id, isHighlighted: pin.isCurrentRunner, heading: pin.heading, isStale: isStale, isDisconnected: isDisconnected)
                    }
                }
            }
            .onChange(of: context.date) { _, _ in followCameraIfNeeded() }
        }
        // Dims the frozen last-known frame rather than hiding it outright, so spatial context
        // (roughly where everyone was) stays visible — reuses RunnerMapPin's SleepPin opacity
        // band, applied here at the whole-map level instead of per-pin since the entire view is
        // stale, not one runner. `allowsHitTesting(false)` keeps it purely visual so a stray tap
        // can't nudge the frozen camera.
        .overlay {
            if isUltraConstrained {
                Color.black.opacity(0.35)
                    .allowsHitTesting(false)
                    .transition(.opacity)
            }
        }
        .overlay(alignment: .topLeading) {
            // Only once the sheet is dragged open enough that the map fills most of the
            // screen — at the medium detent there's no room and the participants grid below
            // already serves this purpose.
            if visibleFraction > 0.9 && !isUltraConstrained {
                RunnerLegendRow(
                    names: pins.map(\.id),
                    currentRunnerName: currentRunnerName,
                    followedRunnerName: $followedRunnerName,
                    onFitAll: {
                        followedRunnerName = nil
                        fitCamera()
                    }
                )
                .padding(.top, 12)
                .padding(.leading, 12)
            }
        }
        .overlay(alignment: .top) {
            if isUltraConstrained {
                SatelliteStatusBadge(lastSent: lastSent)
                    .padding(.top, 12)
                    .padding(.horizontal, 12)
                    .transition(.move(edge: .top).combined(with: .opacity))
            }
        }
        .animation(.easeInOut(duration: 0.4), value: isUltraConstrained)
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
        // Auto-resume the instant the path clears — re-fit immediately rather than waiting for
        // the next per-second TimelineView tick to call followCameraIfNeeded/fitCamera, since
        // those are now unblocked but nothing else triggers them right away.
        .onChange(of: isUltraConstrained) { _, stillConstrained in
            guard !stillConstrained else { return }
            if followedRunnerName != nil {
                followCameraIfNeeded()
            } else {
                fitCamera()
            }
        }
    }

    /// Re-centers on the followed runner's current pin, keeping whatever zoom `followSpan`
    /// holds — called every second from the map's own timer so movement is picked up as soon
    /// as a new position lands, without a separate polling loop.
    private func followCameraIfNeeded() {
        // Frozen while ultra-constrained: pins aren't refreshing (GET polling is skipped), so
        // re-centering on a followed runner's stale last-known pin would just be motion for its
        // own sake. Camera picks back up automatically once this clears.
        guard !isUltraConstrained, let followedPin else { return }
        withAnimation(.easeInOut(duration: 0.6)) {
            cameraPosition = .region(visibleRegion(centeredOn: followedPin.coordinate, span: followSpan))
        }
    }

    private func fitCamera() {
        guard !isUltraConstrained, !pins.isEmpty else { return }
        if pins.count == 1 {
            withAnimation(.easeInOut(duration: 0.6)) {
                cameraPosition = .region(
                    visibleRegion(
                        centeredOn: pins[0].coordinate,
                        span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)
                    )
                )
            }
            return
        }

        let coordinates = pins.map(\.coordinate)
        let minLat = coordinates.map(\.latitude).min()!
        let maxLat = coordinates.map(\.latitude).max()!
        let minLon = coordinates.map(\.longitude).min()!
        let maxLon = coordinates.map(\.longitude).max()!

        let paddedMinLat = minLat - (maxLat - minLat) * 0.2
        let paddedMaxLat = maxLat + (maxLat - minLat) * 0.2
        let span = MKCoordinateSpan(
            latitudeDelta: max(0.01, paddedMaxLat - paddedMinLat),
            longitudeDelta: max(0.01, (maxLon - minLon) * 1.4)
        )
        withAnimation(.easeInOut(duration: 0.6)) {
            cameraPosition = .region(
                visibleRegion(topLatitude: paddedMaxLat, centerLongitude: (minLon + maxLon) / 2, span: span)
            )
        }
    }

    /// Builds a region whose visible-top-slice fraction (`visibleFraction`) frames the given
    /// point/content, by inflating the region's latitude span so the true (always-full-size)
    /// map view's clipped-off bottom sits below what's on screen, then shifting the center
    /// north so the wanted content still lands within the visible slice.
    private func visibleRegion(centeredOn coordinate: CLLocationCoordinate2D, span: MKCoordinateSpan) -> MKCoordinateRegion {
        visibleRegion(topLatitude: coordinate.latitude + span.latitudeDelta / 2, centerLongitude: coordinate.longitude, span: span)
    }

    private func visibleRegion(topLatitude: Double, centerLongitude: Double, span: MKCoordinateSpan) -> MKCoordinateRegion {
        let fraction = min(max(visibleFraction, 0.01), 1)
        let fullLatitudeDelta = span.latitudeDelta / fraction
        let center = CLLocationCoordinate2D(
            latitude: topLatitude - fullLatitudeDelta / 2,
            longitude: centerLongitude
        )
        let fullSpan = MKCoordinateSpan(latitudeDelta: fullLatitudeDelta, longitudeDelta: span.longitudeDelta)
        return MKCoordinateRegion(center: center, span: fullSpan)
    }
}

private extension RunnerPositionResponse {
    var coordinate: CLLocationCoordinate2D {
        CLLocationCoordinate2D(latitude: latitude, longitude: longitude)
    }
}

/// Shown over the map while `NativeMapView.isUltraConstrained` — mirrors `ContentView`'s
/// `offlineBanner` (`Label` + SF Symbol) but as a floating pill rather than a full-width bar,
/// since this overlays just the map rather than the whole screen.
private struct SatelliteStatusBadge: View {
    let lastSent: Date?

    var body: some View {
        VStack(spacing: 2) {
            Label("On satellite — sending position, map paused", systemImage: "antenna.radiowaves.left.and.right")
                .font(.subheadline.weight(.semibold))
                .multilineTextAlignment(.leading)
            if let lastSent {
                Text("Last sent \(lastSent, style: .relative) ago")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.8))
            }
        }
        .foregroundStyle(.white)
        .padding(.vertical, 10)
        .padding(.horizontal, 14)
        .frame(maxWidth: .infinity)
        .background(Color.orange, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .shadow(color: .black.opacity(0.25), radius: 4, y: 2)
    }
}
