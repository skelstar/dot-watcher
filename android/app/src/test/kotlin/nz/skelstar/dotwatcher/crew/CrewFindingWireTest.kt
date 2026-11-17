package nz.skelstar.dotwatcher.crew

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.network.CrewFindingRequest
import nz.skelstar.dotwatcher.network.CrewFindingState
import nz.skelstar.dotwatcher.ui.crew.countdown
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The server's "Find new crew" JSON as the app reads it: what POST/PUT /me/crew/finding and the accept call
 * return (server/Models/AuthModels.cs, CrewFindingState), field for field, so a renamed or removed field
 * fails here.
 */
class CrewFindingWireTest {
    private val json = Json { ignoreUnknownKeys = true }

    private val stateJson = """
        {
          "expiresAt": "2026-11-18T07:44:12.1234567+00:00",
          "people": [
            {"id":"9f2c1a","nickname":null,"displayName":"TW","youAccepted":false,"acceptedYou":false},
            {"id":"77ab03","nickname":"Sam K","displayName":"SK","youAccepted":true,"acceptedYou":true}
          ],
          "added": [
            {"userId":"u-priya","nickname":"Priya","displayName":"PR","since":"2026-11-18T07:43:50.0000000+00:00"}
          ]
        }
    """.trimIndent()

    @Test fun `the state reads people, their accept flags and who was added`() {
        val state = json.decodeFromString<CrewFindingState>(stateJson)

        assertEquals(2, state.people.size)
        val tom = state.people[0]
        assertEquals("9f2c1a", tom.id)
        assertNull(tom.nickname)
        assertEquals("TW", tom.label())
        assertFalse(tom.youAccepted)
        assertFalse(tom.acceptedYou)
        val sam = state.people[1]
        assertEquals("Sam K", sam.label())
        assertTrue(sam.youAccepted)
        assertTrue(sam.acceptedYou)
        assertEquals("u-priya", state.added.single().userId)
    }

    @Test fun `the server's expiry, with seven decimal places, parses`() {
        val state = json.decodeFromString<CrewFindingState>(stateJson)
        val millis = java.time.OffsetDateTime.parse(state.expiresAt).toInstant().toEpochMilli()
        assertEquals(java.time.Instant.parse("2026-11-18T07:44:12.123Z").toEpochMilli(), millis)
    }

    @Test fun `an empty window reads`() {
        val state = json.decodeFromString<CrewFindingState>("""{"expiresAt":"2026-11-18T07:44:12+00:00","people":[],"added":[]}""")
        assertTrue(state.people.isEmpty())
        assertTrue(state.added.isEmpty())
    }

    @Test fun `the request sends the field names the server binds`() {
        val body = Json.encodeToString(CrewFindingRequest(latitude = -41.2865, longitude = 174.7762, accuracy = 8.0))
        assertTrue(body.contains("\"latitude\":-41.2865"))
        assertTrue(body.contains("\"longitude\":174.7762"))
        assertTrue(body.contains("\"accuracy\":8.0"))
    }

    @Test fun `the countdown rounds up to whole seconds`() {
        assertEquals("2:00", countdown(120_000))
        assertEquals("1:48", countdown(107_200))
        assertEquals("0:01", countdown(1))
        assertEquals("0:00", countdown(-5_000))
    }
}
