import SwiftUI

// Groups (see .ai/plans/go-and-groups.md, step 2; mock-ups .ai/mockups/go-and-groups-join.html and
// go-and-groups-before-go.html). For accounts with the "groups" feature, joining a nearby session no longer
// starts tracking in it: you join (before Go, or while out), and everyone in the group sees you by name on the
// Go map while you're out (the server does that). Everything here is gated on `LocationManager.hasGroups`.

/// The server's name for the feature.
let groupsFeature = "groups"
/// Who may organise a group: with "groups", creating a session makes a group, and that needs this too.
let createGroupsFeature = "create-groups"

/// The small line at the top of the start screen's group sign: "GROUP", or how many.
func groupSignHeader(count: Int) -> String {
    count > 1 ? "\(count) GROUPS" : "GROUP"
}

/// The line under a group's name on the group sign: whose it is ("Yours", "Kiri's", or "Joined" when an older
/// server doesn't name the organiser), and how long is left when the server says.
func groupSignLine(group: SessionMembership, now: Date) -> String {
    let organiser = group.ownerDisplayName?.trimmingCharacters(in: .whitespaces) ?? ""
    let whose = group.isOwner == true ? "Yours" : organiser.isEmpty ? "Joined" : "\(organiser)'s"
    guard let time = GroupTime(endsAtISO: group.endsAt, now: now) else { return whose }
    return "\(whose) · \(time.left)"
}

/// When a group ends, for "until it ends, …": the clock time if that's today, else the day too ("Sun 18:00").
/// Nil when the server didn't say or it has already ended.
func groupEndWords(endsAtISO: String?, now: Date, calendar: Calendar = .current) -> String? {
    guard let iso = endsAtISO, let ends = GroupTime.parse(iso), ends > now else { return nil }
    let formatter = DateFormatter()
    formatter.timeZone = calendar.timeZone
    formatter.dateFormat = calendar.isDate(ends, inSameDayAs: now) ? "H:mm" : "EEE H:mm"
    return formatter.string(from: ends)
}

/// A group's entry code: the organiser gives it to entrants so someone passing by can't join a race.
enum EntryCode {
    static let minLength = 4
    static let maxLength = 8

    private static func isAllowed(_ c: Character) -> Bool {
        ("A"..."Z").contains(c) || ("a"..."z").contains(c) || ("0"..."9").contains(c)
    }

    /// What someone types, cleaned the way the server reads it: letters and digits only, upper case, at most 8.
    static func clean(_ typed: String) -> String {
        String(typed.filter(isAllowed).uppercased().prefix(maxLength))
    }

    /// Whether the server accepts `code`: 4-8 letters A-Z or digits, any case.
    static func isValid(_ code: String) -> Bool {
        (minLength...maxLength).contains(code.count) && code.allSatisfy(isAllowed)
    }
}

/// When a group ends, worded for the person: the local clock time and how long is left.
struct GroupTime: Equatable {
    /// e.g. "12:00", in the phone's time zone.
    let endsAt: String
    /// e.g. "2 h 48 m left", "35 m left", or "Ending now" in the last minute.
    let left: String

    /// Nil when `endsAtISO` (the server's `endsAt`) is missing or unreadable (an older server), or the group
    /// has already ended, so the caller simply shows nothing.
    init?(endsAtISO: String?, now: Date, timeZone: TimeZone = .current) {
        guard let iso = endsAtISO, let ends = Self.parse(iso), ends > now else { return nil }
        let formatter = DateFormatter()
        formatter.dateFormat = "H:mm"
        formatter.timeZone = timeZone
        endsAt = formatter.string(from: ends)
        let minutes = Int(ends.timeIntervalSince(now) / 60)
        switch minutes {
        case ..<1: left = "Ending now"
        case ..<60: left = "\(minutes) m left"
        default: left = minutes % 60 == 0 ? "\(minutes / 60) h left" : "\(minutes / 60) h \(minutes % 60) m left"
        }
    }

