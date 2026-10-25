import MapKit
import SwiftUI

// Casual tracking screens: the Go button on the start screen, "Before you go", "Who can see me", and
// the casual map. State and calls live in `CasualStore`. Laid out like the Android app's casual
// screens, which follow the "Casual Go Button" design. See .ai/plans/casual-tracking.md.

private let goGreen = Color(red: 0x34 / 255, green: 0xC7 / 255, blue: 0x59 / 255)
private let stopRed = Color(red: 1, green: 0x3B / 255, blue: 0x30 / 255)
private let stopRedFill = Color(red: 0xB3 / 255, green: 0x26 / 255, blue: 0x1E / 255)

/// How long Go and Stop must be held. Long enough to ignore a press in a pocket, short enough that
/// nobody has to try twice.
private let holdDuration: TimeInterval = 0.9

private func durationLabel(_ duration: TimeInterval) -> String { "\(Int(duration / 3600))h" }

// MARK: - Press and hold

/// The same press-and-hold for Go and Stop: `progress` fills over `holdDuration` while the finger is
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

private extension View {
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
                    .font(.system(size: 28))
                    .symbolRenderingMode(.hierarchical)
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .padding(16)
            .accessibilityLabel("Close")
        }
    }
}

private extension View {
    func sheetCloseButton() -> some View { modifier(SheetCloseButton()) }
}

// MARK: - Start screen

/// The big hold-to-go button with its caption, shown on the start screen when the server has casual for
/// this account. Holding Go opens "Before you go", which is where tracking actually starts.
struct CasualGoBlock: View {
    var casual: CasualStore
    @State private var progress: CGFloat = 0

    var body: some View {
        if casual.isAvailable {
            VStack(spacing: 10) {
                if let line = Self.crewOutLine(casual.crewOut) {
                    Text(line)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
                ZStack {
                    Circle().stroke(Color(.systemGray5), lineWidth: 10)
                    Circle()
                        .trim(from: 0, to: progress)
                        .stroke(goGreen, style: StrokeStyle(lineWidth: 10, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                    Circle().fill(goGreen).padding(17)
                    Text("Go")
                        .font(.system(size: 60, weight: .heavy))
                        .foregroundStyle(.white)
                }
                .frame(width: 224, height: 224)
                .padding(.vertical, 4)
                .contentShape(Circle())
                .holdToComplete(progress: $progress) { casual.isBeforeYouGoPresented = true }
                .accessibilityLabel("Go")
                .accessibilityHint("Hold to start tracking")
                Text("HOLD TO GO")
                    .font(.footnote.weight(.bold))
                    .foregroundStyle(.secondary)
                Text("Out for a run? Hold Go. Your crew will see you.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                if let error = casual.startError {
                    Text(error)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.center)
                }
            }
            .frame(maxWidth: .infinity)
        }
    }

    /// The line under the crew row: who is out, or that nobody is. nil while still loading.
    static func crewOutLine(_ crewOut: Int?) -> String? {
        switch crewOut {
        case nil: return nil
        case 0: return "No crew out right now. They'll show up here."
        case 1: return "1 of your crew is out now"
        default: return "\(crewOut ?? 0) of your crew are out now"
        }
    }
}

// MARK: - Before you go

/// Shown every time Go is held, before anything starts. Says who will see the person, asks how long to
/// share for (sharing always stops on its own, the same choices as the session consent screen), and
/// offers a way to change who can see them.
struct BeforeYouGoSheet: View {
    @State private var duration = CasualStore.defaultDuration
    let onGo: (TimeInterval) -> Void
    let onChooseWhoCanSeeMe: () -> Void

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Before you go")
                    .font(.largeTitle.bold())
                point("Your crew see your name", "While you're out, with their colour ring on the map.")
                point("Nearby runners see nothing",
                      "Unless you turn on Show me to people nearby. Then it's a grey dot with no name.")
                point("Stop any time", "Hold the red Stop, or it stops by itself when the time is up.")

                VStack(alignment: .leading, spacing: 6) {
                    Text("SHARE FOR")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                    Picker("Share for", selection: $duration) {
                        ForEach(LocationManager.trackingDurationOptions, id: \.self) { option in
                            Text(durationLabel(option)).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    Text("Sharing always stops on its own. This picks how soon.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Button {
                    onGo(duration)
                } label: {
                    Text("Go")
                        .font(.headline)
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(goGreen)
                .controlSize(.large)

                Button {
                    onChooseWhoCanSeeMe()
                } label: {
                    Text("Choose who can see me")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .controlSize(.large)
            }
            .padding(24)
        }
        .sheetCloseButton()
    }

    private func point(_ title: String, _ body: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.headline)
            Text(body).font(.subheadline).foregroundStyle(.secondary)
        }
    }
}

// MARK: - Who can see me

/// Who can see a casual run. Crew always can. The switch controls strangers nearby: off until the person
/// turns it on, then remembered. Only people who have also turned it on can be seen, and they see each
/// other, so turning it off also stops showing them others.
struct WhoCanSeeMeSheet: View {
    var casual: CasualStore

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Who can see me")
                .font(.largeTitle.bold())
            Toggle(isOn: .constant(true)) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("My crew").font(.headline)
                    Text("Always. They see your name.").font(.subheadline).foregroundStyle(.secondary)
                }
            }
            .disabled(true)
            Toggle(isOn: Binding(
                get: { casual.visibleToNonCrew },
                set: { visible in Task { await casual.setVisibleToNonCrew(visible) } }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Show me to people nearby").font(.headline)
                    Text("A grey dot with no name, within 5 km.").font(.subheadline).foregroundStyle(.secondary)
                }
            }
            Text("Only people who have also turned this on can be seen, and you see them. Your choice is remembered next time.")
                .font(.footnote)
                .foregroundStyle(.secondary)
            if let error = casual.settingsError {
                Text(error).font(.footnote).foregroundStyle(.red)
            }
            Spacer(minLength: 0)
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .sheetCloseButton()
    }
}

