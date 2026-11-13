import CoreLocation
import SwiftUI

/// The full-screen map shown while tracking in a named session: `TrackingMapScreen` fed with the session's
/// runners and route. Adds what only a session has: the invite share, the organiser's nearby-join
/// controls, and the participants row (tap to follow, hold to block). Stopping leaves the session.
struct SessionMapScreen: View {
    var location: LocationManager
    var crew: CrewStore

    @Environment(\.scenePhase) private var scenePhase
    @State private var map = TrackingMapController()
    @State private var showParticipants = false
    @State private var isUploadingRoute = false
    /// Who a touch-and-hold just targeted, showing the block confirmation; nil when none is up.
    @State private var blockCandidate: String?
    @State private var blockError: String?
    @State private var showNearbySettings = false
    @State private var showEndSession = false
    /// A short confirmation (or error) from the join-location button, cleared after a moment.
    @State private var nearbyNote: String?

    private var pins: [MapPin] {
        var result = location.runnerPositions
            .filter { $0.runnerName != location.runnerName }
            .map {
                MapPin(
                    id: $0.runnerName,
                    coordinate: CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude),
                    heading: $0.heading, timestamp: $0.parsedTimestamp)
            }
        // This device's own position straight from CoreLocation, so the dot is current without waiting
        // for the server.
        if let coordinate = location.currentCoordinate {
            result.append(MapPin(
                id: location.runnerName, coordinate: coordinate,
                heading: location.currentHeading, isSelf: true))
        }
        return result
    }

    var body: some View {
        TrackingMapScreen(
            location: location,
            crew: crew,
            controller: map,
            pins: pins,
            routes: [location.routeCoordinates],
            joinRange: joinRange,
            route: routeActions,
            messagePins: messagePinActions,
            onStop: { location.stop() },
            railTop: { inviteShare },
            railMiddle: { nearbyMenu },
            bottomLeading: { participantsRow }
        )
        .modifier(NearbyOrganiserControls(
            location: location,
            sessionId: nearbySessionId,
            showSettings: $showNearbySettings,
            showEndSession: $showEndSession,
            note: nearbyNote,
            onEnd: { await endSession() }
        ))
        // Who is in the session, so a member who hasn't sent a position yet still gets a dot. Then the
        // session's message pins, which don't come with this device's own posts, on a slow poll while
        // the map is on screen.
        .task {
            await location.loadLatestPositions()
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.pinPollInterval)
                if scenePhase == .active { await location.loadSessionPins() }
            }
        }
        .sheet(isPresented: $showParticipants) {
            ParticipantsSheet(
                names: sessionNames,
                me: location.runnerName,
                followedName: map.followedId,
                status: { name in
                    guard let pin = pins.first(where: { $0.id == name }) else { return nil }
                    return pin.timestamp.map { Date().timeIntervalSince($0) } ?? 0
                },
                onSelect: { name in
                    showParticipants = false
                    map.follow(name)
                },
                onBlock: { name in
                    showParticipants = false
                    blockCandidate = name
                }
            )
            .presentationDetents([.medium, .large])
        }
        .confirmationDialog(
            blockCandidate ?? "",
            isPresented: Binding(get: { blockCandidate != nil }, set: { if !$0 { blockCandidate = nil } }),
            titleVisibility: .visible
        ) {
            if let name = blockCandidate {
                Button("Block \(name)", role: .destructive) {
                    Task { await blockParticipant(name: name) }
                }
                Button("Cancel", role: .cancel) {}
            }
        } message: {
            let inCrew = blockCandidate
                .flatMap { location.participantUserIds[$0] }
                .map { id in crew.members.contains { $0.userId == id } } ?? false
            Text("They'll be removed from this session and won't be able to join any of your sessions again."
                + (inCrew ? " They'll also be removed from your crew." : ""))
        }
        .alert("Couldn't block user", isPresented: Binding(
            get: { blockError != nil },
            set: { if !$0 { blockError = nil } }
        )) {
            Button("OK", role: .cancel) {}
        } message: {
            if let blockError { Text(blockError) }
        }
    }

    // MARK: Map inputs

    /// The organiser's join range: a dotted ring around the join location while nearby is on.
    private var joinRange: LinzJoinRange? {
        guard let id = nearbySessionId, let status = location.nearbyStatuses[id], status.nearby,
              let center = location.nearbyMeetingPoints[id]
        else { return nil }
        return LinzJoinRange(center: center, radiusMeters: Double(status.radiusMeters))
    }

    /// The session's one route, shared by everyone in it.
    private var routeActions: RouteActions {
        RouteActions(
            hasRoute: location.hasRoute,
            isUploading: isUploadingRoute,
            removeMessage: "This removes the route for everyone in the session.",
            upload: { data in
                isUploadingRoute = true
                defer { isUploadingRoute = false }
                try await location.uploadRoute(gpx: data)
            },
            remove: { try await location.removeRoute() }
        )
    }

    private static let pinPollInterval: Duration = .seconds(15)

    /// Runners in the session drop pins; everyone in it sees them.
    private var messagePinActions: MessagePinActions {
        MessagePinActions(
            pins: location.sessionPins,
            canDrop: location.isTracking && location.activeMembership?.role == "runner",
            audience: "Everyone in this session will see it.",
            drop: { coordinate, message in try await location.dropSessionPin(at: coordinate, message: message) },
            delete: { pin in try await location.deleteSessionPin(pin) }
        )
    }

    // MARK: Controls

    @ViewBuilder
    private var inviteShare: some View {
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
    }

    @ViewBuilder
    private var nearbyMenu: some View {
        if let sessionId = nearbySessionId {
            // Tap: move the join location to here. Touch and hold for the range, switching off and End.
            Menu {
                Button {
                    Task { await setJoinLocation(sessionId) }
                } label: {
                    Label("Set join location here", systemImage: "mappin.and.ellipse")
                }
                Button {
                    showNearbySettings = true
                } label: {
                    Label("Nearby settings…", systemImage: "slider.horizontal.3")
                }
                Button(role: .destructive) {
                    showEndSession = true
                } label: {
                    Label("End session", systemImage: "xmark.circle")
                }
            } label: {
                Image(systemName: "mappin.and.ellipse")
                    .font(.system(size: 18, weight: .semibold))
                    .foregroundStyle(.black)
                    .frame(width: 44, height: 44)
                    .background(Color.white.opacity(0.85), in: Circle())
                    .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
            } primaryAction: {
                Task { await setJoinLocation(sessionId) }
            }
            .accessibilityLabel("Set join location")
        }
    }

    /// The session's id when this device made it and made it nearby, so the join-location controls apply;
    /// nil otherwise (a joiner, or a session without nearby join).
    private var nearbySessionId: String? {
        guard let id = location.activeMembership?.sessionId,
              location.ownedSessionIds.contains(id),
              location.nearbyStatuses[id] != nil
        else { return nil }
        return id
    }

    private func showNearbyNote(_ text: String) async {
        withAnimation { nearbyNote = text }
        try? await Task.sleep(for: .seconds(2.5))
        withAnimation { nearbyNote = nil }
    }

    private func setJoinLocation(_ sessionId: String) async {
        await showNearbyNote(await location.pingJoinLocation(sessionId: sessionId))
    }

    private func endSession() async {
        guard let sessionId = nearbySessionId else { return }
        do {
            try await location.endSession(sessionId: sessionId)
        } catch {
            await showNearbyNote(error.localizedDescription)
        }
    }

    private func blockParticipant(name: String) async {
        guard let userId = location.participantUserIds[name] else {
            blockError = "Couldn't find that participant. Try refreshing and blocking again."
            return
        }
        do {
            try await location.blockUser(userId: userId, displayName: name)
            // The server also ends any crew link with them, so reload rather than show a stale crew.
            await crew.refresh()
        } catch {
            blockError = error.localizedDescription
        }
    }

    /// You first, then everyone else in the session: members from the session list plus anyone who has a
    /// position (the list only loads on a refresh, positions arrive with every send).
    private var sessionNames: [String] {
        let me = location.runnerName
        var others = location.participants.filter { $0 != me }
        for position in location.runnerPositions where position.runnerName != me && !others.contains(position.runnerName) {
            others.append(position.runnerName)
        }
        return [me] + others
    }

    /// Up to four people as separate dots, you first, in the pins' colours; a dashed edge means no recent
    /// position. Tapping a dot centres the map on that runner; tap it again, or the fit-all button, to see
    /// everyone. With more than four the dots overlap, as in the crew row, and the whole chip opens a list.
    private var participantsRow: some View {
        let me = location.runnerName
        let names = sessionNames
        let overlapped = names.count > Self.separateDotLimit
        let shown = names.prefix(overlapped ? Self.overlappedFaces : Self.separateDotLimit)
        let extra = names.count - shown.count
        return TimelineView(.periodic(from: .now, by: 1)) { context in
            let chip = HStack(spacing: overlapped ? -10 : 6) {
                ForEach(Array(shown), id: \.self) { name in
                    let pin = pins.first { $0.id == name }
                    let age = pin?.timestamp.map { context.date.timeIntervalSince($0) }
                    let dot = RunnerCircle(
                        name: name,
                        size: 34,
                        fontSize: 11,
                        fillColor: name == me ? RunnerColorPalette.currentUser : RunnerColorPalette.color(for: name),
                        isDisconnected: pin == nil || (age.map { $0 > MapPinAge.disconnectedAfter } ?? false)
                    )
                    .overlay(
                        Circle()
                            .stroke(Color.black, lineWidth: 2.5)
                            .opacity(map.followedId == name ? 1 : 0)
                            .padding(-3)
                    )
                    if overlapped {
                        // A chip-coloured edge keeps overlapped dots apart.
                        dot.background(Circle().fill(Color.white)).overlay(Circle().strokeBorder(Color.white, lineWidth: 2))
                    } else {
                        dot
                            .onTapGesture {
                                guard pin != nil else { return }
                                map.toggleFollow(name)
                            }
                            .onLongPressGesture {
                                if name != me { blockCandidate = name }
                            }
                            .accessibilityElement(children: .ignore)
                            .accessibilityLabel(name == me ? "You" : name)
                            .accessibilityAddTraits(.isButton)
                            .accessibilityHint(pin == nil ? "No recent position" : "Centres the map on them")
                    }
                }
                if extra > 0 {
                    Text("+\(extra)")
                        .font(.system(size: 12.5, weight: .bold))
                        .foregroundStyle(.black.opacity(0.6))
                        .padding(.leading, overlapped ? 14 : 6)
                }
            }
            .padding(.horizontal, 10)
            .frame(height: 46)
            .background(Color.white.opacity(0.85), in: Capsule())
            .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
            if overlapped {
                Button { showParticipants = true } label: { chip }
                    .buttonStyle(.plain)
                    .accessibilityLabel("\(names.count) people in this session")
                    .accessibilityHint("Opens the list")
            } else {
                chip
            }
        }
    }

    private static let separateDotLimit = 4
    private static let overlappedFaces = 6
}