    /// The server writes times like "2026-11-24T23:00:00.0000000+00:00". `ISO8601DateFormatter` won't read
    /// seven fractional digits, and the end time doesn't need them, so they are dropped first.
    static func parse(_ iso: String) -> Date? {
        let trimmed = iso.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression)
        return ISO8601DateFormatter().date(from: trimmed)
    }
}

// MARK: - Start screen

/// The group sign above Go (option D in the "Group button on the start screen" mock-up): every group the person
/// is in, one row each with a gear, in the palette's accent colours. A row opens that group's `GroupSheet`, which
/// holds what the big group cards used to: the organiser's controls, or Leave.
struct GroupSign: View {
    let groups: [SessionMembership]
    let now: Date
    let onOpen: (SessionMembership) -> Void

    var body: some View {
        let colors = SignColors.current
        VStack(alignment: .leading, spacing: 0) {
            colors.accentInk.frame(height: 6)
            Text(groupSignHeader(count: groups.count))
                .font(SignFont.font(size: 11))
                .tracking(1)
                .padding(.horizontal, 16)
                .padding(.top, 10)
                .padding(.bottom, 2)
                .accessibilityAddTraits(.isHeader)
            ForEach(Array(groups.enumerated()), id: \.element.id) { index, group in
                if index > 0 {
                    colors.accentInk.opacity(0.25)
                        .frame(height: 2)
                        .padding(.horizontal, 16)
                }
                Button { onOpen(group) } label: {
                    row(group, ink: colors.accentInk)
                }
                .buttonStyle(.plain)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("\(group.sessionName). \(groupSignLine(group: group, now: now))")
                .accessibilityHint("Opens the group's settings")
                .accessibilityAddTraits(.isButton)
            }
        }
        .padding(.bottom, 6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(colors.accent)
        .foregroundStyle(colors.accentInk)
    }

    private func row(_ group: SessionMembership, ink: Color) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(group.sessionName)
                    .font(SignFont.font(size: 20))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Text(groupSignLine(group: group, now: now))
                    .font(SignFont.font(size: 12))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            // About the size of the Join and Create icons; no frame, since the whole row is the button.
            Image(systemName: "gearshape.fill")
                .resizable()
                .scaledToFit()
                .frame(width: 36, height: 36)
        }
        .padding(.leading, 16)
        .padding(.trailing, 14)
        .padding(.vertical, 6)
        .contentShape(Rectangle())
    }
}

/// One group, opened from its row on the group sign: the organiser's controls on their own group, Leave on
/// anyone else's. Follows the live membership, so it closes when the group ends or the person leaves.
struct GroupSheet: View {
    var location: LocationManager
    let sessionId: String
    let isOut: Bool
    let now: Date
    @Environment(\.dismiss) private var dismiss
    @State private var errorText: String?

    private var group: SessionMembership? {
        location.groupMemberships.first { $0.sessionId == sessionId }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Spacer()
                    Button("Done") { dismiss() }
                        .font(.headline)
                }
                if let group {
                    GroupInCard(group: group, isOut: isOut, now: now, onLeave: {
                        Task { await leave() }
                    }, organiser: location)
                } else {
                    Text("You're not in this group any more.")
                }
                if let errorText {
                    Text(errorText)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.red)
                }
            }
            .padding(24)
        }
        .onChange(of: group == nil) { _, gone in
            if gone { dismiss() }
        }
    }

    private func leave() async {
        do {
            try await location.leaveGroup(sessionId: sessionId)
            errorText = nil
        } catch {
            errorText = "Couldn't leave the group. Check your connection and try again."
        }
    }
}

