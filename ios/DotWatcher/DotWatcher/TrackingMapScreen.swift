import CoreLocation
import SwiftUI
import UniformTypeIdentifiers

/// A dot on the tracking map: a runner, this device, or (in `nearby`) an anonymous grey dot. Both map
/// sources build these from their own position types.
struct MapPin: Identifiable {
    let id: String
    /// The account's display name, for the letters on the dot; nil falls back to `id`.
    var displayName: String? = nil
    let coordinate: CLLocationCoordinate2D
    var heading: Double? = nil
    var isSelf = false
    /// In a named session: drawn with a padlock (Go only; a session's own members never are).
    var isLocked = false
    /// When the position was taken; nil for this device's own live pin, which is never stale.
    var timestamp: Date? = nil
}

/// Adding, replacing and removing a route from the map's Route button. Each action throws to show
/// its message in the shell's error alert.
struct RouteActions {
    let hasRoute: Bool
    let isUploading: Bool
    /// The body of the "Remove route?" confirmation: who loses the route.
    let removeMessage: String
    let upload: (Data) async throws -> Void
    let remove: () async throws -> Void
}

/// A failure a map source reports with its own wording.
struct MapActionError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Where the tracking map is looking: fit everyone (and keep fitting as they move), follow one runner,
/// or left where the person dragged it. Owned by the screen wrapping `TrackingMapScreen`, so its own
/// controls (the session's participants row) can follow someone too.
@Observable
final class TrackingMapController {
    fileprivate(set) var camera = LinzMapCamera()
    /// While set, the map re-frames everyone as positions move; fit-all sets it, dragging clears it.
    fileprivate(set) var fitLocked = true
    /// The pin the map stays centred on; nil frames everyone (or leaves the camera alone).
    private(set) var followedId: String?
    /// Bumped by every request below, so the shell re-frames straight away rather than on the next tick.
    fileprivate private(set) var reframeRequest = 0

    func follow(_ id: String) {
        followedId = id
        fitLocked = false
        reframeRequest += 1
    }

    func fitAll() {
        followedId = nil
        fitLocked = true
        reframeRequest += 1
    }

    /// Tapping the followed runner again lets go and frames everyone; tapping someone else follows them.
    func toggleFollow(_ id: String) {
        if followedId == id { fitAll() } else { follow(id) }
    }

    /// Shows a region (a just-added route) and stops following or fitting, so the next tick doesn't
    /// pull the camera back; fit-all turns fitting back on.
    fileprivate func show(_ region: LinzMapRegion) {
        followedId = nil
        fitLocked = false
        camera.move(to: region)
    }

    fileprivate func stopFollowing() { followedId = nil }
    fileprivate func userMovedCamera() { fitLocked = false }
    fileprivate func move(to region: LinzMapRegion) { camera.move(to: region) }
}

/// The full-screen map while sharing, for both Go and a named session: the LINZ map with everyone's dots,
/// the timer, a rail of buttons and Stop. The shell owns everything the two have in common:
/// the camera, fit and follow, satellite mode, the Route button and file picker, the extend sheet,
/// Settings, and the bottom bar's Stop or battery card. `CasualMapScreen` and `SessionMapScreen` wrap
/// it, supplying the dots, what Stop does, and their own rail buttons, bottom-left view and sheets.
struct TrackingMapScreen<RailTop: View, RailMiddle: View, BottomLeading: View>: View {
    var location: LocationManager
    var crew: CrewStore
    var controller: TrackingMapController
    var pins: [MapPin]
    /// Anonymous grey dots, drawn under the pins and included in fit-everyone; empty for a session.
    var nearby: [MapPin] = []
    var routes: [[CLLocationCoordinate2D]] = []
    var joinRange: LinzJoinRange?
    /// nil hides the Route button.
    var route: RouteActions?
    /// Message pins on this map, and dropping and deleting them; nil for none.
    var messagePins: MessagePinActions?
    let onStop: () -> Void
    /// Rail buttons above Fit all.
    @ViewBuilder var railTop: () -> RailTop
    /// Rail buttons between Fit all and Route.
    @ViewBuilder var railMiddle: () -> RailMiddle
    @ViewBuilder var bottomLeading: () -> BottomLeading

