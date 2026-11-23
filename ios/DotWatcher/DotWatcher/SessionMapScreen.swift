import CoreLocation
import SwiftUI

/// The full-screen map shown while tracking in a named session: `TrackingMapScreen` fed with the session's
/// runners and route. Adds what only a session has: the invite share, the organiser's nearby-join
/// controls, and the participants row (tap to follow, hold to block). Stopping leaves the session.
struct SessionMapScreen: View {
    var location: LocationManager
    var crew: CrewStore

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
            controller: map,
            pins: pins,
            routes: [location.routeCoordinates],
            joinRange: joinRange,
            route: routeActions,
            onStop: { location.stop() },
            railTop: { inviteShare },
            railMiddle: {
                nearbyMenu
                crewButton
            },
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
        // Who is in the session, so a member who hasn't sent a position yet still gets a dot.
        .task { await location.loadLatestPositions() }
        // The crew sheet lives here, not on `ContentView`, because this map is a full-screen cover (as on
        // the Go map). `CrewHost` stands down while it's up.
        .sheet(isPresented: Binding(
            get: { crew.isFlowPresented },
            set: { crew.isFlowPresented = $0 }
        )) {
            CrewFlowView(crew: crew, webBaseURL: location.webBaseURL)
                .presentationDetents([.large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
        }
        .sheet(isPresented: $showParticipants) {
            MapPeopleSheet(
                title: "In this session",
                people: sessionPeople,
                me: location.runnerName,
                followedId: map.followedId,
                status: { person in
                    guard let pin = pins.first(where: { $0.id == person.id }) else { return nil }
                    return pin.timestamp.map { Date().timeIntervalSince($0) } ?? 0
                },
                onSelect: { person in
                    showParticipants = false
                    map.follow(person.id)
                },
                onBlock: { person in
                    showParticipants = false
                    blockCandidate = person.id
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

    /// Opens the crew sheet, where "Find new crew" lives. The session map has no crew chip (bottom left is the
    /// runners), so this is its way in.
    @ViewBuilder
    private var crewButton: some View {
        if crew.isAvailable {
            MapRailButton(symbol: "person.2", label: "Crew") { crew.isFlowPresented = true }
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

    /// Everyone in the session, you first, as the shared dot chip: tap to follow, hold to block.
    private var sessionPeople: [MapPerson] { sessionNames.map { MapPerson(id: $0) } }

    private var participantsRow: some View {
        MapPeopleChip(
            people: sessionPeople,
            me: location.runnerName,
            pins: pins,
            controller: map,
            countLabel: "\(sessionPeople.count) people in this session",
            onHold: { blockCandidate = $0.id },
            onShowAll: { showParticipants = true }
        )
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
                // Never over a Go run: with groups the start screen can show while out, and a group isn't a session map.
                get: {
                    location.activeMembership != nil && (location.isTracking || showShareConsent) && !casualMapPresented
                        && !location.isCasualTracking
                },
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
