package nz.skelstar.dotwatcher.groups

import nz.skelstar.dotwatcher.network.SessionMembership
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class GoSignEyebrowTest {
    private fun group(name: String) =
        SessionMembership(sessionId = name, sessionName = name, inviteCode = "K7Q4", role = "runner", displayName = "Kiri")

    @Test fun `no groups, no line`() {
        assertNull(goSignEyebrow(emptyList()))
    }

    @Test fun `one group by name`() {
        assertEquals("With Hunua 50k", goSignEyebrow(listOf(group("Hunua 50k"))))
    }

    @Test fun `the newest by name, then how many more`() {
        assertEquals(
            "With Hunua 50k + 2 more",
            goSignEyebrow(listOf(group("Hunua 50k"), group("Sunday long run"), group("Parkrun"))),
        )
    }
}