// MARK: - Hosting

/// Presents the casual sheets and map once, at the top of `ContentView`, and keeps the store in step
/// with sign-in and the app coming to the front (as `CrewHost` does for crew).
struct CasualHost: ViewModifier {
    @Bindable var casual: CasualStore
    var location: LocationManager
    @Environment(\.scenePhase) private var scenePhase

    func body(content: Content) -> some View {
        content
            .sheet(isPresented: $casual.isBeforeYouGoPresented) {
                BeforeYouGoSheet(
                    onGo: { duration in
                        casual.isBeforeYouGoPresented = false
                        Task { await casual.start(duration: duration) }
                    },
                    onChooseWhoCanSeeMe: {
                        casual.isBeforeYouGoPresented = false
                        // A second sheet can't open while the first is still closing.
                        Task {
                            try? await Task.sleep(for: .milliseconds(450))
                            casual.isWhoCanSeeMePresented = true
                        }
                    }
                )
                .presentationDetents([.large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .sheet(isPresented: $casual.isWhoCanSeeMePresented) {
                WhoCanSeeMeSheet(casual: casual)
                    .presentationDetents([.medium])
                    .preferredColorScheme(location.appearanceMode.colorScheme)
            }
            .fullScreenCover(isPresented: $casual.isMapPresented) {
                CasualMapScreen(casual: casual, location: location)
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
    }
}

// MARK: - Map

/// The map while out casually, laid out like the Android casual map: wordmark and auto-stop countdown
/// top-left, a right rail, a count chip and Stop at the bottom. There is no session behind it, so no
/// invite or web map buttons; instead an eye opens "Who can see me". Shows this person, their crew by
/// name, and (only when both have chosen to be seen) anonymous dots within 5 km. Stopping is a hold.
struct CasualMapScreen: View {
    var casual: CasualStore
    var location: LocationManager

    @Environment(\.scenePhase) private var scenePhase
    @State private var camera: MapCameraPosition = .automatic
    @State private var stopProgress: CGFloat = 0
    @State private var showExtend = false
    @State private var showWhoCanSeeMe = false
    @State private var showShare = false
    @State private var fittedCount = -1

    private static let pollInterval: Duration = .seconds(10)

    private struct Pin: Identifiable {
        let id: String
        let coordinate: CLLocationCoordinate2D
        let heading: Double?
        let isSelf: Bool
        let isLocked: Bool
        /// nil for this device's own live pin, which is never stale.
        let timestamp: Date?
    }

    private var pins: [Pin] {
        var result: [Pin] = casual.people
            .filter { $0.isSelf != true }
            .map {
                Pin(
                    id: $0.runnerName,
                    coordinate: CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude),
                    heading: $0.heading,
                    isSelf: false,
                    isLocked: $0.inSession == true,
                    timestamp: ISO8601DateFormatter().date(from: $0.timestamp)
                )
            }
        // This device's own position straight from CoreLocation, so the dot is current without waiting
        // for the server, as in the session map.
        let me = casual.people.first { $0.isSelf == true }
        if let coordinate = location.currentCoordinate
            ?? me.map({ CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude) }) {
            result.append(Pin(
                id: location.runnerName, coordinate: coordinate,
                heading: location.currentHeading ?? me?.heading,
                isSelf: true, isLocked: false, timestamp: nil))
        }
        return result
    }

    var body: some View {
        ZStack {
            map
            VStack {
                HStack(alignment: .top) {
                    topLeft
                    Spacer()
                    rail
                }
                .padding(.horizontal, 14)
                .padding(.top, 8)
                Spacer()
                if location.pausedForBattery {
                    batteryCard
                        .padding(.horizontal, 12)
                        .padding(.bottom, 16)
                } else {
                    HStack(alignment: .bottom) {
                        countChip
                        Spacer()
                        stopPill
                    }
                    .padding(.horizontal, 14)
                    .padding(.bottom, 26)
                }
            }
        }
        .task {
            // Only while the app is visible: the tracking loop keeps sending this device's position
            // regardless, and nobody is looking at the map with the screen locked.
            while !Task.isCancelled {
                if scenePhase == .active { await casual.loadLive() }
                try? await Task.sleep(for: Self.pollInterval)
            }
        }
        .sheet(isPresented: $showExtend) {
            ExtendSheet(
                onExtend: { location.extendTracking(by: $0) },
                onStopNow: {
                    showExtend = false
                    casual.stop()
                },
                location: location
            )
            .presentationDetents([.height(240)])
        }
        .sheet(isPresented: $showWhoCanSeeMe) {
            WhoCanSeeMeSheet(casual: casual)
                .presentationDetents([.medium])
        }
        .sheet(isPresented: $showShare) {
            ShareRunSheet(casual: casual)
                .presentationDetents([.large])
        }
    }

    // MARK: Map

    private var map: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            Map(position: $camera) {
                ForEach(Array(casual.nearby.enumerated()), id: \.offset) { _, dot in
                    Annotation("", coordinate: CLLocationCoordinate2D(latitude: dot.latitude, longitude: dot.longitude)) {
                        NearbyDotView(isLocked: dot.inSession == true)
                    }
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
                        .overlay(alignment: .bottomTrailing) {
                            if pin.isLocked { LockBadge().offset(x: 4, y: 4) }
                        }
                    }
                }
            }
        }
        .ignoresSafeArea()
        .onChange(of: pins.count + casual.nearby.count) { _, count in
            // Frame everyone whenever someone arrives or leaves; the fit-all button does it on demand.
            if count != fittedCount {
                fittedCount = count
                fitAll()
            }
        }
        .onAppear { fitAll() }
    }

    private func fitAll() {
        // Everyone on the map, anonymous dots included: they are why the person opened it.
        let coordinates = pins.map(\.coordinate)
            + casual.nearby.map { CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude) }
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

    private var topLeft: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                wordmark
                countdown
            }
            EndingSoonPill(location: location)
        }
    }

    private var wordmark: some View {
        HStack(spacing: 1) {
            Text("d").font(.system(size: 19, weight: .heavy))
            Text(RunnerCircle.initials(for: location.runnerName))
                .font(.system(size: 9.5, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 25, height: 25)
                .background(Circle().fill(RunnerColorPalette.currentUser))
            Text("t-watchr").font(.system(size: 19, weight: .heavy))
        }
        .shadow(color: .white, radius: 4)
        .foregroundStyle(.black)
    }

    private var countdown: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let remaining = max(0, (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date))
            let total = max(1, location.trackingTotalDuration)
            let low = remaining <= 30 * 60
            let tint = low ? Color(red: 0xE0 / 255, green: 0x8A / 255, blue: 0) : Color(red: 0x30 / 255, green: 0xA1 / 255, blue: 0x4E / 255)
            Button { showExtend = true } label: {
                ZStack {
                    Circle().stroke(Color.black.opacity(0.08), lineWidth: 3)
                    Circle()
                        .trim(from: 0, to: min(1, remaining / total))
                        .stroke(tint, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                    Text(Self.hoursMinutes(remaining))
                        .font(.system(size: 11, weight: .bold, design: .monospaced))
                        .foregroundStyle(low ? Color(red: 0xA3 / 255, green: 0x5F / 255, blue: 0) : .black)
                }
                .padding(2)
                .frame(width: 46, height: 46)
                .background(low ? Color(red: 1, green: 0.88, blue: 0.54) : Color.white.opacity(0.85), in: Circle())
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Auto-stops in \(Int(remaining / 60)) minutes")
            .accessibilityHint("Add time or stop now")
        }
    }

    static func hoursMinutes(_ seconds: TimeInterval) -> String {
        let total = Int(seconds)
        return "\(total / 3600):" + String(format: "%02d", (total % 3600) / 60)
    }

    private var rail: some View {
        VStack(spacing: 10) {
            railButton("square.and.arrow.up", label: "Share a link to watch me") {
                showShare = true
                Task { await casual.makeWatchLink() }
            }
            railButton("arrow.up.left.and.arrow.down.right", label: "Fit all runners") { fitAll() }
            railButton("eye", label: "Who can see me") { showWhoCanSeeMe = true }
        }
    }

    private func railButton(_ symbol: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(.black)
                .frame(width: 44, height: 44)
                .background(Color.white.opacity(0.85), in: Circle())
                .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(label)
    }

    /// "2 crew out · 5 nearby". The nearby part appears only when the person has chosen to be seen,
    /// because that is also when they can see others.
    private var countChip: some View {
        let crew = casual.crewOut ?? 0
        let text = (crew == 0 ? "No crew out" : "\(crew) crew out")
            + (casual.visibleToNonCrew ? "  ·  \(casual.nearby.count) nearby" : "")
        return Text(text)
            .font(.system(size: 12.5, weight: .semibold))
            .foregroundStyle(.black)
            .padding(.horizontal, 14)
            .frame(height: 36)
            .background(Color.white.opacity(0.85), in: Capsule())
            .shadow(color: .black.opacity(0.18), radius: 2, y: 1)
    }

    private var stopPill: some View {
        ZStack {
            Capsule().fill(stopRed)
            GeometryReader { geometry in
                Capsule()
                    .fill(stopRedFill)
                    .frame(width: geometry.size.width * stopProgress)
            }
            .clipShape(Capsule())
            Text("Hold to stop")
                .font(.system(size: 15, weight: .bold))
                .foregroundStyle(.white)
        }
        .frame(width: 150, height: 48)
        .shadow(color: .black.opacity(0.25), radius: 3, y: 2)
        .contentShape(Capsule())
        .holdToComplete(progress: $stopProgress) { casual.stop() }
        .accessibilityLabel("Stop")
        .accessibilityHint("Hold to stop tracking")
    }

    private var batteryCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label("Battery critical, updates paused", systemImage: "battery.0percent")
                .font(.system(size: 13.5, weight: .bold))
                .foregroundStyle(Color(red: 0xC4 / 255, green: 0x27 / 255, blue: 0x1D / 255))
            Text("Others can't see you move until you send manually, plug in, or turn automatic updates back on.")
                .font(.system(size: 11.5))
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                Button { Task { await location.sendLocationNowWhilePaused() } } label: {
                    Text("Send now").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                Button { location.resumeAutomaticTrackingOverridingBattery() } label: {
                    Text("Auto").frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
            }
            Button { casual.stop() } label: {
                Text("Stop").fontWeight(.bold).frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(stopRed)
        }
        .padding(14)
        .background(Color(red: 1, green: 0.94, blue: 0.93), in: RoundedRectangle(cornerRadius: 22))
    }
}

