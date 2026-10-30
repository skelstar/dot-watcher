package nz.skelstar.dotwatcher.nearby

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.events.EventsUiState
import nz.skelstar.dotwatcher.location.Fix
import nz.skelstar.dotwatcher.network.ApiErrorResponse
import nz.skelstar.dotwatcher.network.CreateSessionRequest
import nz.skelstar.dotwatcher.network.JoinNearbyRequest
import nz.skelstar.dotwatcher.network.NearbyEvent
import nz.skelstar.dotwatcher.network.NearbyStatus
import nz.skelstar.dotwatcher.network.UpdateNearbyRequest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The server's nearby-join JSON as the app reads and writes it (server/Models/EventModels.cs, AuthModels.cs,
 * SessionsController.cs), so a renamed or removed field fails here. Uses the same Json settings as the app's
 * Retrofit client: unknown keys ignored, defaults not written.
 */
class NearbyWireTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test fun `a nearby list entry carries the session id, the placeholder does not`() {
        val entries = json.decodeFromString<List<NearbyEvent>>(
            """
            [
              {"id":"session-abc","eyebrow":"Happening near you · 100 m","title":"Sunday long run",
               "subtitle":"Organised by Alice","sessionId":"abc"},
              {"id":"placeholder-rogaine","eyebrow":"Happening near you · 2.3 km","title":"Rogaine event",
               "subtitle":"Sat 9am"}
            ]
            """.trimIndent()
        )

        assertEquals("abc", entries[0].sessionId)
        assertNull(entries[1].sessionId)
        assertEquals(listOf(entries[0]), EventsUiState(events = entries).joinable)
    }

    @Test fun `creating an ordinary session sends none of the nearby fields`() {
        val body = json.encodeToString(CreateSessionRequest(sessionName = "SUNSET23"))

        assertEquals("""{"sessionName":"SUNSET23"}""", body)
    }

    @Test fun `creating a nearby session sends the title, range and position`() {
        val request = CreateSessionRequest(
            sessionName = "Sunday long run",
            nearby = true,
            nearbyRadiusMeters = 250,
            latitude = -36.85,
            longitude = 174.76,
            accuracy = 8.0,
        )

        val sent = json.parseToJsonElementMap(json.encodeToString(request))

        assertEquals("Sunday long run", sent["sessionName"])
        assertEquals("true", sent["nearby"])
        assertEquals("250", sent["nearbyRadiusMeters"])
        assertEquals("-36.85", sent["latitude"])
        assertEquals("174.76", sent["longitude"])
        assertEquals("8.0", sent["accuracy"])
    }

    @Test fun `switching nearby off sends no position`() {
        val body = json.encodeToString(UpdateNearbyRequest(enabled = false))

        assertEquals("""{"enabled":false}""", body)
    }

    @Test fun `joining sends the position and leaves the name to the server when absent`() {
        val body = json.encodeToString(JoinNearbyRequest(latitude = -36.85, longitude = 174.76, accuracy = 5.0))

        assertEquals("""{"latitude":-36.85,"longitude":174.76,"accuracy":5.0}""", body)
    }

    @Test fun `the ping response reads the switch and range`() {
        val status = json.decodeFromString<NearbyStatus>("""{"nearby":true,"radiusMeters":100}""")

        assertEquals(NearbyStatus(nearby = true, radiusMeters = 100), status)
    }

    @Test fun `the one-active-session conflict body carries the existing session id`() {
        val error = json.decodeFromString<ApiErrorResponse>(
            """{"error":"You already have an active session. End it to create another.","sessionId":"abc"}"""
        )

        assertEquals("abc", error.sessionId)
        assertEquals("You already have an active session. End it to create another.", error.error)
    }

    @Test fun `an ordinary error body has no session id`() {
        assertNull(json.decodeFromString<ApiErrorResponse>("""{"error":"Nope."}""").sessionId)
    }

    @Test fun `only a known accuracy of 100 m or better can place a join location`() {
        assertTrue(Fix(-36.85, 174.76, 8.0).isUsableForJoinLocation())
        assertTrue(Fix(-36.85, 174.76, 100.0).isUsableForJoinLocation())
        assertFalse(Fix(-36.85, 174.76, 100.1).isUsableForJoinLocation())
        assertFalse(Fix(-36.85, 174.76, null).isUsableForJoinLocation())
    }

    @Test fun `the range choices are the four the server accepts`() {
        assertEquals(listOf(50, 100, 250, 500), NEARBY_RADIUS_OPTIONS)
        assertTrue(NEARBY_DEFAULT_RADIUS_METRES in NEARBY_RADIUS_OPTIONS)
    }

    @Test fun `a title is trimmed before it is sent`() {
        assertEquals("Sunday long run", NearbyDraft(title = "  Sunday long run ").trimmedTitle)
    }

    /** Field name to its JSON text, for asserting on a few fields without depending on their order. */
    private fun Json.parseToJsonElementMap(text: String): Map<String, String> =
        (parseToJsonElement(text) as kotlinx.serialization.json.JsonObject).mapValues { (_, value) ->
            (value as kotlinx.serialization.json.JsonPrimitive).content
        }
}
