package nz.skelstar.dotwatcher.route

import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.network.CasualLiveResponse
import nz.skelstar.dotwatcher.network.CasualRouteRef
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GpxRouteTest {
    private fun gpx(body: String) = """<?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="test" xmlns="http://www.topografix.com/GPX/1/1">$body</gpx>""".toByteArray()

    @Test fun `reads track points in order across segments`() {
        val points = parseGpxTrack(gpx("""
            <trk><trkseg>
              <trkpt lat="-41.29" lon="174.78"><ele>10</ele></trkpt>
              <trkpt lat="-41.30" lon="174.79"/>
            </trkseg><trkseg>
              <trkpt lon="174.80" lat="-41.31"/>
            </trkseg></trk>"""))

        assertEquals(
            listOf(RoutePoint(-41.29, 174.78), RoutePoint(-41.30, 174.79), RoutePoint(-41.31, 174.80)),
            points,
        )
        assertTrue(points.isDrawable())
    }

    @Test fun `ignores waypoints, route points and bad coordinates`() {
        val points = parseGpxTrack(gpx("""
            <wpt lat="1" lon="2"/>
            <rte><rtept lat="3" lon="4"/></rte>
            <trk><trkseg>
              <trkpt lat="abc" lon="174.78"/>
              <trkpt lat="-41.30"/>
              <trkpt lat="NaN" lon="174.79"/>
              <trkpt lat="-41.31" lon="174.80"/>
            </trkseg></trk>"""))

        assertEquals(listOf(RoutePoint(-41.31, 174.80)), points)
        assertFalse(points.isDrawable())
    }

    @Test fun `tolerates a namespace prefix`() {
        val bytes = """<?xml version="1.0"?>
            <gpx:gpx xmlns:gpx="http://www.topografix.com/GPX/1/1"><gpx:trk><gpx:trkseg>
              <gpx:trkpt lat="1.5" lon="2.5"/><gpx:trkpt lat="3.5" lon="4.5"/>
            </gpx:trkseg></gpx:trk></gpx:gpx>""".toByteArray()

        assertEquals(listOf(RoutePoint(1.5, 2.5), RoutePoint(3.5, 4.5)), parseGpxTrack(bytes))
    }

    @Test fun `anything that is not XML gives no points`() {
        assertTrue(parseGpxTrack("not a gpx file".toByteArray()).isEmpty())
        assertTrue(parseGpxTrack(ByteArray(0)).isEmpty())
    }

    @Test fun `the cache fetches new routes, skips unchanged ones and forgets removed ones`() = runBlocking {
        val cache = CasualRouteCache()
        val fetched = mutableListOf<String>()
        val file = gpx("""<trk><trkseg><trkpt lat="1" lon="2"/><trkpt lat="3" lon="4"/></trkseg></trk>""")
        val fetch: suspend (String) -> ByteArray? = { runId -> fetched += runId; file }

        cache.sync(listOf(CasualRouteRef("AL", "run-1", "t1"), CasualRouteRef("Bo", "run-2", "t1")), fetch)
        assertEquals(listOf("run-1", "run-2"), fetched)
        assertEquals(2, cache.lines.size)

        // Same uploadedAt: not fetched again. A new uploadedAt: fetched again. Gone: forgotten.
        fetched.clear()
        cache.sync(listOf(CasualRouteRef("AL", "run-1", "t2")), fetch)
        assertEquals(listOf("run-1"), fetched)
        assertEquals(1, cache.lines.size)
    }

    @Test fun `the cache leaves out a route that fails to download or has no track, and retries it`() = runBlocking {
        val cache = CasualRouteCache()
        val refs = listOf(CasualRouteRef("AL", "run-1", "t1"))

        cache.sync(refs) { null }
        assertTrue(cache.lines.isEmpty())

        cache.sync(refs) { gpx("<trk><trkseg><trkpt lat=\"1\" lon=\"2\"/></trkseg></trk>") }
        assertTrue(cache.lines.isEmpty())

        cache.sync(refs) { gpx("<trk><trkseg><trkpt lat=\"1\" lon=\"2\"/><trkpt lat=\"3\" lon=\"4\"/></trkseg></trk>") }
        assertEquals(1, cache.lines.size)
    }

    @Test fun `the live response reads route references, and their absence`() {
        val json = Json { ignoreUnknownKeys = true }
        val live = json.decodeFromString<CasualLiveResponse>("""
            {"participants":[],"positions":[],"nearby":[],
             "routes":[{"runnerName":"AL","runId":"r1","uploadedAt":"2026-11-15T09:00:00+00:00"}]}
        """)
        assertEquals(listOf(CasualRouteRef("AL", "r1", "2026-11-15T09:00:00+00:00")), live.routes)

        assertNull(json.decodeFromString<CasualLiveResponse>("""{"participants":[],"positions":[]}""").routes)
    }
}
