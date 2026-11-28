import CoreLocation
import SwiftUI

// Casual tracking screens: the Go button on the start screen, "Before you go", "Who can see me", and
// the casual map. State and calls live in `CasualStore`. Laid out like the Android app's casual
// screens, which follow the "Casual Go Button" design. See .ai/plans/casual-tracking.md.

private let goGreen = Color(red: 0x34 / 255, green: 0xC7 / 255, blue: 0x59 / 255)
let stopRed = Color(red: 1, green: 0x3B / 255, blue: 0x30 / 255)
let stopRedFill = Color(red: 0xB3 / 255, green: 0x26 / 255, blue: 0x1E / 255)

/// How long Go must be held. Long enough to ignore a press in a pocket, short enough that
/// nobody has to try twice.
private let holdDuration: TimeInterval = 0.9

private func durationLabel(_ duration: TimeInterval) -> String { "\(Int(duration / 3600))h" }

// MARK: - Press and hold

/// The press-and-hold for Go: `progress` fills over `holdDuration` while the finger is
/// down, then `onComplete` runs with a firm tap. Letting go early drains it and does nothing. VoiceOver
/// users get a plain activation instead of a held press.
private struct HoldToComplete: ViewModifier {
    @Binding var progress: CGFloat
    let onComplete: () -> Void
    @State private var holdTask: Task<Void, Never>?
    // True only while a finger is down. Unlike onEnded, it also goes back to false when the system
    // cancels the touch (a scroll taking over, a sheet appearing under the finger), so a hold can't
    // carry on, or stay stuck, after the finger has gone.
    @GestureState private var isPressed = false

    func body(content: Content) -> some View {
        content
            .gesture(
                DragGesture(minimumDistance: 0)
                    .updating($isPressed) { _, state, _ in state = true }
                    .onChanged { _ in begin() }
                    .onEnded { _ in cancel() }
            )
            .onChange(of: isPressed) { _, pressed in
                if !pressed { cancel() }
            }
            .accessibilityAddTraits(.isButton)
            .accessibilityAction { onComplete() }
    }

    private func begin() {
        guard holdTask == nil else { return }
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        withAnimation(.linear(duration: holdDuration)) { progress = 1 }
        holdTask = Task {
            try? await Task.sleep(for: .seconds(holdDuration))
            guard !Task.isCancelled else { return }
            UIImpactFeedbackGenerator(style: .rigid).impactOccurred()
            onComplete()
            progress = 0
            holdTask = nil
        }
    }

    private func cancel() {
        guard let task = holdTask else { return }
        task.cancel()
        holdTask = nil
        withAnimation(.easeOut(duration: 0.15)) { progress = 0 }
    }
}

extension View {
    func holdToComplete(progress: Binding<CGFloat>, onComplete: @escaping () -> Void) -> some View {
        modifier(HoldToComplete(progress: progress, onComplete: onComplete))
    }
}

// MARK: - Sheets

/// A close button in the corner of a sheet. A phone dismisses a sheet by dragging it down; a Mac window has
/// no such gesture, so without this the sheet could not be closed there.
private struct SheetCloseButton: ViewModifier {
    @Environment(\.dismiss) private var dismiss

    func body(content: Content) -> some View {
        content.overlay(alignment: .topTrailing) {
            Button { dismiss() } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.app(size: 28))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .padding(16)
            .accessibilityLabel("Close")
        }
    }
}

extension View {
    func sheetCloseButton() -> some View { modifier(SheetCloseButton()) }
}

// MARK: - Start screen

/// The line under the crew row: who is out, or that nobody is. nil while still loading.
func crewOutLine(_ crewOut: Int?) -> String? {
    switch crewOut {
    case nil: return nil
    case 0: return "No crew out right now. They'll show up here."
    case 1: return "1 of your crew is out now"
    default: return "\(crewOut ?? 0) of your crew are out now"
    }
}

// MARK: - Before you go