    /// Fixed start for the map's per-second timeline. `.periodic(from: .now, …)` restarts on every body
    /// update, and each tick's re-frame updates the body, so ticks would fire back to back and hang.
    @State private var timelineStart = Date()
    @State private var fittedCount = -1
    @State private var showExtend = false
    @State private var showSettings = false
    @State private var showRoutePicker = false
    @State private var showRemoveRoute = false
    @State private var routeError: String?
    @State private var pendingPin: PendingMessagePin?
    @State private var selectedPin: MapMessagePin?

    private static var runnerMarkerPrefix: String { "runner-" }
    private static var messagePinMarkerPrefix: String { "msgpin-" }

    var body: some View {
        ZStack {
            map
            LinzAttribution()
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            chrome
        }
        .modifier(TrackingMapSheets(
            location: location,
            crew: crew,
            onStop: onStop,
            showExtend: $showExtend,
            showSettings: $showSettings,
            showRoutePicker: $showRoutePicker,
            showRemoveRoute: $showRemoveRoute,
            routeError: $routeError,
            removeRouteMessage: route?.removeMessage ?? "",
            onPickedRoute: handlePickedRoute,
            onRemoveRoute: removeRoute
        ))
        .modifier(MessagePinSheets(actions: messagePins, pendingPin: $pendingPin, selectedPin: $selectedPin))
    }

    // MARK: Map

    private var map: some View {
        TimelineView(.periodic(from: timelineStart, by: 1)) { context in
            LinzMapView(
                markers: markers(at: context.date),
                extraRoutes: routes,
                joinRange: joinRange,
                camera: controller.camera,
                onUserMovedCamera: { controller.userMovedCamera() },
                onMarkerTap: handleMarkerTap,
                onLongPress: longPressHandler
            )
            .onChange(of: context.date) { _, _ in reframe() }
        }
        // Dims the frozen last-known frame while on satellite: positions stop refreshing, so the map is
        // left as it was rather than hidden.
        .overlay {
            if location.isUltraConstrained {
                Color.black.opacity(0.35).allowsHitTesting(false)
            }
        }
        .ignoresSafeArea()
        .onChange(of: pins.count + nearby.count) { _, count in
            // Frame everyone whenever someone arrives (not when they leave); the fit-all button does it on demand.
            let arrived = count > fittedCount
            fittedCount = count
            if arrived { controller.fitAll() }
        }
        .onChange(of: controller.reframeRequest) { _, _ in reframe() }
        .onAppear {
            controller.fitAll()
            reframe()
        }
    }

    /// Anonymous nearby dots under the named pins.
    private func markers(at date: Date) -> [LinzMapMarker] {
        let pinSize = location.mapDotSize.pinSize
        let greyDots = nearby.map { dot in
            LinzMapMarker(id: "nearby-\(dot.id)", coordinate: dot.coordinate) {
                NearbyDotView(isLocked: dot.isLocked, pinSize: pinSize)
            }
        }
        // While sharing, the own dot's ring counts down to the next send, in whole seconds like the
        // 1-second timeline that redraws it. None while paused for battery: no send is coming.
        let sendCountdown: Double? = location.isTracking && !location.pausedForBattery
            ? ceil(location.nextPostAt(from: date).timeIntervalSince(date)) / location.interval
            : nil
        let people = pins.map { pin in
            let age = pin.timestamp.map { date.timeIntervalSince($0) }
            return LinzMapMarker(id: Self.runnerMarkerPrefix + pin.id, coordinate: pin.coordinate, zIndex: 1) {
                RunnerMapPin(
                    name: pin.id,
                    label: pin.displayName,
                    size: pinSize,
                    isHighlighted: pin.isSelf,
                    heading: pin.heading,
                    isStale: age.map { $0 > MapPinAge.staleAfter } ?? false,
                    isDisconnected: age.map { $0 > MapPinAge.disconnectedAfter } ?? false,
                    sendCountdown: pin.isSelf ? sendCountdown : nil
                )
                .overlay(alignment: .bottomTrailing) {
                    if pin.isLocked { LockBadge().offset(x: 4, y: 4) }
                }
            }
        }
        let messages = (messagePins?.pins ?? []).map { pin in
            LinzMapMarker(id: Self.messagePinMarkerPrefix + pin.id, coordinate: pin.coordinate, zIndex: 2) {
                MessagePinMarker(isMine: pin.isMine == true)
            }
        }
        return greyDots + people + messages
    }

