import SwiftUI

// The bottom-left chip of dots on both tracking maps: the session map shows everyone in the session, the Go
// map this person and their crew who are out. Both maps use it so the two can't drift apart.

/// One dot in the chip: `id` is the map pin's id, which is also the name shown for them (a crew nickname on
/// Go) and picks the colour; `dotLabel` is what the letters on the dot come from (the account's initials on
/// Go, as on the pins), nil to use `id`.
struct MapPerson: Identifiable, Hashable {
    let id: String
    var dotLabel: String? = nil
}

/// Up to four people as separate dots, you first, in the pins' colours; a dashed edge means no recent
/// position. Tapping a dot centres the map on that runner; tap it again, or the fit-all button, to see
/// everyone. With more than four the dots overlap, as in the crew row, and the whole chip opens the list
/// (`onShowAll`, which presents `MapPeopleSheet`).
struct MapPeopleChip: View {
    /// You first.
    let people: [MapPerson]
    let me: String
    let pins: [MapPin]
    var controller: TrackingMapController
    /// What the overlapped chip says to VoiceOver, e.g. "5 people in this session".
    let countLabel: String
    /// Touch and hold on someone else's dot (the session map's block); nil for none.
    var onHold: ((MapPerson) -> Void)? = nil
    let onShowAll: () -> Void

    private static let separateDotLimit = 4
    private static let overlappedFaces = 6

    var body: some View {
        let overlapped = people.count > Self.separateDotLimit
        let shown = people.prefix(overlapped ? Self.overlappedFaces : Self.separateDotLimit)
        let extra = people.count - shown.count
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let chip = HStack(spacing: overlapped ? -10 : 6) {
                ForEach(Array(shown)) { person in
                    let pin = pins.first { $0.id == person.id }
                    let age = pin?.timestamp.map { context.date.timeIntervalSince($0) }
                    let dot = RunnerCircle(
                        name: person.id,
                        label: person.dotLabel,
                        size: 34,
                        fontSize: 11,
                        fillColor: person.id == me ? RunnerColorPalette.currentUser : RunnerColorPalette.color(for: person.id),
                        isDisconnected: pin == nil || (age.map { $0 > MapPinAge.disconnectedAfter } ?? false)
                    )
                    .overlay(
                        Circle()
                            .stroke(Color.black, lineWidth: 2.5)
                            .opacity(controller.followedId == person.id ? 1 : 0)
                            .padding(-3)
                    )
                    if overlapped {
                        // A chip-coloured edge keeps overlapped dots apart.
                        dot.background(Circle().fill(Color.white)).overlay(Circle().strokeBorder(Color.white, lineWidth: 2))
                    } else {
                        dot
                            .onTapGesture {
                                guard pin != nil else { return }
                                controller.toggleFollow(person.id)
                            }
                            .onLongPressGesture {
                                if person.id != me { onHold?(person) }
                            }
                            .accessibilityElement(children: .ignore)
                            .accessibilityLabel(person.id == me ? "You" : person.id)
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
                Button(action: onShowAll) { chip }
                    .buttonStyle(.plain)
                    .accessibilityLabel(countLabel)
                    .accessibilityHint("Opens the list")
            } else {
                chip
            }
        }
    }
}

/// Everyone in the chip, for when there are too many dots to tap one at a time. Tapping someone with a
/// position closes the list and centres the map on them.
struct MapPeopleSheet: View {
    /// "In this session", "Out now".
    let title: String
    let people: [MapPerson]
    let me: String
    let followedId: String?
    /// Seconds since the person's last position, or nil if they have none.
    let status: (MapPerson) -> TimeInterval?
    let onSelect: (MapPerson) -> Void
    /// Offered on a touch and hold of someone else (the session map); nil for none.
    var onBlock: ((MapPerson) -> Void)? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("\(title) · \(people.count)")
                .font(.title3.bold())
                .padding(.horizontal, 20)
                .padding(.top, 20)
            ScrollView {
                VStack(spacing: 0) {
                    ForEach(people) { person in
                        let age = status(person)
                        Button { onSelect(person) } label: {
                            HStack(spacing: 14) {
                                RunnerCircle(
                                    name: person.id,
                                    label: person.dotLabel,
                                    size: 40,
                                    fontSize: 13,
                                    fillColor: person.id == me ? RunnerColorPalette.currentUser : RunnerColorPalette.color(for: person.id),
                                    isDisconnected: age == nil || (age ?? 0) > MapPinAge.disconnectedAfter
                                )
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(person.id == me ? "You" : person.id).font(.headline)
                                    Text(Self.statusText(age)).font(.caption).foregroundStyle(.secondary)
                                }
                                Spacer()
                                if followedId == person.id {
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
                            if person.id != me, let onBlock {
                                Button(role: .destructive) { onBlock(person) } label: {
                                    Label("Block \(person.id)", systemImage: "hand.raised")
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