/// Shown every time Go is held, before anything starts. Holds the "Who can see me" switches, so they can be
/// changed without leaving this sheet, and asks how long to share for (sharing always stops on its own, the
/// same choices as the session consent screen).
struct BeforeYouGoSheet: View {
    var casual: CasualStore
    let me: String
    let crew: [CrewMember]
    /// The groups the person is in (only with the "groups" feature): shown as the yellow circle and a line each.
    var groups: [SessionMembership] = []
    @State private var duration = CasualStore.defaultDuration
    let onGo: (TimeInterval) -> Void

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Before you go")
                    .font(.app(.largeTitle, weight: .bold))

                VStack(alignment: .leading, spacing: 14) {
                    sectionHeader("WHO CAN SEE ME")
                    WhoCanSeeMeControls(casual: casual, me: me, crew: crew, groups: groups)
                }

                VStack(alignment: .leading, spacing: 6) {
                    sectionHeader("SHARE FOR")
                    Picker("Share for", selection: $duration) {
                        ForEach(LocationManager.trackingDurationOptions, id: \.self) { option in
                            Text(durationLabel(option)).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    Text("Sharing always stops on its own. This picks how soon. Hold the red Stop to end it sooner.")
                        .font(.app(.caption))
                        .foregroundStyle(.secondary)
                }

                Button {
                    onGo(duration)
                } label: {
                    Text("Go")
                        .font(.app(.headline))
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(goGreen)
                .controlSize(.large)
            }
            .padding(24)
        }
        .sheetCloseButton()
    }

    private func sectionHeader(_ text: String) -> some View {
        Text(text)
            .font(.app(.caption, weight: .semibold))
            .foregroundStyle(.secondary)
    }
}

// MARK: - Who can see me

/// Who can see a casual run. Crew can, unless the person turns "My crew" off, which also stops them seeing
/// their crew. The nearby switch controls
/// strangers: off until the person turns it on, then remembered. Only people who have also turned it on
/// can be seen, and they see each other, so turning it off also stops showing them others. With both off
/// the person is private: nobody sees them on the map, but a "Share your run" link still works.
struct WhoCanSeeMeSheet: View {
    var casual: CasualStore
    let me: String
    let crew: [CrewMember]
    var groups: [SessionMembership] = []

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("Who can see me")
                    .font(.app(.largeTitle, weight: .bold))
                WhoCanSeeMeControls(casual: casual, me: me, crew: crew, groups: groups)
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .sheetCloseButton()
    }
}

/// The "Who can see me" switches, shared by that sheet (from the map) and "Before you go". A small picture
/// of who sees you sits above them and follows each switch. Each switch saves as soon as it changes.
///
/// In a group (mock-up "Before you go · in a group" on the "Group button on the start screen" canvas) the
/// picture puts this person inside a yellow circle with the group, a key explains it, and each group gets a
/// line under "My crew": the group sees them by name whatever the switches say, until it ends or they leave.
/// "Hide my start area" isn't shown then (groups start at a meeting point); the saved setting still applies.
struct WhoCanSeeMeControls: View {
    var casual: CasualStore
    /// This person's runner name, for the initials on their own dot.
    let me: String
    let crew: [CrewMember]
    var groups: [SessionMembership] = []
    var now = Date()

    private var inGroup: Bool { !groups.isEmpty }

    /// "who aren't in your crew" is underlined: it is the group the setting is about.
    private static let hideStartAreaCaption: AttributedString = {
        var text = AttributedString("People who aren't in your crew don't see you until you are 300 m from where you started.")
        if let range = text.range(of: "who aren't in your crew") {
            text[range].underlineStyle = .single
        }
        return text
    }()