    /// Tapping a runner's dot follows them; tapping them again goes back to everyone. Grey dots are
    /// anonymous and can't be followed.
    private func handleMarkerTap(_ markerId: String) {
        if markerId.hasPrefix(Self.messagePinMarkerPrefix) {
            let id = String(markerId.dropFirst(Self.messagePinMarkerPrefix.count))
            selectedPin = messagePins?.pins.first { $0.id == id }
            return
        }
        guard markerId.hasPrefix(Self.runnerMarkerPrefix) else { return }
        controller.toggleFollow(String(markerId.dropFirst(Self.runnerMarkerPrefix.count)))
    }

    /// Press and hold drops a message pin there, when this map's source allows it.
    private var longPressHandler: ((CLLocationCoordinate2D) -> Void)? {
        guard messagePins?.canDrop == true else { return nil }
        return { pendingPin = PendingMessagePin(coordinate: $0) }
    }

    /// Runs every second and on each controller request. Frozen on satellite, like the rest of the map.
    private func reframe() {
        guard !location.isUltraConstrained else { return }
        if let followedId = controller.followedId {
            guard let pin = pins.first(where: { $0.id == followedId }) else {
                controller.stopFollowing()  // they left, or have no position any more
                return
            }
            controller.move(to: LinzMapRegion(center: pin.coordinate, span: 0.01))
        } else if controller.fitLocked,
                  let region = LinzMapRegion.fitting(pins.map(\.coordinate) + nearby.map(\.coordinate)) {
            controller.move(to: region)
        }
    }

    // MARK: Chrome

    private var chrome: some View {
        VStack {
            HStack(alignment: .top) {
                // Nothing to count down until sharing starts: the session map is also the backdrop for
                // the "share your location?" dialog.
                if location.isTracking {
                    MapTimerPill(location: location, runnerName: location.runnerName) { showExtend = true }
                } else {
                    MapWordmark(runnerName: location.runnerName)
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
                BatteryPausedCard(location: location, onStop: onStop)
                    .padding(.horizontal, 12)
                    .padding(.bottom, 16)
            } else {
                HStack(alignment: .bottom) {
                    bottomLeading()
                    Spacer(minLength: 12)
                    if location.isTracking {
                        StopPill(onStop: onStop)
                    }
                }
                .padding(.horizontal, 14)
                .padding(.bottom, 26)
            }
        }
    }

    private var rail: some View {
        VStack(spacing: 10) {
            railTop()
            MapRailButton(symbol: "arrow.up.left.and.arrow.down.right", label: "Fit all runners") {
                controller.fitAll()
            }
            railMiddle()
            if let route {
                routeMenu(route)
            }
            if location.isAuthenticated {
                MapRailButton(symbol: "gearshape", label: "Settings") { showSettings = true }
            }
        }
    }

    /// Add or replace a GPX route, or remove it.
    private func routeMenu(_ route: RouteActions) -> some View {
        Menu {
            Button {
                showRoutePicker = true
            } label: {
                Label(
                    route.hasRoute ? "Replace route (GPX)…" : "Add route (GPX)…",
                    systemImage: "point.topleft.down.to.point.bottomright.curvepath"
                )
            }
            if route.hasRoute {
                Button(role: .destructive) {
                    showRemoveRoute = true
                } label: {
                    Label("Remove route", systemImage: "trash")
                }
            }
        } label: {
            Image(systemName: "point.topleft.down.to.point.bottomright.curvepath")
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(.black)
                .frame(width: 44, height: 44)
                .background(Color.white.opacity(0.85), in: Circle())
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
        }
        .disabled(route.isUploading)
        .accessibilityLabel("Route")
    }

    // MARK: Route

    /// Reads the chosen GPX file, uploads it through the source, then shows the whole route.
    private func handlePickedRoute(_ result: Result<URL, Error>) {
        guard let route else { return }
        switch result {
        case .failure(let error):
            routeError = error.localizedDescription
        case .success(let url):
            // Files outside the app's sandbox need access opened for the read and closed straight after.
            let scoped = url.startAccessingSecurityScopedResource()
            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else {
                routeError = "Couldn't read that file."
                return
            }
            Task {
                do {
                    try await route.upload(data)
                    if let region = LinzMapRegion.fitting(GpxRouteParser.parse(data)) {
                        controller.show(region)
                    }
                } catch {
                    routeError = error.localizedDescription
                }
            }
        }
    }

    private func removeRoute() {
        guard let route else { return }
        Task {
            do { try await route.remove() }
            catch { routeError = error.localizedDescription }
        }
    }
}

/// The shell's sheets and alerts, split from `TrackingMapScreen.body` to keep that expression within the
/// Swift type-checker's budget.
private struct TrackingMapSheets: ViewModifier {
    var location: LocationManager
    var crew: CrewStore
    let onStop: () -> Void
    @Binding var showExtend: Bool
    @Binding var showSettings: Bool
    @Binding var showRoutePicker: Bool
    @Binding var showRemoveRoute: Bool
    @Binding var routeError: String?
    let removeRouteMessage: String
    let onPickedRoute: (Result<URL, Error>) -> Void
    let onRemoveRoute: () -> Void