/// Everyone in the session, for when there are too many dots to tap one at a time. Tapping someone with a
/// position closes the list and centres the map on them.
private struct ParticipantsSheet: View {
    let names: [String]
    let me: String
    let followedName: String?
    /// Seconds since the person's last position, or nil if they have none.
    let status: (String) -> TimeInterval?
    let onSelect: (String) -> Void
    let onBlock: (String) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("In this session · \(names.count)")
                .font(.title3.bold())
                .padding(.horizontal, 20)
                .padding(.top, 20)
            ScrollView {
                VStack(spacing: 0) {
                    ForEach(names, id: \.self) { name in
                        let age = status(name)
                        Button { onSelect(name) } label: {
                            HStack(spacing: 14) {
                                RunnerCircle(
                                    name: name,
                                    size: 40,
                                    fontSize: 13,
                                    fillColor: name == me ? RunnerColorPalette.currentUser : RunnerColorPalette.color(for: name),
                                    isDisconnected: age == nil || (age ?? 0) > MapPinAge.disconnectedAfter
                                )
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(name == me ? "You" : name).font(.headline)
                                    Text(Self.statusText(age)).font(.caption).foregroundStyle(.secondary)
                                }
                                Spacer()
                                if followedName == name {
                                    Image(systemName: "scope").foregroundStyle(.secondary)
                                }
                            }
                            .padding(.horizontal, 20)
                            .padding(.vertical, 10)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(age == nil)
                        .contextMenu {
                            if name != me {
                                Button(role: .destructive) { onBlock(name) } label: {
                                    Label("Block \(name)", systemImage: "hand.raised")
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static func statusText(_ age: TimeInterval?) -> String {
        guard let age else { return "No position yet" }
        if age < 60 { return "Updated just now" }
        return "Updated \(Int(age / 60)) min ago"
    }
}

/// Shows the session map full-screen while tracking in a named session, and as the backdrop while the
/// "share your location?" dialog is up, so joining goes from the code straight to the map (as Go goes from
/// "Before you go" to its map). Stopping, or declining to share, leaves the session and returns to the landing
/// screen: there is no separate session screen. Not when
/// out casually, which has its own map. The dialog is presented from here because a sheet can't be
/// presented from the screen underneath a full-screen cover. Lives at the root so it can present over the
/// landing screen too.
struct SessionMapCover: ViewModifier {
    var location: LocationManager
    var crew: CrewStore
    var casualMapPresented: Bool
    @Binding var showShareConsent: Bool

    func body(content: Content) -> some View {
        content
            .fullScreenCover(isPresented: Binding(
                get: { location.activeMembership != nil && (location.isTracking || showShareConsent) && !casualMapPresented },
                set: { _ in }
            )) {
                SessionMapScreen(location: location, crew: crew)
                    .preferredColorScheme(location.appearanceMode.colorScheme)
                    .sheet(isPresented: $showShareConsent) {
                        if let membership = location.activeMembership {
                            ShareLocationConsentView(
                                sessionName: membership.sessionName,
                                onShare: { duration in
                                    location.recordSharingConsent(sessionId: membership.sessionId)
                                    location.start(duration: duration)
                                },
                                onDecline: { leave(membership) }
                            )
                            .presentationDetents([.medium, .large])
                            .preferredColorScheme(location.appearanceMode.colorScheme)
                        }
                    }
            }
            // Swiping the dialog away without choosing counts as declining.
            .onChange(of: showShareConsent) { _, isShowing in
                guard !isShowing, !location.isTracking, let membership = location.activeMembership else { return }
                leave(membership)
            }
            // Stopping ends your part in the session, as stopping Go ends a run: Stop, "Stop now" and the
            // auto-stop all land here, and the person is taken back to the landing screen. Rejoin with the
            // invite code to carry on.
            .onChange(of: location.isTracking) { wasTracking, isTracking in
                guard wasTracking, !isTracking, !casualMapPresented,
                      let membership = location.activeMembership else { return }
                leave(membership)
            }
    }

    private func leave(_ membership: SessionMembership) {
        Task { try? await location.leaveSession(sessionId: membership.sessionId) }
    }
}