    /// Only while nearby is on does "Hide my start area" matter, so only then can the start be exposed.
    private var startExposed: Bool { casual.visibleToNonCrew && !casual.hideStartArea }

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            WhoSeesYouStrip(
                me: me,
                crew: crew,
                crewVisible: casual.visibleToCrew,
                nearbyVisible: casual.visibleToNonCrew,
                startHidden: !inGroup && casual.visibleToNonCrew && casual.hideStartArea,
                inGroup: inGroup
            )
            if inGroup {
                WhoSeesYouKey(
                    groupName: groups.count == 1 ? groups[0].sessionName : "your groups",
                    // The first crew dot's colour, as drawn in the picture.
                    crewColor: crew.first.map { RunnerColorPalette.color(for: $0.nickname ?? $0.displayName) } ?? .orange
                )
            }
            Toggle(isOn: Binding(
                get: { casual.visibleToCrew },
                set: { visible in Task { await casual.setVisibleToCrew(visible) } }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("My crew").font(.app(.headline))
                    if inGroup {
                        Text("By name, anywhere").font(.app(.subheadline)).foregroundStyle(.secondary)
                    }
                }
            }
            ForEach(groups) { group in
                GroupSeesYouLine(group: group, now: now)
            }
            Toggle(isOn: Binding(
                get: { casual.visibleToNonCrew },
                set: { visible in Task { await casual.setVisibleToNonCrew(visible) } }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Show me to people nearby").font(.app(.headline))
                    if inGroup {
                        Text("Anyone else within 5 km, as a dot with no name")
                            .font(.app(.subheadline)).foregroundStyle(.secondary)
                    }
                }
            }
            if !inGroup {
                Toggle(isOn: Binding(
                    get: { casual.hideStartArea },
                    set: { hide in Task { await casual.setHideStartArea(hide) } }
                )) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Hide my start area").font(.app(.headline))
                        if startExposed {
                            Label {
                                Text("Anyone nearby can see where you set off from, which may be your home.")
                            } icon: {
                                Image(systemName: "exclamationmark.circle.fill")
                            }
                            .font(.app(.subheadline))
                            .foregroundStyle(.orange)
                        } else {
                            Text(Self.hideStartAreaCaption)
                                .font(.app(.subheadline)).foregroundStyle(.secondary)
                        }
                    }
                }
                .disabled(!casual.visibleToNonCrew)
            }
            // In a group they're never private: the group always sees them.
            if casual.isPrivate, !inGroup {
                Label {
                    Text("You're private: nobody sees you on the map, and you don't see your crew. You can still send someone a link to watch you. On the map, tap \(Image(systemName: "square.and.arrow.up")) Share.")
                } icon: {
                    Image(systemName: "eye.slash")
                }
                .font(.app(.footnote))
                .padding(12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(Color.secondary.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
            }
            if let error = casual.settingsError {
                Text(error).font(.app(.footnote)).foregroundStyle(.red)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// A small map strip above the "Who can see me" switches: this person at their start, up to three of their
/// crew along a route, and two anonymous nearby dots. Turning a switch off fades its dots away, which shows
/// the two-way part (with "My crew" off you don't see them either). With the start hidden, a dashed ring
/// circles the start and this person's dot is half see-through. Decorative: the switches carry the meaning.
///
/// With `inGroup`, a yellow circle around this person and two stand-in members shows the group, which sees
/// them by name whatever the switches say; the nearby dots move outside it.
struct WhoSeesYouStrip: View {
    let me: String
    let crew: [CrewMember]
    let crewVisible: Bool
    let nearbyVisible: Bool
    let startHidden: Bool
    var inGroup = false

    /// Drawn on a 300 × 84 grid, scaled to the width it is given.
    private static let grid = CGSize(width: 300, height: 84)
    private static let start = CGPoint(x: 36, y: 66)
    private static let crewSpots = [CGPoint(x: 132, y: 46), CGPoint(x: 164, y: 40), CGPoint(x: 262, y: 20)]
    private static let nearbySpots = [CGPoint(x: 94, y: 24), CGPoint(x: 212, y: 64)]
    /// In a group: the circle around the start, two members in it, and the nearby dots clear of it.
    private static let groupCenter = CGPoint(x: 58, y: 50)
    private static let groupRadius: CGFloat = 38
    private static let groupMemberSpots = [CGPoint(x: 66, y: 30), CGPoint(x: 80, y: 56)]
    private static let groupMemberColors = [Color(red: 0x37 / 255, green: 0x47 / 255, blue: 0x4F / 255),
                                            Color(red: 0x00 / 255, green: 0x79 / 255, blue: 0x6B / 255)]
    private static let groupNearbySpots = [CGPoint(x: 200, y: 18), CGPoint(x: 212, y: 64)]
    /// The join-area yellow (as on the organiser's map), for the group circle and its key.
    static let groupFill = Color(red: 0xFA / 255, green: 0xCC / 255, blue: 0x15 / 255).opacity(0.35)
    static let groupStroke = Color(red: 0xCA / 255, green: 0x8A / 255, blue: 0x04 / 255)
    /// Stand-ins when the person has no crew yet, drawn without initials.
    private static let placeholderCrewColors: [Color] = [.orange, .green, .purple]
    private static let nearbyGrey = Color(red: 0x9A / 255, green: 0xA3 / 255, blue: 0xAB / 255)

    var body: some View {
        GeometryReader { geometry in
            let scale = geometry.size.width / Self.grid.width
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 12).fill(Color.green.opacity(0.14))
                Path { path in
                    path.move(to: point(16, 70, scale))
                    path.addCurve(to: point(150, 44, scale), control1: point(70, 64, scale), control2: point(100, 48, scale))
                    path.addCurve(to: point(286, 14, scale), control1: point(200, 40, scale), control2: point(240, 24, scale))
                }
                .stroke(Color(.systemGray3), style: StrokeStyle(lineWidth: 5 * scale, lineCap: .round))

                if inGroup {
                    Circle()
                        .fill(Self.groupFill)
                        .overlay(Circle().stroke(Self.groupStroke, lineWidth: 1.5))
                        .frame(width: Self.groupRadius * 2 * scale, height: Self.groupRadius * 2 * scale)
                        .position(point(Self.groupCenter.x, Self.groupCenter.y, scale))
                    ForEach(Self.groupMemberSpots.indices, id: \.self) { index in
                        dot(color: Self.groupMemberColors[index], label: nil, scale: scale)
                            .position(point(Self.groupMemberSpots[index].x, Self.groupMemberSpots[index].y, scale))
                    }
                }

                Circle()
                    .stroke(Color.secondary, style: StrokeStyle(lineWidth: 1.5, dash: [3, 3]))
                    .frame(width: 48 * scale, height: 48 * scale)
                    .position(point(Self.start.x, Self.start.y, scale))
                    .opacity(startHidden ? 1 : 0)
                    .scaleEffect(startHidden ? 1 : 0.4, anchor: anchor(Self.start))

                let nearbySpots = inGroup ? Self.groupNearbySpots : Self.nearbySpots
                ForEach(nearbySpots.indices, id: \.self) { index in
                    dot(color: Self.nearbyGrey, label: "?", scale: scale)
                        .position(point(nearbySpots[index].x, nearbySpots[index].y, scale))
                        .opacity(nearbyVisible ? 1 : 0)
                        .scaleEffect(nearbyVisible ? 1 : 0.4, anchor: anchor(nearbySpots[index]))
                }

                ForEach(Self.crewSpots.indices, id: \.self) { index in
                    crewDot(index, scale: scale)
                        .position(point(Self.crewSpots[index].x, Self.crewSpots[index].y, scale))
                        .opacity(crewVisible ? 1 : 0)
                        .scaleEffect(crewVisible ? 1 : 0.4, anchor: anchor(Self.crewSpots[index]))
                }

                dot(color: RunnerColorPalette.currentUser, label: RunnerCircle.initials(for: me), scale: scale)
                    .position(point(Self.start.x, Self.start.y, scale))
                    .opacity(startHidden ? 0.5 : 1)
            }
        }
        .aspectRatio(Self.grid.width / Self.grid.height, contentMode: .fit)
        .animation(.easeOut(duration: 0.25), value: crewVisible)
        .animation(.easeOut(duration: 0.25), value: nearbyVisible)
        .animation(.easeOut(duration: 0.25), value: startHidden)
        .accessibilityHidden(true)
    }

    /// A crew dot coloured and labelled as on the map: colour from the name the map uses (nickname, else
    /// display name), letters from the display name.
    @ViewBuilder
    private func crewDot(_ index: Int, scale: CGFloat) -> some View {
        if index < crew.count {
            let member = crew[index]
            dot(color: RunnerColorPalette.color(for: member.nickname ?? member.displayName),
                label: RunnerCircle.initials(for: member.displayName), scale: scale)
        } else if crew.isEmpty {
            dot(color: Self.placeholderCrewColors[index], label: nil, scale: scale)
        }
    }

    private func dot(color: Color, label: String?, scale: CGFloat) -> some View {
        Circle()
            .fill(color)
            .overlay(Circle().stroke(Color.white, lineWidth: 2))
            .overlay {
                if let label {
                    Text(label)
                        .font(.app(size: (label == "?" ? 11 : 9) * scale, weight: .bold))
                        .foregroundStyle(.white)
                }
            }
            .frame(width: 22 * scale, height: 22 * scale)
    }

    private func point(_ x: CGFloat, _ y: CGFloat, _ scale: CGFloat) -> CGPoint {
        CGPoint(x: x * scale, y: y * scale)
    }

    private func anchor(_ spot: CGPoint) -> UnitPoint {
        UnitPoint(x: spot.x / Self.grid.width, y: spot.y / Self.grid.height)
    }
}

/// Under the picture while in a group: what the circle, the crew colour and the grey dots mean.
struct WhoSeesYouKey: View {
    let groupName: String
    let crewColor: Color

    var body: some View {
        // Wraps onto two lines on a narrow phone or with large text.
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 14) { items }
            VStack(alignment: .leading, spacing: 4) { items }
        }
        .font(.app(.footnote))
        .foregroundStyle(.secondary)
    }

    @ViewBuilder
    private var items: some View {
        item(Circle().fill(WhoSeesYouStrip.groupFill).overlay(Circle().stroke(WhoSeesYouStrip.groupStroke, lineWidth: 1.5)),
             "In \(groupName), by name")
        item(Circle().fill(crewColor), "Crew, by name")
        item(Circle().fill(Color(.systemGray)), "Others, no name")
    }

    private func item(_ swatch: some View, _ text: String) -> some View {
        HStack(spacing: 6) {
            swatch.frame(width: 11, height: 11)
            Text(text).lineLimit(1)
        }
    }
}