    /// What the route picker offers: .gpx files (a type the system may not know by name), or any XML.
    private static var gpxTypes: [UTType] { [UTType(filenameExtension: "gpx"), .xml].compactMap { $0 } }

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: $showExtend) {
                ExtendSheet(
                    onExtend: { location.extendTracking(by: $0) },
                    onStopNow: {
                        showExtend = false
                        onStop()
                    },
                    location: location
                )
                .presentationDetents([.height(240)])
            }
            .sheet(isPresented: $showSettings) {
                AuthSheet(location: location, crew: crew)
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            // Signing out from the settings sheet hands over to the sign-in cover on `ContentView`; this
            // sheet must not still be up when it presents.
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if !isAuthenticated { showSettings = false }
            }
            .fileImporter(isPresented: $showRoutePicker, allowedContentTypes: Self.gpxTypes, onCompletion: onPickedRoute)
            .alert("Remove route?", isPresented: $showRemoveRoute) {
                Button("Remove", role: .destructive, action: onRemoveRoute)
                Button("Cancel", role: .cancel) {}
            } message: {
                Text(removeRouteMessage)
            }
            .alert("Couldn't update the route", isPresented: Binding(
                get: { routeError != nil },
                set: { if !$0 { routeError = nil } }
            )) {
                Button("OK", role: .cancel) {}
            } message: {
                if let routeError { Text(routeError) }
            }
    }
}

/// Dropping a message pin where the person pressed and held, and a tapped pin's detail.
private struct MessagePinSheets: ViewModifier {
    let actions: MessagePinActions?
    @Binding var pendingPin: PendingMessagePin?
    @Binding var selectedPin: MapMessagePin?

    func body(content: Content) -> some View {
        content
            .sheet(item: $pendingPin) { pending in
                DropMessagePinSheet(audience: actions?.audience ?? "") { message in
                    try await actions?.drop(pending.coordinate, message)
                }
                .presentationDetents([.medium])
            }
            .sheet(item: $selectedPin) { pin in
                MessagePinDetailSheet(pin: pin) {
                    try await actions?.delete(pin)
                }
                .presentationDetents([.height(240), .medium])
            }
    }
}

// MARK: - Map pieces

/// A person who is not in the crew: a grey dot with a white edge and "?" for initials, and a padlock
/// when they are in a named session. Nothing that says who it is. 90% of a runner's dot at the
/// chosen dot size, so it follows the Dot size setting.
private struct NearbyDotView: View {
    let isLocked: Bool
    /// The `RunnerMapPin` size in use on this map.
    let pinSize: CGFloat

    private var size: CGFloat { RunnerMapPin.circleSize(for: pinSize) * 0.9 }

    var body: some View {
        Circle()
            .fill(Color(red: 0x9E / 255, green: 0xA3 / 255, blue: 0xA8 / 255))
            .overlay(Circle().stroke(.white, lineWidth: 2))
            .frame(width: size, height: size)
            .overlay {
                Text("?")
                    .font(.system(size: size * 0.4, weight: .bold))
                    .foregroundStyle(.white)
            }
            .overlay(alignment: .bottomTrailing) {
                if isLocked { LockBadge().offset(x: 4, y: 4) }
            }
            .accessibilityLabel(isLocked ? "Someone nearby, in a session" : "Someone nearby")
    }
}

/// The padlock on a crew member's dot when they are in a named session rather than out casually.
private struct LockBadge: View {
    var body: some View {
        Image(systemName: "lock.fill")
            .font(.system(size: 8, weight: .bold))
            .foregroundStyle(.white)
            .frame(width: 16, height: 16)
            .background(Circle().fill(Color(red: 0x14 / 255, green: 0x18 / 255, blue: 0x1F / 255)))
            .accessibilityLabel("In a session")
    }
}
