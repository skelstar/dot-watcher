import CoreLocation
import SwiftUI

// Setting a group's join location at a point on the map instead of where the organiser is standing: a
// crosshair in the middle of the map marks the point. Two ways in: "Choose on map" on the organiser's group
// card (`JoinLocationPickerSheet`, works without going out), and on the Go map once it's dragged off the
// person's dot (`MapCrosshairAction`, through `TrackingMapScreen`).

/// The small cross in the middle of the map: black with a white edge, so it shows on any basemap colour.
/// Takes no touches, so the map still drags under it.
struct MapCrosshair: View {
    private static let length: CGFloat = 22
    private static let thickness: CGFloat = 2

    var body: some View {
        ZStack {
            bars(length: Self.length + 4, thickness: Self.thickness + 2).foregroundStyle(.white)
            bars(length: Self.length, thickness: Self.thickness).foregroundStyle(.black)
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    private func bars(length: CGFloat, thickness: CGFloat) -> some View {
        ZStack {
            Rectangle().frame(width: length, height: thickness)
            Rectangle().frame(width: thickness, height: length)
        }
    }
}

/// Something the Go map can do at the crosshair, shown as a button while the map is dragged off the person's
/// dot. `perform` returns nil when it worked, or what went wrong.
struct MapCrosshairAction: Identifiable {
    let id: String
    let label: String
    /// Shown for a few seconds after `perform` succeeds.
    let doneMessage: String
    let perform: (CLLocationCoordinate2D) async -> String?
}

extension MapCrosshairAction {
    /// One "Set join location" per group the person organises with nearby join on. With several, each button
    /// names its group.
    static func joinLocations(for location: LocationManager) -> [MapCrosshairAction] {
        let owned = location.groupMemberships.filter { $0.isOwner == true && $0.nearby == true }
        return owned.map { group in
            MapCrosshairAction(
                id: "join-location-\(group.sessionId)",
                label: owned.count > 1 ? "Set join location for \(group.sessionName)" : "Set join location here",
                doneMessage: "Join location set for \(group.sessionName)",
                perform: { await location.setGroupJoinLocation(sessionId: group.sessionId, at: $0) }
            )
        }
    }
}

/// The organiser's join range for the Go map: the first group they organise with nearby on whose join
/// location this phone knows (set from here, or when it was created on this phone).
func ownedGroupJoinRange(for location: LocationManager) -> LinzJoinRange? {
    for group in location.groupMemberships where group.isOwner == true && group.nearby == true {
        if let center = location.nearbyMeetingPoints[group.sessionId] {
            let radius = location.nearbyStatuses[group.sessionId]?.radiusMeters ?? group.nearbyRadiusMeters ?? NearbyRadius.defaultMeters
            return LinzJoinRange(center: center, radiusMeters: Double(radius))
        }
    }
    return nil
}

/// The buttons under the crosshair on the Go map, and the result of the last one pressed.
struct MapCrosshairActionBar: View {
    let actions: [MapCrosshairAction]
    let centre: CLLocationCoordinate2D?
    @State private var isBusy = false
    @State private var message: String?

    var body: some View {
        VStack(spacing: 8) {
            if let message {
                Text(message)
                    .font(.app(.subheadline, weight: .semibold))
                    .foregroundStyle(.black)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 7)
                    .background(Color.white.opacity(0.94), in: RoundedRectangle(cornerRadius: 14))
                    .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
            }
            ForEach(actions) { action in
                Button {
                    run(action)
                } label: {
                    Text(isBusy ? "Setting…" : action.label)
                        .font(.app(.subheadline, weight: .semibold))
                        .foregroundStyle(.black)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 9)
                        .background(Color.white.opacity(0.94), in: Capsule())
                        .shadow(color: .black.opacity(0.2), radius: 2, y: 1)
                }
                .buttonStyle(.plain)
                .disabled(isBusy || centre == nil)
            }
        }
    }

    private func run(_ action: MapCrosshairAction) {
        guard let centre else { return }
        Task {
            isBusy = true
            let failure = await action.perform(centre)
            isBusy = false
            message = failure ?? action.doneMessage
            try? await Task.sleep(for: .seconds(4))
            message = nil
        }
    }
}

/// "Choose on map" from the organiser's group card: drag the map until the crosshair is on the spot, see the
/// join range around it, and set it. Opens on the current join location when this phone knows it, otherwise
/// on where the phone is.
struct JoinLocationPickerSheet: View {
    var location: LocationManager
    let group: SessionMembership
    /// Called once the join location is set, just before the sheet closes.
    var onSet: () -> Void = {}
    @Environment(\.dismiss) private var dismiss
    @State private var camera = LinzMapCamera()
    @State private var centre: CLLocationCoordinate2D?
    @State private var isBusy = false
    @State private var errorText: String?

    private var radiusMeters: Double {
        Double(location.nearbyStatuses[group.sessionId]?.radiusMeters ?? group.nearbyRadiusMeters ?? NearbyRadius.defaultMeters)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Join location")
                .font(.app(.title2, weight: .bold))
            Text("Move the map until the cross is where people should be to join \(group.sessionName). You don't need to be there.")
                .font(.app(.subheadline))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ZStack {
                LinzMapView(
                    markers: [],
                    joinRange: centre.map { LinzJoinRange(center: $0, radiusMeters: radiusMeters) },
                    camera: camera,
                    onCenterChanged: { centre = $0 }
                )
                MapCrosshair()
                LinzAttribution()
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
            }
            .clipShape(RoundedRectangle(cornerRadius: 12))
            if let errorText {
                Text(errorText)
                    .font(.app(.subheadline, weight: .semibold))
                    .foregroundStyle(.red)
            }
            Button {
                Task { await set() }
            } label: {
                Text(isBusy ? "Setting…" : "Set join location")
                    .font(.app(.body, weight: .bold))
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .disabled(isBusy || centre == nil)
        }
        .padding(20)
        .sheetCloseButton()
        .task {
            let start: CLLocationCoordinate2D?
            if let known = location.nearbyMeetingPoints[group.sessionId] {
                start = known
            } else {
                start = await location.currentFix()?.coordinate
            }
            // Wide enough to see the whole range with room around it.
            let span = max(0.006, radiusMeters * 4 / 111_320)
            camera.move(to: LinzMapRegion(center: start ?? LinzBasemap.newZealand, span: start == nil ? 8 : span))
        }
    }

    private func set() async {
        guard let centre else { return }
        isBusy = true
        defer { isBusy = false }
        if let failure = await location.setGroupJoinLocation(sessionId: group.sessionId, at: centre) {
            errorText = failure
        } else {
            onSet()
            dismiss()
        }
    }
}
