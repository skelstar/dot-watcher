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
    /// The runner the map stays centred on, picked by tapping their dot; nil frames everyone.
    @State private var followedName: String?

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
                        participantsRow
                        Spacer(minLength: 12)
                        HoldToStopPill(progress: $stopProgress) { location.stop() }
                    }
                    .padding(.horizontal, 14)
                    .padding(.bottom, 26)
                }
            }
        }
        // Who is in the session, so a member who hasn't sent a position yet still gets a dot.
        .task { await location.loadLatestPositions() }
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
            .onChange(of: context.date) { _, _ in followIfNeeded() }
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

    /// Keeps the map on the followed runner (their pin moves as new positions land), at the zoom the
    /// map already has. Frozen on satellite, like the rest of the map.
    private func followIfNeeded() {
        guard !location.isUltraConstrained, let followedName else { return }
        guard let pin = pins.first(where: { $0.id == followedName }) else {
            self.followedName = nil  // they left, or have no position any more
            return
        }
        withAnimation(.easeInOut(duration: 0.6)) {
            camera = .region(MKCoordinateRegion(
                center: pin.coordinate, span: MKCoordinateSpan(latitudeDelta: 0.01, longitudeDelta: 0.01)))
        }
    }

    private func fitAll() {
        followedName = nil
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

    /// Everyone in the session as a dot, you first: the same colours as their pins. A dot with a dashed
    /// edge has no recent position. Tapping one centres the map on that runner; tap it again, or the
    /// fit-all button, to see everyone.
    private var participantsRow: some View {
        let me = location.runnerName
        // Members from the session list, plus anyone who has a position (the list only loads on a
        // refresh, positions arrive with every send).
        var others = location.participants.filter { $0 != me }
        for position in location.runnerPositions where position.runnerName != me && !others.contains(position.runnerName) {
            others.append(position.runnerName)
        }
        let names = [me] + others
        let shown = names.prefix(4)
        let extra = names.count - shown.count
        return TimelineView(.periodic(from: .now, by: 1)) { context in
            HStack(spacing: 6) {
                ForEach(Array(shown), id: \.self) { name in
                    let pin = pins.first { $0.id == name }
                    let age = pin?.timestamp.map { context.date.timeIntervalSince($0) }
                    RunnerCircle(
                        name: name,
                        size: 34,
                        fontSize: 11,
                        fillColor: name == me ? RunnerColorPalette.currentUser : RunnerColorPalette.color(for: name),
                        isDisconnected: pin == nil || (age.map { $0 > NativeMapView.disconnectedAfter } ?? false)
                    )
                    .overlay(
                        Circle()
                            .stroke(Color.black, lineWidth: 2.5)
                            .opacity(followedName == name ? 1 : 0)
                            .padding(-3)
                    )
                    .onTapGesture {
                        guard pin != nil else { return }
                        followedName = followedName == name ? nil : name
                        if followedName == nil { fitAll() } else { followIfNeeded() }
                    }
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel(name == me ? "You" : name)
                    .accessibilityAddTraits(.isButton)
                    .accessibilityHint(pin == nil ? "No recent position" : "Centres the map on them")
                }
                if extra > 0 {
                    Text("+\(extra)")
                        .font(.system(size: 12.5, weight: .bold))
                        .foregroundStyle(.black.opacity(0.6))
                        .padding(.horizontal, 2)
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 46)
            .background(Color.white.opacity(0.85), in: Capsule())
            .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
        }
    }
}
