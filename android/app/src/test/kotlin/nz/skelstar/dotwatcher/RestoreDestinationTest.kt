package nz.skelstar.dotwatcher

import nz.skelstar.dotwatcher.data.ShareChoiceStore.Choice
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.SessionMembership
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Duration
import java.time.Instant

class RestoreDestinationTest {
    private val now = Instant.parse("2026-10-19T00:00:00Z")
    private val runner = membership("s1")

    private fun membership(id: String) =
        SessionMembership(id, "Session $id", "ABC123", "runner", "AA")

    private fun decide(
        memberships: List<SessionMembership> = listOf(runner),
        tracking: TrackingState.Tracking? = null,
        choice: Choice? = null,
        canResume: Boolean = true,
    ) = decideRestore(memberships, tracking, choice, now, canResume)

    @Test fun `no membership goes to create or join`() {
        assertEquals(RestoreDecision.CreateOrJoin, decide(memberships = emptyList()))
    }

    @Test fun `a session left over from an earlier run is left`() {
        assertEquals(RestoreDecision.LeaveStale(listOf(runner)), decide())
    }

    @Test fun `every leftover session is left`() {
        val other = membership("s2")
        assertEquals(RestoreDecision.LeaveStale(listOf(runner, other)), decide(memberships = listOf(runner, other)))
    }

    @Test fun `still sharing in this process stays on the map`() {
        val tracking = TrackingState.Tracking("s1", now.plusSeconds(600))
        assertEquals(RestoreDecision.Map(runner), decide(tracking = tracking))
    }

    @Test fun `a share that has not run out resumes without asking, keeping the real total`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(2))
        assertEquals(
            RestoreDecision.ResumeSharing(runner, remaining = Duration.ofHours(1), total = Duration.ofHours(2)),
            decide(choice = choice),
        )
    }

    @Test fun `a share that has run out is left`() {
        val choice = Choice.Sharing("s1", expiresAt = now.minusSeconds(1), totalDuration = Duration.ofHours(2))
        assertEquals(RestoreDecision.LeaveStale(listOf(runner)), decide(choice = choice))
    }

    @Test fun `a share that cannot resume for lack of permission is left`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(2))
        assertEquals(RestoreDecision.LeaveStale(listOf(runner)), decide(choice = choice, canResume = false))
    }

    @Test fun `a share for a session we are no longer in does not count`() {
        val choice = Choice.Sharing("gone", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(1))
        assertEquals(RestoreDecision.LeaveStale(listOf(runner)), decide(choice = choice))
    }

    @Test fun `the session being shared is the one resumed when there are several`() {
        val a = membership("a")
        val b = membership("b")
        val choice = Choice.Sharing("b", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(1))
        val result = decide(memberships = listOf(a, b), choice = choice)
        assertEquals(RestoreDecision.ResumeSharing(b, Duration.ofHours(1), Duration.ofHours(1)), result)
    }

    @Test fun `total is never reported smaller than what remains`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(3)), totalDuration = Duration.ofHours(1))
        val result = decide(choice = choice) as RestoreDecision.ResumeSharing
        assertEquals(Duration.ofHours(3), result.total)
    }
}