/// One group under "My crew": everyone in it sees this person by name until it ends. Not a switch: joining was
/// the consent, and leaving (from the group sign) is the way out.
struct GroupSeesYouLine: View {
    let group: SessionMembership
    let now: Date

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "person.2")
                .font(.app(.headline))
                .frame(width: 26)
            VStack(alignment: .leading, spacing: 2) {
                Text(group.sessionName).font(.app(.headline))
                Text(summary)
                    .font(.app(.subheadline))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(12)
        .background(WhoSeesYouStrip.groupFill.opacity(0.6), in: RoundedRectangle(cornerRadius: 12))
        .accessibilityElement(children: .combine)
    }

    private var summary: String {
        let until = groupEndWords(endsAtISO: group.endsAt, now: now).map { "until it ends, \($0)" } ?? "until it ends"
        return "Everyone in it sees you by name \(until). Leave it from the group sign."
    }
}

// MARK: - Hosting

/// Presents the casual sheets and map once, at the top of `ContentView`, and keeps the store in step
/// with sign-in and the app coming to the front (as `CrewHost` does for crew).
struct CasualHost: ViewModifier {
    @Bindable var casual: CasualStore
    var crew: CrewStore
    var location: LocationManager
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: $casual.isBeforeYouGoPresented) {
                BeforeYouGoSheet(casual: casual, me: location.runnerName, crew: crew.members,
                                 groups: location.groupMemberships) { duration in
                    casual.isBeforeYouGoPresented = false
                    Task { await casual.start(duration: duration) }
                }
                .presentationDetents([.large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .fullScreenCover(isPresented: $casual.isMapPresented) {
                CasualMapScreen(casual: casual, crew: crew, location: location)
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .task {
                casual.attach(location)
                await casual.refresh()
            }
            .onChange(of: location.isAuthenticated) { _, isAuthenticated in
                if isAuthenticated {
                    Task { await casual.refresh() }
                } else {
                    casual.clear()
                }
            }
            .onChange(of: scenePhase) { _, phase in
                if phase == .active { Task { await casual.refresh() } }
            }
            // Tracking ended (the time ran out, or a battery stop): the map has nothing left to show.
            .onChange(of: location.isTracking) { _, isTracking in
                if !isTracking { casual.isMapPresented = false }
            }
            // A tap on a crew-out alert ("DW: Mia has joined") opens the Go map, if the Go run is still going.
            .onReceive(NotificationCenter.default.publisher(for: .crewOutAlertTapped)) { _ in
                if location.isCasualTracking { casual.isMapPresented = true }
            }
    }
}

// MARK: - Map

/// The map while out casually: `TrackingMapScreen` fed with this person, their crew by name, and (only
/// when both have chosen to be seen) anonymous dots within 5 km. There is no session behind it, so no
/// invite; instead the rail has a watch link, an eye that opens "Who can see me" and the crew button. The
/// bottom-left chip is the dots of this person and their crew who are out, as on the session map: tap one to
/// follow them. Stopping is a hold.
struct CasualMapScreen: View {
    var casual: CasualStore
    @Bindable var crew: CrewStore
    var location: LocationManager

    @Environment(\.scenePhase) private var scenePhase
    @State private var map = TrackingMapController()
    @State private var showWhoCanSeeMe = false
    @State private var showShare = false
    @State private var showPeople = false
    /// Groups (see .ai/plans/go-and-groups.md): the "Your groups" sheet, and the clock for the chip's time left.
    @State private var showGroups = false
    @State private var now = Date()

    private static let pollInterval: Duration = .seconds(10)

    private var pins: [MapPin] {
        var result: [MapPin] = casual.people
            .filter { $0.isSelf != true }
            .map {
                MapPin(
                    id: $0.runnerName,
                    displayName: $0.displayName,
                    coordinate: CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude),
                    heading: $0.heading,
                    isLocked: $0.inSession == true,
                    timestamp: positionTimestampParser.date(from: $0.timestamp)
                )
            }
        // This device's own position straight from CoreLocation, so the dot is current without waiting
        // for the server, as in the session map.
        let me = casual.people.first { $0.isSelf == true }
        if let coordinate = location.currentCoordinate
            ?? me.map({ CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude) }) {
            result.append(MapPin(
                id: location.runnerName, coordinate: coordinate,
                heading: location.currentHeading ?? me?.heading, isSelf: true))
        }
        return result
    }

    /// Anonymous: an index for an id, nothing that says who it is.
    private var nearby: [MapPin] {
        casual.nearby.enumerated().map { index, dot in
            MapPin(
                id: "\(index)",
                coordinate: CLLocationCoordinate2D(latitude: dot.latitude, longitude: dot.longitude),
                isLocked: dot.inSession == true)
        }
    }

    var body: some View {
        TrackingMapScreen(
            location: location,
            controller: map,
            pins: pins,
            nearby: nearby,
            routes: casual.routeLines,
            joinRange: ownedGroupJoinRange(for: location),
            otherJoinRanges: casual.groupJoinRanges,
            route: routeActions,
            // An organiser can drag the map and set their group's join location at the crosshair.
            crosshairActions: MapCrosshairAction.joinLocations(for: location),
            onStop: { casual.stop() },
            railTop: {
                MapRailButton(symbol: "square.and.arrow.up", label: "Share a link to watch me") {
                    showShare = true
                    Task { await casual.makeWatchLink() }
                }
            },
            railMiddle: {
                MapRailButton(symbol: "eye", label: "Who can see me") { showWhoCanSeeMe = true }
                // The crew sheet, where crew can be added mid-run (the chip used to open it).
                if crew.isAvailable {
                    MapRailButton(symbol: "person.2", label: "Crew") { crew.isFlowPresented = true }
                }
            },
            bottomLeading: { peopleChip }
        )
        .task {
            // Only while the app is visible: the tracking loop keeps sending this device's position
            // regardless, and nobody is looking at the map with the screen locked. Skipped on satellite,
            // where the map is frozen and every request costs.
            while !Task.isCancelled {
                if scenePhase == .active, !location.isUltraConstrained { await casual.loadLive() }
                try? await Task.sleep(for: Self.pollInterval)
            }
        }
        .sheet(isPresented: $showWhoCanSeeMe) {
            WhoCanSeeMeSheet(casual: casual, me: location.runnerName, crew: crew.members,
                             groups: location.groupMemberships)
                .presentationDetents([.medium, .large])
        }
        .sheet(isPresented: $showShare) {
            ShareRunSheet(casual: casual)
                .presentationDetents([.large])
        }
        .sheet(isPresented: $showPeople) {
            MapPeopleSheet(
                title: "Out now",
                people: people,
                me: location.runnerName,
                followedId: map.followedId,
                status: { person in
                    guard let pin = pins.first(where: { $0.id == person.id }) else { return nil }
                    return pin.timestamp.map { Date().timeIntervalSince($0) } ?? 0
                },
                onSelect: { person in
                    showPeople = false
                    map.follow(person.id)
                }
            )
            .presentationDetents([.medium, .large])
        }
        // The crew sheet lives here, not on `ContentView`, because the map is a full-screen cover. A
        // crew link opened during a run lands here too (`CrewStore` sets the same flag).
        .sheet(isPresented: $crew.isFlowPresented) {
            CrewFlowView(crew: crew, webBaseURL: location.webBaseURL)
                .presentationDetents([.large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
        }
        .modifier(GoMapGroups(location: location, casual: casual, showGroups: $showGroups, now: $now))
    }

    /// This person's own route, shown to their crew and to anyone with their watch link. The store
    /// reports failures in `routeError`; they're handed to the shell's alert instead.
    private var routeActions: RouteActions {
        RouteActions(
            hasRoute: casual.hasOwnRoute,
            isUploading: casual.isUploadingRoute,
            removeMessage: "This removes your route for you, your crew and anyone watching your link.",
            upload: { data in
                guard await casual.setRoute(gpx: data) else { throw takeRouteError() }
            },
            remove: {
                await casual.removeRoute()
                if casual.routeError != nil { throw takeRouteError() }
            }
        )
    }

    private func takeRouteError() -> MapActionError {
        let message = casual.routeError ?? "Couldn't update the route."
        casual.routeError = nil
        return MapActionError(message: message)
    }

    /// You first, then your crew who are out: named as on their pins, lettered from their initials. Anonymous nearby dots are
    /// never in it: they have no name.
    private var people: [MapPerson] {
        [MapPerson(id: location.runnerName)] + casual.people
            .filter { $0.isSelf != true }
            .map { MapPerson(id: $0.runnerName, dotLabel: $0.displayName) }
    }

    /// The same dot chip as the session map, without its block.
    private var peopleChip: some View {
        MapPeopleChip(
            people: people,
            me: location.runnerName,
            pins: pins,
            controller: map,
            countLabel: "You and " + (people.count == 2 ? "1 crew member" : "\(people.count - 1) crew") + " out",
            onShowAll: { showPeople = true }
        )
    }
}

// MARK: - Map pieces

/// "Share your run": a secret link to a web map of just this person, for someone with no app or account.
/// It can be sent through the share sheet, opened in the browser, or copied. The link stops working when
/// the run ends, and "Turn off links" ends it sooner.
private struct ShareRunSheet: View {
    var casual: CasualStore
    @Environment(\.openURL) private var openURL
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Share your run")
                    .font(.app(.largeTitle, weight: .bold))
                Text("Send this link to someone who isn't in the app. They watch your position on a web map. No app and no account.")
                    .font(.app(.subheadline))
                    .foregroundStyle(.secondary)

                if let url = casual.watchLink {
                    Text(url.absoluteString)
                        .font(.system(.subheadline, design: .monospaced))
                        .textSelection(.enabled)
                    ShareLink(item: url, message: Text("Watch my run on Dot Watcher")) {
                        Text("Share link")
                            .font(.app(.headline))
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(goGreen)
                    .controlSize(.large)
                    Button { openURL(url) } label: {
                        Text("Open in browser").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.large)
                    Button { UIPasteboard.general.string = url.absoluteString } label: {
                        Text("Copy link").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.large)
                } else if let error = casual.watchLinkError {
                    Text(error).font(.app(.subheadline, weight: .semibold)).foregroundStyle(.red)
                    Button { Task { await casual.makeWatchLink() } } label: {
                        Text("Try again").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.large)
                } else {
                    ProgressView().frame(maxWidth: .infinity)
                }

                point("Works until you stop", "The link goes dead when your run ends. A new run gets a new link.")
                point("Shows only you", "Not your crew, and not anyone nearby.")

                if casual.watchLink != nil {
                    Button(role: .destructive) {
                        Task {
                            await casual.turnOffWatchLinks()
                            dismiss()
                        }
                    } label: {
                        Text("Turn off links").frame(maxWidth: .infinity)
                    }
                }
            }
            .padding(24)
        }
        .sheetCloseButton()
    }

    private func point(_ title: String, _ body: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.app(.headline))
            Text(body).font(.app(.subheadline)).foregroundStyle(.secondary)
        }
    }
}

