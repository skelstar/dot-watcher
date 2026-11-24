package nz.skelstar.dotwatcher.groups

import nz.skelstar.dotwatcher.nearby.NearbyDraft
import nz.skelstar.dotwatcher.nearby.cleanEntryCode
import nz.skelstar.dotwatcher.nearby.isValidEntryCode
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneId

class GroupTimeTest {
    private val now = Instant.parse("2026-11-24T20:12:00Z")
    private val auckland = ZoneId.of("Pacific/Auckland") // UTC+13 in November

    @Test fun `hours and minutes left, ending at local time`() {
        assertEquals(GroupTime("12:00", "2 h 48 m left"), groupTime("2026-11-24T23:00:00Z", now, auckland))
    }

    @Test fun `whole hours drop the minutes`() {
        assertEquals("3 h left", groupTime("2026-11-24T23:12:00Z", now, auckland)?.left)
    }

    @Test fun `under an hour is minutes`() {
        assertEquals("35 m left", groupTime("2026-11-24T20:47:00Z", now, auckland)?.left)
    }

    @Test fun `the last minute says ending now`() {
        assertEquals("Ending now", groupTime("2026-11-24T20:12:30Z", now, auckland)?.left)
    }

    @Test fun `ended, missing or unreadable shows nothing`() {
        assertNull(groupTime("2026-11-24T20:12:00Z", now, auckland))
        assertNull(groupTime("2026-11-24T19:00:00Z", now, auckland))
        assertNull(groupTime(null, now, auckland))
        assertNull(groupTime("tomorrow", now, auckland))
    }

    @Test fun `server times with fractional seconds and an offset are read`() {
        assertEquals("12:00", groupTime("2026-11-24T23:00:00.0000000+00:00", now, auckland)?.endsAt)
    }

    @Test fun `entry codes are cleaned and checked the way the server does`() {
        assertEquals("K7Q4", cleanEntryCode(" k7-q4 "))
        assertEquals("ABCDEFGH", cleanEntryCode("abcdefghij"))
        assertTrue(isValidEntryCode("k7q4"))
        assertFalse(isValidEntryCode("K7Q"))
        assertFalse(isValidEntryCode("KÖQ4"))
    }

    @Test fun `a draft only sends a code when asking for one`() {
        assertNull(NearbyDraft(askForCode = false, entryCode = "K7Q4").entryCodeToSend)
        assertEquals("K7Q4", NearbyDraft(askForCode = true, entryCode = " K7Q4 ").entryCodeToSend)
        assertFalse(NearbyDraft(askForCode = true, entryCode = "K7").isEntryCodeValid)
        assertTrue(NearbyDraft(askForCode = false, entryCode = "").isEntryCodeValid)
    }
}