// MARK: - Map pieces

/// "Auto-stops in 8 min": only inside the last 30 minutes, next to the amber countdown.
private struct EndingSoonPill: View {
    var location: LocationManager

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let remaining = (location.trackingExpiresAt ?? context.date).timeIntervalSince(context.date)
            if remaining > 0, remaining <= 30 * 60 {
                Text("Auto-stops in \(Int((remaining + 59) / 60)) min")
                    .font(.system(size: 12.5, weight: .semibold))
                    .foregroundStyle(Color(red: 0x7A / 255, green: 0x4D / 255, blue: 0))
                    .padding(.horizontal, 13)
                    .frame(height: 36)
                    .background(Color(red: 1, green: 0.96, blue: 0.84), in: Capsule())
            }
        }
    }
}

/// A person who is not in the crew: a grey dot with a white edge, and a padlock when they are in a named
/// session. No name, initials or colour: nothing that says who it is.
private struct NearbyDotView: View {
    let isLocked: Bool

    var body: some View {
        Circle()
            .fill(Color(red: 0x9E / 255, green: 0xA3 / 255, blue: 0xA8 / 255))
            .overlay(Circle().stroke(.white, lineWidth: 2))
            .frame(width: 18, height: 18)
            .overlay {
                if isLocked {
                    Image(systemName: "lock.fill")
                        .font(.system(size: 8, weight: .bold))
                        .foregroundStyle(.white)
                }
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
                    .font(.largeTitle.bold())
                Text("Send this link to someone who isn't in the app. They watch your position on a web map. No app and no account.")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                if let url = casual.watchLink {
                    Text(url.absoluteString)
                        .font(.system(.subheadline, design: .monospaced))
                        .textSelection(.enabled)
                    ShareLink(item: url, message: Text("Watch my run on Dot Watcher")) {
                        Text("Share link")
                            .font(.headline)
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
                    Text(error).font(.subheadline.weight(.semibold)).foregroundStyle(.red)
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
            Text(title).font(.headline)
            Text(body).font(.subheadline).foregroundStyle(.secondary)
        }
    }
}

/// Opened by tapping the countdown: add time to the current auto-stop, or stop now.
private struct ExtendSheet: View {
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
                    .font(.headline)
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
                Text("Stop now").fontWeight(.bold).frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .tint(stopRed)
            .controlSize(.large)
        }
        .padding(24)
        .sheetCloseButton()
    }
}