/// One group the person is in, on the start screen and in "Your groups". `isOut` is whether they're on a Go
/// run right now: out, the group can see them; not out, nobody can yet.
///
/// For a group the person organised (`group.isOwner`, with `organiser` set) the card carries the organiser's
/// controls, since a group has no session map: Ping here, Choose on map (a join location somewhere else), the
/// entry code, and End group in place of Leave.
struct GroupInCard: View {
    let group: SessionMembership
    let isOut: Bool
    let now: Date
    let onLeave: () -> Void
    /// Enables the organiser's controls on their own group; nil shows Leave only.
    var organiser: LocationManager? = nil
    @State private var confirmLeave = false
    @State private var confirmEnd = false
    @State private var showCode = false
    @State private var showPicker = false
    /// A nearby group's entry code, loaded for the dashed code box; nil while loading or when there's none.
    @State private var entryCode: String?
    @State private var message: String?
    @State private var isBusy = false

    private var isOrganiser: Bool { organiser != nil && group.isOwner == true }

    /// The code joiners need, shown in the dashed box (mock-up `.ai/mockups/go-and-groups-join.html`): a
    /// nearby group's entry code when one is set, otherwise a private group's invite code.
    private var shownCode: (label: String, code: String)? {
        if group.nearby == true {
            return entryCode.map { ("Entry code", $0) }
        }
        return group.inviteCode.isEmpty ? nil : ("Invite code", group.inviteCode)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(eyebrow)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(group.sessionName)
                .font(.title3.bold())
            Text(summary)
                .font(.subheadline)
                .fixedSize(horizontal: false, vertical: true)
            if isOrganiser, let organiser {
                organiserControls(organiser)
            } else {
                Button("Leave group", role: .destructive) { confirmLeave = true }
                    .font(.subheadline.weight(.semibold))
                    .padding(.top, 4)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.secondary.opacity(0.6), lineWidth: 2))
        .confirmationDialog("Leave \(group.sessionName)?", isPresented: $confirmLeave, titleVisibility: .visible) {
            Button("Leave", role: .destructive, action: onLeave)
            Button("Stay in", role: .cancel) {}
        } message: {
            Text("The group stops seeing you by name. Your crew still can.")
        }
        .confirmationDialog("End \(group.sessionName)?", isPresented: $confirmEnd, titleVisibility: .visible) {
            Button("End group", role: .destructive) {
                Task { await end() }
            }
            Button("Keep it", role: .cancel) {}
        } message: {
            Text("It ends for everyone and nobody new can join. Anyone out carries on with their own run.")
        }
    }

    private var eyebrow: String {
        let label = isOrganiser ? "YOUR GROUP" : "YOU'RE IN"
        guard let time = GroupTime(endsAtISO: group.endsAt, now: now) else { return label }
        return "\(label) · ENDS \(time.endsAt) · \(time.left.uppercased())"
    }

    private var summary: String {
        if isOrganiser {
            return group.nearby == true
                ? "People near your join location see it on their start screen. You don't need to be out."
                : "Only people with the invite code can join."
        }
        return isOut
            ? "You're out, so everyone in the group can see you by name."
            : "Not out yet. Nobody in the group can see you until you press Go."
    }

    @ViewBuilder
    private func organiserControls(_ location: LocationManager) -> some View {
        if let shownCode {
            codeBox(label: shownCode.label, code: shownCode.code)
        }
        // Two rows: where people join (Ping here, or a point chosen on the map), then the code and End group.
        if group.nearby == true {
            HStack(spacing: 16) {
                Button("Ping here") {
                    Task {
                        isBusy = true
                        message = await location.pingGroup(sessionId: group.sessionId)
                        isBusy = false
                    }
                }
                .disabled(isBusy)
                Button("Choose on map") { showPicker = true }
                    .disabled(isBusy)
            }
            .font(.subheadline.weight(.semibold))
            .padding(.top, 4)
        }
        HStack(spacing: 16) {
            // With a code set, the box above carries Change; this is only for adding one.
            if group.nearby == true, entryCode == nil {
                Button(showCode ? "Hide code" : "Entry code") { showCode.toggle() }
            }
            Button("End group", role: .destructive) { confirmEnd = true }
                .disabled(isBusy)
        }
        .font(.subheadline.weight(.semibold))
        .padding(.top, group.nearby == true ? 0 : 4)
        .sheet(isPresented: $showPicker) {
            JoinLocationPickerSheet(location: location, group: group) { message = "Join location set on the map" }
                .presentationDetents([.large])
                .preferredColorScheme(location.appearanceMode.colorScheme)
        }
        .task(id: group.sessionId) {
            guard group.nearby == true else { return }
            entryCode = try? await location.entryCode(sessionId: group.sessionId)
        }
        if showCode {
            EntryCodeSection(location: location, sessionId: group.sessionId, onChanged: { entryCode = $0 })
                .padding(.top, 6)
        }
        if let message {
            Text(message)
                .font(.subheadline.weight(.semibold))
                .fixedSize(horizontal: false, vertical: true)
        }
        if group.nearby == true {
            GroupAreaSection(location: location, group: group)
                .padding(.top, 6)
        }
    }

    /// The code inside a dashed box; Change (nearby groups only, since invite codes are fixed) opens the editor.
    private func codeBox(label: String, code: String) -> some View {
        HStack(spacing: 8) {
            Text(label)
                .font(.subheadline)
            Text(code)
                .font(.system(.title3, design: .monospaced).weight(.bold))
                .kerning(2)
                .textSelection(.enabled)
            Spacer(minLength: 0)
            if group.nearby == true {
                Button(showCode ? "Done" : "Change") { showCode.toggle() }
                    .font(.subheadline.weight(.semibold))
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color.primary, style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
        )
        .padding(.top, 6)
    }

    private func end() async {
        guard let organiser else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            try await organiser.endSession(sessionId: group.sessionId)
        } catch {
            message = error.localizedDescription
        }
    }
}

