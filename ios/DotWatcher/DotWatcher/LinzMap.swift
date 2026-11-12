import CoreLocation
import MapLibre
import SwiftUI
import UIKit

/// LINZ Basemaps' topographic vector style, the same basemap as the web client
/// (`client/src/map/mapStyle.ts`) and the Android app (`MapScreen.kt`). Coverage is New Zealand only:
/// outside NZ the map is blank.
///
/// The key comes from the `LINZ_API_KEY` build setting (set in the gitignored
/// `ios/DotWatcher/Config/Secrets.xcconfig`, see `ios/README.md`) via `LINZAPIKey` in `Info.plist`.
/// It ships inside the app, like the web client's browser key: treat it as public.
enum LinzBasemap {
    static var apiKey: String {
        (Bundle.main.object(forInfoDictionaryKey: "LINZAPIKey") as? String)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    static var styleURL: URL {
        if apiKey.isEmpty {
            print("LinzBasemap: no LINZ_API_KEY set (see ios/README.md); the map will be blank.")
        }
        var components = URLComponents(string: "https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json")!
        components.queryItems = [URLQueryItem(name: "api", value: apiKey)]
        return components.url!
    }

    static let copyrightURL = URL(string: "https://www.linz.govt.nz/linz-copyright")!
    static let dataAttributionURL = URL(string: "https://www.linz.govt.nz/data/linz-data/linz-basemaps/data-attribution")!

    /// Where the map starts before there is anyone to frame.
    static let newZealand = CLLocationCoordinate2D(latitude: -41.0, longitude: 173.0)
}

/// The LINZ licence (CC BY 4.0) asks for visible attribution linking to its copyright statement and the
/// basemap's data attribution. The style declares none of its own, so the map's built-in attribution
/// button is hidden and this is shown instead, as the web client does with `LINZ_ATTRIBUTION`.
struct LinzAttribution: View {
    var body: some View {
        HStack(spacing: 4) {
            Link("© LINZ CC BY 4.0", destination: LinzBasemap.copyrightURL)
            Text("·")
            Link("Basemaps data attribution", destination: LinzBasemap.dataAttributionURL)
        }
        .font(.system(size: 9.5))
        .foregroundStyle(.black.opacity(0.7))
        .tint(.black.opacity(0.7))
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
        .background(Color.white.opacity(0.7), in: Capsule())
    }
}

/// A rectangle of the map: `center`, and how many degrees it spans each way.
struct LinzMapRegion: Equatable {
    var center: CLLocationCoordinate2D
    var latitudeDelta: Double
    var longitudeDelta: Double

    init(center: CLLocationCoordinate2D, latitudeDelta: Double, longitudeDelta: Double) {
        self.center = center
        self.latitudeDelta = latitudeDelta
        self.longitudeDelta = longitudeDelta
    }

    init(center: CLLocationCoordinate2D, span: Double) {
        self.init(center: center, latitudeDelta: span, longitudeDelta: span)
    }

    /// Everyone in `coordinates` with half as much again around them, never closer than `minimumSpan`
    /// degrees across (so a lone runner shows their surroundings). nil when there is nobody.
    static func fitting(_ coordinates: [CLLocationCoordinate2D], minimumSpan: Double = 0.01) -> LinzMapRegion? {
        guard !coordinates.isEmpty else { return nil }
        let minLat = coordinates.map(\.latitude).min()!
        let maxLat = coordinates.map(\.latitude).max()!
        let minLon = coordinates.map(\.longitude).min()!
        let maxLon = coordinates.map(\.longitude).max()!
        return LinzMapRegion(
            center: CLLocationCoordinate2D(latitude: (minLat + maxLat) / 2, longitude: (minLon + maxLon) / 2),
            latitudeDelta: max(minimumSpan, (maxLat - minLat) * 1.5),
            longitudeDelta: max(minimumSpan, (maxLon - minLon) * 1.5))
    }

    var bounds: MLNCoordinateBounds {
        MLNCoordinateBounds(
            sw: CLLocationCoordinate2D(latitude: center.latitude - latitudeDelta / 2, longitude: center.longitude - longitudeDelta / 2),
            ne: CLLocationCoordinate2D(latitude: center.latitude + latitudeDelta / 2, longitude: center.longitude + longitudeDelta / 2))
    }