/// Opened by tapping the countdown: add time to the current auto-stop, or stop now.
struct ExtendSheet: View {
    let onExtend: (TimeInterval) -> Void
    let onStopNow: () -> Void
    var location: LocationManager

    private static let options: [(String, TimeInterval)] = [
        ("+30 min", 30 * 60), ("+1 hr", 60 * 60), ("+2 hr", 2 * 60 * 60),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                let remaining = max(0, (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date))
                Text(remaining >= 3600
                     ? "Auto-stops in \(Int(remaining / 3600)) hr \(Int(remaining / 60) % 60) min"
                     : "Auto-stops in \(Int((remaining + 59) / 60)) min")
                    .font(.app(.headline))
            }
            HStack(spacing: 8) {
                ForEach(Self.options, id: \.0) { option in
                    Button { onExtend(option.1) } label: {
                        Text(option.0).frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .controlSize(.large)
                }
            }
            Button(action: onStopNow) {
                Text("Stop now").font(.app(.body, weight: .bold)).frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(stopRed)
            .controlSize(.large)
        }
        .padding(24)
        .sheetCloseButton()
    }
}

/// What the Go map adds for groups: "‹ Home" (to the start screen with tracking carrying on, to join a group
/// while out), the group chip under the timer, and the "Your groups" sheet. A modifier of its own so the map's
/// long chain of sheets stays within the type-checker's budget. Nothing shows without the "groups" feature.
private struct GoMapGroups: ViewModifier {
    var location: LocationManager
    var casual: CasualStore
    @Binding var showGroups: Bool
    @Binding var now: Date

    func body(content: Content) -> some View {
        if location.hasGroups {
            content
                .overlay(alignment: .topLeading) {
                    GoMapGroupBar(
                        groups: location.groupMemberships,
                        now: now,
                        onHome: { casual.isMapPresented = false },
                        onOpenGroups: { showGroups = true }
                    )
                    // Below the timer pill, in the map's top-left corner.
                    .padding(.leading, 14)
                    .padding(.top, 64)
                }
                .sheet(isPresented: $showGroups) {
                    GroupsOnMapSheet(location: location, now: now)
                        .presentationDetents([.medium, .large])
                        .preferredColorScheme(location.appearanceMode.colorScheme)
                }
                .task {
                    // The list changes rarely (join, leave, end): fetch it as the map opens, then tick the clock.
                    await location.loadSessions()
                    while !Task.isCancelled {
                        now = Date()
                        try? await Task.sleep(for: .seconds(60))
                    }
                }
        } else {
            content
        }
    }
}
