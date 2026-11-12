import CoreLocation
import SwiftUI

/// A message pin dropped on the map: by someone in a named session (`GET /sessions/{id}/pins`), or on a
/// Go run by this person or their crew (inside `GET /casual/live`). `isMine` and `canDelete` are
/// worked out by the server for this viewer. See .ai/plans/session-map-pins.md.
struct MapMessagePin: Decodable, Identifiable, Equatable {
    let id: String
    /// The name this viewer knows the author by.
    let author: String
    let latitude: Double
    let longitude: Double
    let message: String
    let createdAt: String
    let isMine: Bool?
    let canDelete: Bool?

    var coordinate: CLLocationCoordinate2D { CLLocationCoordinate2D(latitude: latitude, longitude: longitude) }

    /// The server sends fractional seconds (up to seven digits), which `ISO8601DateFormatter` can't
    /// always read, so they are dropped first: second precision is plenty for "5 min ago".
    var createdDate: Date? {
        let whole = createdAt.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression)
        return positionTimestampParser.date(from: whole)
    }

    static let maxMessageLength = 140
}

/// What the map shell needs to show and manage one source's message pins.
struct MessagePinActions {
    let pins: [MapMessagePin]
    /// Whether press-and-hold drops a pin (runners only, and only while out on Go).
    let canDrop: Bool
    /// Who will see a new pin, for the drop sheet.
    let audience: String
    let drop: (CLLocationCoordinate2D, String) async throws -> Void
    let delete: (MapMessagePin) async throws -> Void
}

/// A coordinate the person pressed and held on, waiting for the message.
struct PendingMessagePin: Identifiable {
    let id = UUID()
    let coordinate: CLLocationCoordinate2D
}

/// A pin on the map: a speech bubble in a white circle, drawn above the runners' dots.
struct MessagePinMarker: View {
    let isMine: Bool

    var body: some View {
        Image(systemName: "text.bubble.fill")
            .font(.system(size: 14, weight: .bold))
            .foregroundStyle(.white)
            .frame(width: 30, height: 30)
            .background(Circle().fill(isMine ? Color.accentColor : Color(red: 0xE8 / 255, green: 0x8A / 255, blue: 0x0C / 255)))
            .overlay(Circle().stroke(.white, lineWidth: 2))
            .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
            .accessibilityLabel("Message pin")
    }
}

/// Typing the message for a pin about to be dropped where the person pressed.
struct DropMessagePinSheet: View {
    let audience: String
    let onDrop: (String) async throws -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var message = ""
    @State private var isSending = false
    @State private var error: String?
    @FocusState private var isFocused: Bool

    private var trimmed: String { message.trimmingCharacters(in: .whitespacesAndNewlines) }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Water here, turn left…", text: $message, axis: .vertical)
                        .lineLimit(2...5)
                        .focused($isFocused)
                        .onChange(of: message) { _, new in
                            if new.count > MapMessagePin.maxMessageLength {
                                message = String(new.prefix(MapMessagePin.maxMessageLength))
                            }
                        }
                } footer: {
                    HStack(alignment: .top) {
                        Text(audience)
                        Spacer()
                        Text("\(message.count)/\(MapMessagePin.maxMessageLength)")
                            .monospacedDigit()
                    }
                }
                if let error {
                    Section { Text(error).foregroundStyle(.red) }
                }
            }
            .navigationTitle("Drop a pin")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Drop") { Task { await send() } }
                        .disabled(trimmed.isEmpty || isSending)
                }
            }
            .onAppear { isFocused = true }
        }
    }

    private func send() async {
        isSending = true
        error = nil
        defer { isSending = false }
        do {
            try await onDrop(trimmed)
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
    }
}

/// A tapped pin: the message, who left it and when, and Delete when this person may.
struct MessagePinDetailSheet: View {
    let pin: MapMessagePin
    let onDelete: () async throws -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var isDeleting = false
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(pin.message)
                .font(.title3.weight(.semibold))
                .textSelection(.enabled)
            Group {
                if let date = pin.createdDate {
                    Text("\(pin.isMine == true ? "You" : pin.author) · \(date, style: .relative) ago")
                } else {
                    Text(pin.isMine == true ? "You" : pin.author)
                }
            }
            .font(.subheadline)
            .foregroundStyle(.secondary)
            if let error {
                Text(error).font(.subheadline).foregroundStyle(.red)
            }
            Spacer(minLength: 0)
            if pin.canDelete == true {
                Button(role: .destructive) {
                    Task { await delete() }
                } label: {
                    Text("Delete pin").frame(maxWidth: .infinity)
                }
                .buttonStyle(.bordered)
                .controlSize(.large)
                .disabled(isDeleting)
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func delete() async {
        isDeleting = true
        error = nil
        defer { isDeleting = false }
        do {
            try await onDelete()
            dismiss()
        } catch {
            self.error = error.localizedDescription
        }
    }
}