    static func == (lhs: LinzMapRegion, rhs: LinzMapRegion) -> Bool {
        lhs.center.latitude == rhs.center.latitude && lhs.center.longitude == rhs.center.longitude
            && lhs.latitudeDelta == rhs.latitudeDelta && lhs.longitudeDelta == rhs.longitudeDelta
    }
}

/// Where the screen wants the camera. Each `move(to:)` is one request: the map animates there once, then
/// leaves the camera alone (so the person can drag it) until the next request.
struct LinzMapCamera {
    private(set) var region: LinzMapRegion?
    private(set) var revision = 0

    mutating func move(to region: LinzMapRegion) {
        self.region = region
        revision += 1
    }
}

/// A nearby session's join range: a dotted ring of `radiusMeters` around `center`.
struct LinzJoinRange: Equatable {
    var center: CLLocationCoordinate2D
    var radiusMeters: Double

    static func == (lhs: LinzJoinRange, rhs: LinzJoinRange) -> Bool {
        lhs.center.latitude == rhs.center.latitude && lhs.center.longitude == rhs.center.longitude
            && lhs.radiusMeters == rhs.radiusMeters
    }

    private static let points = 72
    private static let metresPerDegreeLatitude = 111_320.0

    /// The ring as a closed polygon (flat-earth is plenty at a few hundred metres).
    var ring: [CLLocationCoordinate2D] {
        let dLat = radiusMeters / Self.metresPerDegreeLatitude
        let dLon = dLat / cos(center.latitude * .pi / 180)
        return (0...Self.points).map { i in
            let angle = 2 * Double.pi * Double(i % Self.points) / Double(Self.points)
            return CLLocationCoordinate2D(
                latitude: center.latitude + dLat * sin(angle),
                longitude: center.longitude + dLon * cos(angle))
        }
    }
}

/// A SwiftUI view pinned to a coordinate, centred on it. `id` must be unique on the map; a marker with
/// the same id in the next update moves rather than being redrawn. Higher `zIndex` draws on top.
struct LinzMapMarker: Identifiable {
    let id: String
    let coordinate: CLLocationCoordinate2D
    let zIndex: CGFloat
    let content: AnyView

    init<Content: View>(
        id: String, coordinate: CLLocationCoordinate2D, zIndex: CGFloat = 0,
        @ViewBuilder content: () -> Content
    ) {
        self.id = id
        self.coordinate = coordinate
        self.zIndex = zIndex
        self.content = AnyView(content())
    }
}

/// The LINZ topo map (MapLibre Native), with SwiftUI markers, an optional route line and an optional
/// join-range ring. North stays up and the map doesn't tilt: the pins' heading chevrons are drawn in
/// screen space, so they are only right on an unrotated map.
struct LinzMapView: UIViewRepresentable {
    var markers: [LinzMapMarker]
    var route: [CLLocationCoordinate2D] = []
    /// More lines drawn the same way as `route`, for a map that shows several people's routes.
    var extraRoutes: [[CLLocationCoordinate2D]] = []
    var joinRange: LinzJoinRange?
    var camera: LinzMapCamera
    /// Called when the person pans or zooms the map themselves.
    var onUserMovedCamera: () -> Void = {}
    /// Called with a marker's id when the person taps on or near it. The markers themselves take no
    /// touches (drags on a pin must still move the map), so the map finds the nearest one to the tap.
    var onMarkerTap: ((String) -> Void)?

    func makeCoordinator() -> Coordinator { Coordinator() }

