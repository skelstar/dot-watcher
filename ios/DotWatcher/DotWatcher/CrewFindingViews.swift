import SwiftUI
import UIKit

/// "Find new crew", at the top of the crew sheet: the switch with its countdown, the people next to you who
/// also have it on, and the Accept question. See `CrewFinding` and .ai/plans/crew-finding.md.
struct FindNewCrewSection: View {
    var finding: CrewFinding
    @State private var candidate: CrewNearbyPerson?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Toggle(isOn: Binding(
                get: { finding.isOn || finding.isStarting },
                set: { finding.setOn($0) }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Find new crew")
                        .font(.headline)
                    subtitle
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .disabled(finding.isStarting)

            if let note = finding.note {
                VStack(alignment: .leading, spacing: 6) {
                    Text(note)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    if finding.needsSettings {
                        Button("Open Settings") {
                            if let url = URL(string: UIApplication.openSettingsURLString) {
                                UIApplication.shared.open(url)
                            }
                        }
                        .font(.footnote.weight(.semibold))
                    }
                }
            }

            if let added = finding.justAdded {
                Label("\(added.label) is in your crew", systemImage: "checkmark.circle.fill")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(.green)
                    .task(id: added.userId) {
                        try? await Task.sleep(for: .seconds(4))
                        if finding.justAdded?.userId == added.userId { finding.justAdded = nil }
                    }
            }

            if finding.isOn {
                nearbyList
            }
        }
        .padding(14)
        .background(Color(.systemGray6), in: RoundedRectangle(cornerRadius: 14))
        .alert(
            candidate.map { "Add \($0.label) to your crew?" } ?? "",
            isPresented: Binding(get: { candidate != nil }, set: { if !$0 { candidate = nil } }),
            presenting: candidate
        ) { person in
            Button(person.acceptedYou ? "Not now" : "Cancel", role: .cancel) {}
            Button("Accept") {
                Task { await finding.accept(person) }
            }
        } message: { person in
            if person.acceptedYou {
                Text("\(person.label) has already accepted you. You'll see each other on the map.")
            } else {
                Text("You'll see each other on the map. You'll be crew once \(person.label) accepts you as well.")
            }
        }
    }

    @ViewBuilder
    private var subtitle: some View {
        if finding.isStarting {
            Text("Turning on…")
        } else if finding.isOn, let expiresAt = finding.expiresAt {
            TimelineView(.periodic(from: .now, by: 1)) { context in
                Text("On for \(Self.countdown(until: expiresAt, now: context.date))")
                    .monospacedDigit()
            }
        } else {
            Text("Let people right next to you add you")
        }
    }

    @ViewBuilder
    private var nearbyList: some View {
        HStack(spacing: 8) {
            ProgressView()
                .controlSize(.small)
            Text("Looking for people within 50 m")
                .font(.footnote)
                .foregroundStyle(.secondary)
        }

        if finding.people.isEmpty {
            Text("No one yet. Ask them to turn on Find new crew too.")
                .font(.footnote)
                .foregroundStyle(.secondary)
        } else {
            VStack(spacing: 0) {
                ForEach(Array(finding.people.enumerated()), id: \.element.id) { index, person in
                    if index > 0 { Divider().padding(.leading, 52) }
                    row(person)
                }
            }
            .background(Color(.systemBackground), in: RoundedRectangle(cornerRadius: 12))
        }

        if let error = finding.acceptError {
            Text(error)
                .font(.footnote)
                .foregroundStyle(.red)
        }

        Text("Only people who also have this on. They see your name too, nothing else.")
            .font(.caption)
            .foregroundStyle(.secondary)
    }

    private func row(_ person: CrewNearbyPerson) -> some View {
        Button {
            candidate = person
        } label: {
            HStack(spacing: 10) {
                CrewDot(
                    initials: RunnerCircle.initials(for: person.displayName),
                    color: .gray,
                    size: 30,
                    isCrew: false
                )
                VStack(alignment: .leading, spacing: 1) {
                    Text(person.label)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.primary)
                    Text(status(person))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                if finding.acceptingId == person.id {
                    ProgressView()
                } else if person.acceptedYou {
                    badge("Wants you as crew", color: .green)
                } else if person.youAccepted {
                    badge("Waiting", color: .orange)
                } else {
                    Image(systemName: "chevron.right")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(.tertiary)
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        // Accepted already: nothing more to do until they accept back.
        .disabled(person.youAccepted || finding.acceptingId != nil)
        .accessibilityHint(person.youAccepted ? "" : "Asks before adding")
    }

    private func status(_ person: CrewNearbyPerson) -> String {
        if person.acceptedYou { return "Accepted you" }
        if person.youAccepted { return "Waiting for \(person.label)" }
        return "Tap to add"
    }

    private func badge(_ text: String, color: Color) -> some View {
        Text(text)
            .font(.caption2.weight(.bold))
            .foregroundStyle(color)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .background(color.opacity(0.15), in: RoundedRectangle(cornerRadius: 6))
    }

    /// "1:48".
    static func countdown(until end: Date, now: Date) -> String {
        let seconds = max(0, Int(end.timeIntervalSince(now).rounded(.up)))
        return String(format: "%d:%02d", seconds / 60, seconds % 60)
    }
}
