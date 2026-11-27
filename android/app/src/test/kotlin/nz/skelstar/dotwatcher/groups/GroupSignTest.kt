package nz.skelstar.dotwatcher.groups

import nz.skelstar.dotwatcher.network.SessionMembership
import org.junit.Assert.assertEquals
import org.junit.Test

class GroupSignTest {
    private fun group(isOwner: Boolean, owner: String? = "Kiri") = SessionMembership(
        sessionId = "s1", sessionName = "Hunua 50k", inviteCode = "K7Q4", role = "runner", displayName = "Sam",
        ownerDisplayName = owner, isOwner = isOwner,
    )

    @Test fun `one group is just GROUP`() {
        assertEquals("GROUP", groupSignHeader(1))
    }

    @Test fun `several groups are counted`() {
        assertEquals("3 GROUPS", groupSignHeader(3))
    }

    @Test fun `your own group with its time left`() {
        assertEquals("Yours · 9 h 6 m left", groupSignLine(group(isOwner = true), GroupTime("21:36", "9 h 6 m left")))
    }

    @Test fun `a group someone else organised, by their name`() {
        assertEquals("Kiri's · 35 m left", groupSignLine(group(isOwner = false), GroupTime("12:00", "35 m left")))
    }

    @Test fun `no organiser name or end time from an older server`() {
        assertEquals("Joined", groupSignLine(group(isOwner = false, owner = null), null))
        assertEquals("Joined", groupSignLine(group(isOwner = false, owner = " "), null))
    }
}