/// On the organiser's card for a nearby group: how big the join area is, and a picker to change it. The join
/// location stays put; only the size changes. A map of the area shows when this phone knows where the join
/// location is (it set it, by creating the group or pressing Ping here, since the app opened).
struct GroupAreaSection: View {
    var location: LocationManager
    let group: SessionMembership
    @State private var chosen: Int?
    @State private var message: String?
    @State private var isBusy = false

    private var current: Int {
        location.nearbyStatuses[group.sessionId]?.radiusMeters ?? group.nearbyRadiusMeters ?? NearbyRadius.defaultMeters
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("Join area")
                    .font(.subheadline)
                Spacer()
                Picker("Join area", selection: Binding(get: { chosen ?? current }, set: { change(to: $0) })) {
                    ForEach(NearbyRadius.options, id: \.meters) { option in
                        Text(option.label).tag(option.meters)
                    }
                }
                .pickerStyle(.menu)
                .disabled(isBusy)
            }
            if let center = location.nearbyMeetingPoints[group.sessionId] {
                NearbyRangePreview(center: center, radiusMeters: chosen ?? current)
            }
            if let message {
                Text(message)
                    .font(.subheadline.weight(.semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func change(to radius: Int) {
        guard radius != (chosen ?? current) else { return }
        chosen = radius
        Task {
            isBusy = true
            message = await location.setGroupRadius(sessionId: group.sessionId, radiusMeters: radius)
            // The server's answer (in nearbyStatuses) wins; on a failure the picker goes back to it.
            chosen = nil
            isBusy = false
        }
    }
}

// MARK: - Go map

/// Top-left on the Go map, under the timer: "‹ Home" to reach the start screen while tracking carries on (to
/// join a group while out), and, when the person is in any groups, a chip naming the newest with its time left.
struct GoMapGroupBar: View {
    let groups: [SessionMembership]
    let now: Date
    let onHome: () -> Void
    let onOpenGroups: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button(action: onHome) {
                Text("‹ Home")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.black)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 7)
                    .background(Color.white.opacity(0.94), in: RoundedRectangle(cornerRadius: 14))
                    .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Back to the start screen")

            if let newest = groups.first {
                Button(action: onOpenGroups) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(chipLine)
                            .font(.system(size: 9, weight: .semibold))
                            .kerning(0.8)
                            .foregroundStyle(Color(white: 0.35))
                        Text(newest.sessionName)
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(.black)
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 7)
                    .background(Color.white.opacity(0.94), in: RoundedRectangle(cornerRadius: 14))
                    .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Your groups")
            }
        }
    }

    private var chipLine: String {
        var line = groups.count > 1 ? "\(groups.count) GROUPS" : "GROUP"
        if let first = groups.first, let time = GroupTime(endsAtISO: first.endsAt, now: now) {
            line += " · \(time.left.uppercased())"
        }
        return line
    }
}

