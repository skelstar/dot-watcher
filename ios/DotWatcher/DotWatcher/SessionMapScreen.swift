import MapKit
import SwiftUI

/// The full-screen map shown while tracking in a named session: the same layout as the casual map
/// (`CasualMapScreen`), with the session's runners and route on it. Stopping returns to the session
/// screen, where the invite code, participants and Leave still live. See `MapChrome.swift`.
struct SessionMapScreen: View {
    var location: LocationManager

    @State private var camera: MapCameraPosition = .automatic
    @State private var stopProgress: CGFloat = 0
    @State private var showExtend = false
    @State private var fittedCount = -1

    private struct Pin: Identifiable {
        let id: String
        let coordinate: CLLocationCoordinate2D
        let heading: Double?
        let isSelf: Bool
        /// nil for this device's own live pin, which is never stale.
        let timestamp: Date?
    }

    private var pins: [Pin] {
        var result = location.runnerPositions
            .filter { $0.runnerName != location.runnerName }
            .map {
                Pin(
                    id: $0.runnerName,
                    coordinate: CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude),
                    heading: $0.heading, isSelf: false, timestamp: $0.parsedTimestamp)
            }
        // This device's own position straight from CoreLocation, so the dot is current without waiting
        // for the server.
        if let coordinate = location.currentCoordinate {
            result.append(Pin(
                id: location.runnerName, coordinate: coordinate,
                heading: location.currentHeading, isSelf: true, timestamp: nil))
        }
        return result
    }

    var body: some View {
        ZStack {
            map
            VStack {
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(spacing: 8) {
                            MapWordmark(runnerName: location.runnerName)
                            MapCountdown(location: location) { showExtend = true }
                        }
                        EndingSoonPill(location: location)
                    }
                    Spacer()
                    rail
                }
                .padding(.horizontal, 14)
                .padding(.top, 8)
                if location.isUltraConstrained {
                    SatelliteStatusBadge(lastSent: location.lastSent, nextRetryAt: location.nextRetryAt)
                        .padding(.horizontal, 12)
                        .padding(.top, 10)
                }
                Spacer()
                if location.pausedForBattery {
                    BatteryPausedCard(location: location) { location.stop() }
                        .padding(.horizontal, 12)
                        .padding(.bottom, 16)
                } else {
                    HStack(alignment: .bottom) {
                        sessionChip
                        Spacer()
                        HoldToStopPill(progress: $stopProgress) { location.stop() }
                    }
                    .padding(.horizontal, 14)
                    .padding(.bottom, 26)
                }
            }
        }
        .sheet(isPresented: $showExtend) {
            ExtendSheet(
                onExtend: { location.extendTracking(by: $0) },
                onStopNow: {
                    showExtend = false
                    location.stop()
                },
                location: location
            )
            .presentationDetents([.height(240)])
        }
    }

    // MARK: Map

    private var map: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            Map(position: $camera) {
                if location.routeCoordinates.count > 1 {
                    MapPolyline(coordinates: location.routeCoordinates)
                        .stroke(Color(red: 252 / 255, green: 76 / 255, blue: 2 / 255).opacity(0.9), lineWidth: 5) // Strava orange
                }
                ForEach(pins) { pin in
                    Annotation("", coordinate: pin.coordinate) {
                        let age = pin.timestamp.map { context.date.timeIntervalSince($0) }
                        RunnerMapPin(
                            name: pin.id,
                            isHighlighted: pin.isSelf,
                            heading: pin.heading,
                            isStale: age.map { $0 > NativeMapView.staleAfter } ?? false,
                            isDisconnected: age.map { $0 > NativeMapView.disconnectedAfter } ?? false
                        )
                    }
                }
            }
        }
        // Dims the frozen last-known frame while on satellite: positions stop refreshing, so the map is
        // left as it was rather than hidden.
        .overlay {
            if location.isUltraConstrained {
                Color.black.opacity(0.35).allowsHitTesting(false)
            }
        }
        .ignoresSafeArea()
        .onChange(of: pins.count) { _, count in
            // Frame everyone whenever someone arrives or leaves; the fit-all button does it on demand.
            if count != fittedCount {
                fittedCount = count
                fitAll()
            }
        }
        .onAppear { fitAll() }
    }

    private func fitAll() {
        guard !location.isUltraConstrained else { return }
        let coordinates = pins.map(\.coordinate)
        guard let first = coordinates.first else { return }
        if coordinates.count == 1 {
            withAnimation(.easeInOut(duration: 0.6)) {
                camera = .region(MKCoordinateRegion(
                    center: first, span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)))
            }
            return
        }
        let minLat = coordinates.map(\.latitude).min()!
        let maxLat = coordinates.map(\.latitude).max()!
        let minLon = coordinates.map(\.longitude).min()!
        let maxLon = coordinates.map(\.longitude).max()!
        withAnimation(.easeInOut(duration: 0.6)) {
            camera = .region(MKCoordinateRegion(
                center: CLLocationCoordinate2D(latitude: (minLat + maxLat) / 2, longitude: (minLon + maxLon) / 2),
                span: MKCoordinateSpan(
                    latitudeDelta: max(0.01, (maxLat - minLat) * 1.5),
                    longitudeDelta: max(0.01, (maxLon - minLon) * 1.5))))
        }
    }

    // MARK: Controls

    private var rail: some View {
        VStack(spacing: 10) {
            if let inviteCode = location.activeMembership?.inviteCode {
                ShareLink(item: InviteLink.shareMessage(base: location.webBaseURL, code: inviteCode)) {
                    Image(systemName: "square.and.arrow.up")
                        .font(.system(size: 18, weight: .semibold))
                        .foregroundStyle(.black)
                        .frame(width: 44, height: 44)
                        .background(Color.white.opacity(0.85), in: Circle())
                        .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Share the invite")
            }
            MapRailButton(symbol: "arrow.up.left.and.arrow.down.right", label: "Fit all runners") { fitAll() }
        }
    }

    /// "TESTSESS · 3 runners": which session this is, and how many of its runners are on the map.
    private var sessionChip: some View {
        let count = pins.count
        let name = location.activeMembership?.sessionName ?? ""
        let text = (name.isEmpty ? "" : "\(name)  ·  ") + (count == 1 ? "1 runner" : "\(count) runners")
        return Text(text)
            .font(.system(size: 12.5, weight: .semibold))
            .foregroundStyle(.black)
            .padding(.horizontal, 14)
            .frame(height: 36)
            .background(Color.white.opacity(0.85), in: Capsule())
            .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
    }
}
