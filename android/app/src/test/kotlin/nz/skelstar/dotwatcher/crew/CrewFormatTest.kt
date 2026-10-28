package nz.skelstar.dotwatcher.crew

import nz.skelstar.dotwatcher.network.CrewCodePreview
import nz.skelstar.dotwatcher.network.CrewMember
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneId

class CrewFormatTest {
    private val utc = ZoneId.of("UTC")
    private val now = Instant.parse("2026-10-23T00:00:00Z")

    private fun member(id: String, nickname: String? = null, initials: String = "AB") =
        CrewMember(userId = id, nickname = nickname, displayName = initials, since = "2026-03-04T10:00:00.1234567+00:00")

    // --- Names ---------------------------------------------------------------------------

    @Test fun `a member is shown by nickname, else by initials`() {
        assertEquals("Priya", member("1", nickname = "Priya", initials = "PR").label())
        assertEquals("PR", member("1", nickname = null, initials = "PR").label())
        assertEquals("PR", member("1", nickname = "   ", initials = "PR").label())
    }

    @Test fun `a code owner is shown by nickname, else by initials`() {
        assertEquals("Priya", CrewCodePreview("1", "Priya", "PR").label())
        assertEquals("PR", CrewCodePreview("1", null, "PR").label())
    }

    // --- The start-screen row ------------------------------------------------------------

    @Test fun `no crew shows the invitation to add people`() {
        val summary = crewRowSummary(emptyList())
        assertTrue(summary.faces.isEmpty())
        assertEquals("Your crew", summary.title)
        assertEquals("Add people that you know", summary.meta)
    }

    @Test fun `a crew row with people has no count line`() {
        assertEquals("", crewRowSummary(listOf(member("1"))).meta)
    }

    @Test fun `up to four faces are shown with no extra`() {
        val summary = crewRowSummary((1..4).map { member("$it") })
        assertEquals(4, summary.faces.size)
        assertEquals(0, summary.extra)
    }

    @Test fun `more than four collapse into a plus count and keep the order given`() {
        val summary = crewRowSummary((1..7).map { member("$it") })
        assertEquals(listOf("1", "2", "3", "4"), summary.faces.map { it.userId })
        assertEquals(3, summary.extra)
    }

    // --- Dates ---------------------------------------------------------------------------

    @Test fun `since drops the year for this year`() {
        assertEquals("since March", crewSinceLabel("2026-03-04T10:00:00.1234567+00:00", now, utc))
    }

    @Test fun `since includes the year for earlier years`() {
        assertEquals("since March 2025", crewSinceLabel("2025-03-04T10:00:00Z", now, utc))
    }

    @Test fun `since copes with an unreadable timestamp`() {
        assertEquals("in your crew", crewSinceLabel("garbage", now, utc))
    }

    @Test fun `expiry counts hours then minutes`() {
        assertEquals("Expires in 23 h", crewExpiryLabel("2026-10-23T23:30:00.0000000+00:00", now))
        assertEquals("Expires in 40 min", crewExpiryLabel("2026-10-23T00:40:00Z", now))
        assertEquals("Expires in 1 min", crewExpiryLabel("2026-10-23T00:00:10Z", now))
    }

    @Test fun `an expired code says so`() {
        assertEquals("Expired", crewExpiryLabel("2026-10-22T23:59:00Z", now))
        assertEquals("Expired", crewExpiryLabel("2026-10-23T00:00:00Z", now))
        assertEquals("", crewExpiryLabel("garbage", now))
    }

    // --- Messages ------------------------------------------------------------------------

    @Test fun `server answers are turned into plain words`() {
        assertEquals("That's your own crew code.", crewCodeFailureMessage(400))
        assertTrue(crewCodeFailureMessage(404).contains("already been used or has expired"))
        assertTrue(crewCodeFailureMessage(409).contains("full"))
        assertTrue(crewCodeFailureMessage(429).contains("Too many attempts"))
        assertTrue(crewCodeFailureMessage(500).contains("500"))
    }

    // --- Nicknames -----------------------------------------------------------------------

    @Test fun `a nickname is trimmed and blank clears it`() {
        assertEquals("Priya", cleanNickname("  Priya "))
        assertEquals(null, cleanNickname("   "))
        assertEquals(null, cleanNickname(""))
    }

    @Test fun `nickname limits match the server`() {
        assertTrue(isValidNickname(""))
        assertTrue(isValidNickname("Priya"))
        assertTrue(isValidNickname("x".repeat(MAX_NICKNAME_LENGTH)))
        assertFalse(isValidNickname("x".repeat(MAX_NICKNAME_LENGTH + 1)))
        assertFalse(isValidNickname("two\nlines"))
        assertFalse(isValidNickname("tab\there"))
    }
}