/// The groups the person is in, from the Go map: each with its end time and Leave.
struct GroupsOnMapSheet: View {
    var location: LocationManager
    let now: Date
    @Environment(\.dismiss) private var dismiss
    @State private var errorText: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text("Your groups")
                    .font(.title2.bold())
                Text("Everyone in these groups sees you by name while you're out.")
                    .foregroundStyle(.secondary)
                if location.groupMemberships.isEmpty {
                    Text("You're not in any groups now.")
                }
                ForEach(location.groupMemberships) { group in
                    GroupInCard(group: group, isOut: true, now: now, onLeave: {
                        Task { await leave(group.sessionId) }
                    }, organiser: location)
                }
                if let errorText {
                    Text(errorText)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.red)
                }
                Button("Done") { dismiss() }
                    .frame(maxWidth: .infinity)
            }
            .padding(24)
        }
    }

    private func leave(_ sessionId: String) async {
        do {
            try await location.leaveGroup(sessionId: sessionId)
            errorText = nil
        } catch {
            errorText = "Couldn't leave the group. Check your connection and try again."
        }
    }
}

// MARK: - Organiser

/// The organiser's entry code, inside their nearby settings sheet: what it is now, and a field to change or
/// remove it. People already in stay in; only joins from now on need the new code.
struct EntryCodeSection: View {
    var location: LocationManager
    let sessionId: String
    /// Told the code after a change, so a card showing it can keep up.
    var onChanged: ((String?) -> Void)? = nil
    @State private var current: String?
    @State private var loaded = false
    @State private var typed = ""
    @State private var message: String?
    @State private var isBusy = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Entry code")
                .font(.headline)
            Text(summary)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            TextField(current == nil ? "New code" : "Change to", text: $typed)
                .textFieldStyle(.roundedBorder)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .onChange(of: typed) { _, value in
                    let cleaned = EntryCode.clean(value)
                    if cleaned != value { typed = cleaned }
                }
            Text("4 to 8 letters or numbers. People already in stay in.")
                .font(.footnote)
                .foregroundStyle(.secondary)
            HStack {
                Button(current == nil ? "Ask for code" : "Change code") {
                    Task { await change(to: typed) }
                }
                .buttonStyle(.borderedProminent)
                .disabled(isBusy || !loaded || !EntryCode.isValid(typed))
                if current != nil {
                    Button("Remove code") {
                        Task { await change(to: nil) }
                    }
                    .buttonStyle(.bordered)
                    .disabled(isBusy)
                }
            }
            if let message {
                Text(message)
                    .font(.subheadline.weight(.semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .task { await load() }
    }

    private var summary: String {
        if !loaded { return "Loading…" }
        if let current {
            return "People need \(current) to join. Give it to entrants; anyone walking past can't join without it."
        }
        return "None. Anyone nearby can join with one tap."
    }

    private func load() async {
        do {
            current = try await location.entryCode(sessionId: sessionId)
        } catch {
            message = error.localizedDescription
        }
        loaded = true
    }

    private func change(to code: String?) async {
        isBusy = true
        defer { isBusy = false }
        do {
            current = try await location.setEntryCode(sessionId: sessionId, code: code)
            onChanged?(current)
            message = code == nil ? "Anyone nearby can join now" : "Code changed to \(code!.uppercased())"
            typed = ""
        } catch DotWatcherAPIError.badResponse(410, _) {
            message = "This session has ended."
        } catch {
            message = error.localizedDescription
        }
    }
}
