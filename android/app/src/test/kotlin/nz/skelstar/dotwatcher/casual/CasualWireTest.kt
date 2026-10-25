package nz.skelstar.dotwatcher.casual

import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.location.CASUAL_SESSION_ID
import nz.skelstar.dotwatcher.network.CasualLiveResponse
import nz.skelstar.dotwatcher.network.CasualSettings
import nz.skelstar.dotwatcher.network.CasualWatchLinkResponse
import nz.skelstar.dotwatcher.network.LocationPostResponse
import nz.skelstar.dotwatcher.network.UpdateCasualSettingsRequest
import nz.skelstar.dotwatcher.ui.casual.crewOutLine
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The server's casual JSON as the app reads it. The sample is what POST /location and GET
 * /casual/live return (server/Models/CasualModels.cs), field for field, including the fields this
 * app has no use for (it must ignore them), so a renamed or removed field fails here.
 */
class CasualWireTest {
    private val json = Json { ignoreUnknownKeys = true }

    private val liveJson = """
        {
          "participants": ["AL", "Bobby"],
          "positions": [
            [{"runnerName":"AL","latitude":-41.17,"longitude":174.7762,"heading":90.0,
              "timestamp":"2026-10-24T09:00:00+00:00","nextExpectedAt":"2026-10-24T09:00:15+00:00",
              "isUltraConstrained":false,"batteryLevel":80,"inSession":false,"isSelf":true}],
            [{"runnerName":"Bobby","latitude":-41.16,"longitude":174.78,"heading":null,
              "timestamp":"2026-10-24T09:00:01+00:00","nextExpectedAt":null,
              "isUltraConstrained":true,"batteryLevel":null,"inSession":true,"isSelf":false}]
          ],
          "nearby": [
            {"latitude":-41.15,"longitude":174.79,"heading":180.0,"inSession":true},
            {"latitude":-41.14,"longitude":174.80,"heading":null,"inSession":false}
          ]
        }
    """.trimIndent()

    @Test fun `the live response reads positions, the lock flags and anonymous dots`() {
        val live = json.decodeFromString<CasualLiveResponse>(liveJson)

        assertEquals(listOf("AL", "Bobby"), live.participants)
        val self = live.positions[0][0]
        assertTrue(self.isSelf)
        assertFalse(self.inSession)
        val bobby = live.positions[1][0]
        assertFalse(bobby.isSelf)
        assertTrue(bobby.inSession)
        assertTrue(bobby.isUltraConstrained)
        assertNull(bobby.heading)
        assertEquals(2, live.nearby.size)
        assertTrue(live.nearby[0].inSession)
        assertFalse(live.nearby[1].inSession)
    }

    @Test fun `a casual post response still reads as a plain post response`() {
        // The tracking service decodes POST /location's body as LocationPostResponse whichever
        // session it posted to; the casual one has extra fields and must not break that.
        val response = json.decodeFromString<LocationPostResponse>(liveJson)

        assertEquals(2, response.positions.size)
        assertEquals("AL", response.positions[0][0].runnerName)
    }

    @Test fun `an older server's answer without the new fields still reads`() {
        val live = json.decodeFromString<CasualLiveResponse>("""{"participants":[],"positions":[]}""")

        assertTrue(live.nearby.isEmpty())
    }

    @Test fun `settings round trip the one field the server expects`() {
        assertEquals(true, json.decodeFromString<CasualSettings>("""{"visibleToNonCrew":true}""").visibleToNonCrew)
        assertEquals(false, json.decodeFromString<CasualSettings>("{}").visibleToNonCrew)
        assertEquals(
            """{"visibleToNonCrew":true}""",
            json.encodeToString(UpdateCasualSettingsRequest.serializer(), UpdateCasualSettingsRequest(true)),
        )
    }

    @Test fun `the casual session id matches the server's fixed one`() {
        assertEquals("ca5ca100-0000-4000-8000-000000000001", CASUAL_SESSION_ID)
    }

    @Test fun `the crew line says who is out, or that nobody is`() {
        assertNull(crewOutLine(null))
        assertEquals("No crew out right now. They'll show up here.", crewOutLine(0))
        assertEquals("1 of your crew is out now", crewOutLine(1))
        assertEquals("3 of your crew are out now", crewOutLine(3))
    }

    @Test fun `a watch link response reads, and joins the web base URL to its path`() {
        val link = json.decodeFromString<CasualWatchLinkResponse>(
            """{"token":"Qm7K2x9FabcdefghijKLmn","path":"/watch/Qm7K2x9FabcdefghijKLmn"}""",
        )

        assertEquals("Qm7K2x9FabcdefghijKLmn", link.token)
        assertEquals(
            "https://dot-watcher.skelstar.io/watch/Qm7K2x9FabcdefghijKLmn",
            watchLinkUrl("https://dot-watcher.skelstar.io", link.path),
        )
        assertEquals(
            "https://dot-watcher.skelstar.io/watch/x",
            watchLinkUrl("https://dot-watcher.skelstar.io/", "/watch/x"),
        )
    }
}