    func makeUIView(context: Context) -> LayoutReportingMapView {
        let mapView = LayoutReportingMapView(frame: .zero, styleURL: LinzBasemap.styleURL)
        mapView.delegate = context.coordinator
        mapView.logoView.isHidden = true
        mapView.attributionButton.isHidden = true  // replaced by `LinzAttribution`
        mapView.compassView.isHidden = true
        mapView.isRotateEnabled = false
        mapView.isPitchEnabled = false
        mapView.showsUserLocation = false  // this device's own pin is a marker, like everyone else's
        mapView.setCenter(LinzBasemap.newZealand, zoomLevel: 4.5, animated: false)
        let coordinator = context.coordinator
        mapView.onLayout = { [weak coordinator] in coordinator?.applyCameraIfNeeded() }
        coordinator.mapView = mapView

        let tap = UITapGestureRecognizer(target: coordinator, action: #selector(Coordinator.handleTap(_:)))
        tap.delegate = coordinator
        // A double tap zooms; it must not also count as a tap on a pin.
        for recognizer in mapView.gestureRecognizers ?? [] {
            if let doubleTap = recognizer as? UITapGestureRecognizer, doubleTap.numberOfTapsRequired == 2 {
                tap.require(toFail: doubleTap)
            }
        }
        mapView.addGestureRecognizer(tap)
        return mapView
    }

    func updateUIView(_ mapView: LayoutReportingMapView, context: Context) {
        let coordinator = context.coordinator
        coordinator.onUserMovedCamera = onUserMovedCamera
        coordinator.onMarkerTap = onMarkerTap
        coordinator.update(markers: markers)
        coordinator.update(routes: [route] + extraRoutes, joinRange: joinRange)
        coordinator.requestedCamera = camera
        coordinator.applyCameraIfNeeded()
    }

    final class Coordinator: NSObject {
        weak var mapView: MLNMapView?
        var onUserMovedCamera: () -> Void = {}
        var onMarkerTap: ((String) -> Void)?
        var requestedCamera = LinzMapCamera()
        /// How far from a marker's centre a tap still counts as on it: about a pin's radius plus a margin.
        private static let tapRadius: CGFloat = 26

        private var appliedRevision = 0
        private var appliedRegion: LinzMapRegion?
        /// Set by a pan or zoom, so the next request is applied even if it asks for the same region again.
        private var movedByUser = false

        private var entries: [String: MarkerEntry] = [:]

        private var style: MLNStyle?
        private var routes: [[CLLocationCoordinate2D]] = []
        private var joinRange: LinzJoinRange?
        /// What the style's sources hold now; `shapesShown` is false until the first upload.
        private var shapesShown = false
        private var shownRoutes: [[CLLocationCoordinate2D]] = []
        private var shownJoinRange: LinzJoinRange?

        private static let routeSourceID = "route-source"
        private static let routeLayerID = "route-line"
        private static let joinRangeSourceID = "join-range-source"
        private static let joinRangeFillID = "join-range-fill"
        private static let joinRangeLineID = "join-range-line"
        /// Room for the largest marker (a 36 pt pin plus its badge), centred on the coordinate.
        private static let markerSize = CGSize(width: 64, height: 64)

        // MARK: Camera

        func applyCameraIfNeeded() {
            guard let mapView, let region = requestedCamera.region,
                  requestedCamera.revision != appliedRevision,
                  mapView.bounds.width > 0, mapView.bounds.height > 0
            else { return }
            appliedRevision = requestedCamera.revision
            // The per-second re-frame usually asks for where the camera already is.
            if region == appliedRegion && !movedByUser { return }
            let isFirst = appliedRegion == nil
            appliedRegion = region
            movedByUser = false
            let camera = mapView.cameraThatFitsCoordinateBounds(region.bounds)
            if isFirst {
                mapView.setCamera(camera, animated: false)
            } else {
                mapView.setCamera(
                    camera, withDuration: 0.6,
                    animationTimingFunction: CAMediaTimingFunction(name: .easeInEaseOut))
            }
        }

        // MARK: Markers

        private struct MarkerEntry {
            let annotation: MLNPointAnnotation
            let host: UIHostingController<AnyView>
            let view: MLNAnnotationView
        }

        /// Marker id by annotation, for handing the map each annotation's view.
        private var markerIDs: [ObjectIdentifier: String] = [:]

        func update(markers: [LinzMapMarker]) {
            guard let mapView else { return }
            var seen = Set<String>()
            var added: [MLNPointAnnotation] = []
            for marker in markers {
                guard seen.insert(marker.id).inserted else { continue }
                if let entry = entries[marker.id] {
                    let old = entry.annotation.coordinate
                    if old.latitude != marker.coordinate.latitude || old.longitude != marker.coordinate.longitude {
                        entry.annotation.coordinate = marker.coordinate
                    }
                    entry.host.rootView = marker.content
                    entry.view.layer.zPosition = marker.zIndex
                } else {
                    let annotation = MLNPointAnnotation()
                    annotation.coordinate = marker.coordinate
                    markerIDs[ObjectIdentifier(annotation)] = marker.id

                    let host = UIHostingController(rootView: marker.content)
                    host.safeAreaRegions = []  // a pin under the notch must not be pushed down
                    host.view.backgroundColor = .clear
                    host.view.isUserInteractionEnabled = false
                    host.view.frame = CGRect(origin: .zero, size: Self.markerSize)
                    host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]

                    let view = MLNAnnotationView(annotation: annotation, reuseIdentifier: marker.id)
                    view.frame = CGRect(origin: .zero, size: Self.markerSize)
                    view.isEnabled = false  // nothing to select or call out
                    view.isUserInteractionEnabled = false  // drags on a pin still move the map
                    view.layer.zPosition = marker.zIndex
                    view.addSubview(host.view)

                    entries[marker.id] = MarkerEntry(annotation: annotation, host: host, view: view)
                    added.append(annotation)
                }
            }
            let gone = entries.keys.filter { !seen.contains($0) }
            if !gone.isEmpty {
                let removed = gone.compactMap { entries[$0]?.annotation }
                mapView.removeAnnotations(removed)
                removed.forEach { markerIDs[ObjectIdentifier($0)] = nil }
                gone.forEach { entries[$0] = nil }
            }
            if !added.isEmpty { mapView.addAnnotations(added) }
        }

        /// The marker nearest the tap within `tapRadius`; a marker drawn on top (higher `zIndex`) wins over
        /// one underneath, so a runner's pin beats a grey dot beside it.
        @objc func handleTap(_ recognizer: UITapGestureRecognizer) {
            guard recognizer.state == .ended, let mapView, let onMarkerTap else { return }
            let tapPoint = recognizer.location(in: mapView)
            var best: (id: String, zIndex: CGFloat, distance: CGFloat)?
            for (id, entry) in entries {
                let point = mapView.convert(entry.annotation.coordinate, toPointTo: mapView)
                let distance = hypot(point.x - tapPoint.x, point.y - tapPoint.y)
                guard distance <= Self.tapRadius else { continue }
                let zIndex = entry.view.layer.zPosition
                if let current = best,
                   current.zIndex > zIndex || (current.zIndex == zIndex && current.distance <= distance) {
                    continue
                }
                best = (id, zIndex, distance)
            }
            if let best { onMarkerTap(best.id) }
        }

        fileprivate func annotationView(for annotation: MLNAnnotation) -> MLNAnnotationView? {
            guard let id = markerIDs[ObjectIdentifier(annotation)] else { return nil }
            return entries[id]?.view
        }

        // MARK: Route and join range

        func update(routes: [[CLLocationCoordinate2D]], joinRange: LinzJoinRange?) {
            // A line needs two points; empty ones (no route loaded) are dropped so they compare equal.
            self.routes = routes.filter { $0.count > 1 }
            self.joinRange = joinRange
            applyShapes()
        }

        fileprivate func styleDidLoad(_ style: MLNStyle) {
            let routeSource = MLNShapeSource(identifier: Self.routeSourceID, shape: nil, options: nil)
            style.addSource(routeSource)
            let routeLine = MLNLineStyleLayer(identifier: Self.routeLayerID, source: routeSource)
            routeLine.lineColor = NSExpression(forConstantValue: UIColor(red: 252 / 255, green: 76 / 255, blue: 2 / 255, alpha: 1)) // Strava orange
            routeLine.lineOpacity = NSExpression(forConstantValue: 0.9)
            routeLine.lineWidth = NSExpression(forConstantValue: 5)
            routeLine.lineCap = NSExpression(forConstantValue: "round")
            routeLine.lineJoin = NSExpression(forConstantValue: "round")
            style.addLayer(routeLine)

            let rangeSource = MLNShapeSource(identifier: Self.joinRangeSourceID, shape: nil, options: nil)
            style.addSource(rangeSource)
            let rangeFill = MLNFillStyleLayer(identifier: Self.joinRangeFillID, source: rangeSource)
            rangeFill.fillColor = NSExpression(forConstantValue: UIColor.black)
            rangeFill.fillOpacity = NSExpression(forConstantValue: 0.04)
            style.addLayer(rangeFill)
            let rangeLine = MLNLineStyleLayer(identifier: Self.joinRangeLineID, source: rangeSource)
            rangeLine.lineColor = NSExpression(forConstantValue: UIColor.black)
            rangeLine.lineWidth = NSExpression(forConstantValue: 1)
            rangeLine.lineDashPattern = NSExpression(forConstantValue: [5, 5])
            style.addLayer(rangeLine)

            self.style = style
            shapesShown = false
            applyShapes()
        }

        /// Only re-uploads a shape when it changed: this runs on every per-second update.
        private func applyShapes() {
            guard let style else { return }
            let first = !shapesShown
            shapesShown = true
            if first || !Self.same(shownRoutes, routes) {
                shownRoutes = routes
                let source = style.source(withIdentifier: Self.routeSourceID) as? MLNShapeSource
                var lines = routes.map { line in
                    MLNPolylineFeature(coordinates: line, count: UInt(line.count))
                }
                switch lines.count {
                case 0: source?.shape = nil
                case 1: source?.shape = lines.removeFirst()
                default: source?.shape = MLNShapeCollectionFeature(shapes: lines)
                }
            }
            if first || shownJoinRange != joinRange {
                shownJoinRange = joinRange
                let source = style.source(withIdentifier: Self.joinRangeSourceID) as? MLNShapeSource
                if let ring = joinRange?.ring {
                    source?.shape = MLNPolygonFeature(coordinates: ring, count: UInt(ring.count))
                } else {
                    source?.shape = nil
                }
            }
        }

        private static func same(_ a: [[CLLocationCoordinate2D]], _ b: [[CLLocationCoordinate2D]]) -> Bool {
            a.count == b.count && zip(a, b).allSatisfy { line, other in
                line.count == other.count
                    && zip(line, other).allSatisfy { $0.latitude == $1.latitude && $0.longitude == $1.longitude }
            }
        }

        // MARK: Gestures

        fileprivate func regionWillChange(reason: MLNCameraChangeReason) {
            let gestures: MLNCameraChangeReason = [
                .gesturePan, .gesturePinch, .gestureRotate, .gestureZoomIn, .gestureZoomOut,
                .gestureOneFingerZoom, .gestureTilt,
            ]
            guard !reason.isDisjoint(with: gestures) else { return }
            movedByUser = true
            onUserMovedCamera()
        }
    }
}

extension LinzMapView.Coordinator: @preconcurrency MLNMapViewDelegate {
    func mapView(_ mapView: MLNMapView, didFinishLoading style: MLNStyle) {
        styleDidLoad(style)
    }

    func mapView(_ mapView: MLNMapView, viewFor annotation: MLNAnnotation) -> MLNAnnotationView? {
        annotationView(for: annotation)
    }

    func mapView(_ mapView: MLNMapView, annotationCanShowCallout annotation: MLNAnnotation) -> Bool {
        false
    }

    func mapView(_ mapView: MLNMapView, regionWillChangeWith reason: MLNCameraChangeReason, animated: Bool) {
        regionWillChange(reason: reason)
    }
}

extension LinzMapView.Coordinator: UIGestureRecognizerDelegate {
    /// The pin tap runs alongside the map's own single-tap handling rather than replacing it.
    func gestureRecognizer(
        _ gestureRecognizer: UIGestureRecognizer,
        shouldRecognizeSimultaneouslyWith otherGestureRecognizer: UIGestureRecognizer
    ) -> Bool {
        true
    }
}

/// An `MLNMapView` that says when it is laid out, so a camera requested before the map had a size (on
/// appear) is applied as soon as it has one.
final class LayoutReportingMapView: MLNMapView {
    var onLayout: (() -> Void)?

    override func layoutSubviews() {
        super.layoutSubviews()
        onLayout?()
    }
}
