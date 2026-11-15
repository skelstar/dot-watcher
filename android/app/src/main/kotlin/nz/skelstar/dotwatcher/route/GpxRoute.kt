package nz.skelstar.dotwatcher.route

import org.xml.sax.Attributes
import org.xml.sax.helpers.DefaultHandler
import java.io.ByteArrayInputStream
import javax.xml.parsers.SAXParserFactory

/** One point on a GPX route. */
data class RoutePoint(val latitude: Double, val longitude: Double)

/** The server refuses anything larger (SessionStore.CasualRouteMaxLength), so a bigger file is turned
 *  away before it is read into memory and sent. */
const val ROUTE_MAX_BYTES = 5_000_000

/**
 * Pulls the track points (`<trkpt lat="…" lon="…">`) out of a GPX document: the same points iOS
 * (GpxRouteParser.swift) and the web client (client/src/gpx.ts) draw. Waypoints and route points are
 * ignored, and a namespace prefix (`<gpx:trkpt>`) is tolerated. Anything that isn't readable XML gives
 * an empty list, as does a file with no track.
 */
fun parseGpxTrack(bytes: ByteArray): List<RoutePoint> {
    val points = mutableListOf<RoutePoint>()
    val handler = object : DefaultHandler() {
        override fun startElement(uri: String?, localName: String?, qName: String?, attributes: Attributes) {
            val name = qName?.takeIf { it.isNotEmpty() } ?: localName.orEmpty()
            if (name != "trkpt" && !name.endsWith(":trkpt")) return
            val lat = attributes.getValue("lat")?.toDoubleOrNull() ?: return
            val lon = attributes.getValue("lon")?.toDoubleOrNull() ?: return
            if (lat.isFinite() && lon.isFinite()) points += RoutePoint(lat, lon)
        }
    }
    return runCatching {
        val factory = SAXParserFactory.newInstance().apply { isNamespaceAware = false }
        // A GPX file never needs a DTD or external entities; refuse them where the parser lets us.
        runCatching { factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
        runCatching { factory.setFeature("http://xml.org/sax/features/external-general-entities", false) }
        runCatching { factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false) }
        factory.newSAXParser().parse(ByteArrayInputStream(bytes), handler)
        points.toList()
    }.getOrElse { emptyList() }
}

/** A route only draws as a line with two or more points. */
fun List<RoutePoint>.isDrawable(): Boolean = size > 1

/** The message for a file that has no track, the same words as iOS. */
const val NO_TRACK_MESSAGE = "No route found. This GPX file has no track points."
