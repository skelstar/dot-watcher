import CoreLocation
import Foundation

/// Pulls the track points (`<trkpt lat="…" lon="…">`) out of a GPX document — the same points the
/// web client draws (see `client/src/gpx.ts`). Waypoints and route points are ignored.
final class GpxRouteParser: NSObject, XMLParserDelegate {
    private var coordinates: [CLLocationCoordinate2D] = []

    static func parse(_ data: Data) -> [CLLocationCoordinate2D] {
        let delegate = GpxRouteParser()
        let parser = XMLParser(data: data)
        parser.delegate = delegate
        guard parser.parse() else { return [] }
        return delegate.coordinates
    }

    func parser(
        _ parser: XMLParser,
        didStartElement elementName: String,
        namespaceURI: String?,
        qualifiedName qName: String?,
        attributes attributeDict: [String: String] = [:]
    ) {
        // Tolerate a namespace prefix (`<gpx:trkpt>`), like the web parser does.
        guard elementName == "trkpt" || elementName.hasSuffix(":trkpt"),
              let lat = attributeDict["lat"].flatMap(Double.init),
              let lon = attributeDict["lon"].flatMap(Double.init),
              lat.isFinite, lon.isFinite
        else { return }
        coordinates.append(CLLocationCoordinate2D(latitude: lat, longitude: lon))
    }
}
