package nz.skelstar.dotwatcher

import nz.skelstar.dotwatcher.data.ShareChoiceStore.Choice
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.SessionMembership
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Duration
import java.time.Instant

class RestoreDestinationTest {
    private val now = Instant.parse("2026-10-19T00:00:00Z")
    private val runner = membership("s1", role = "runner")
    private val viewer = membership("s2", role = "viewer")

    private fun membership(id: String, role: String) =
        SessionMembership(id, "Session $id", "ABC123", role, "AA")

    private fun decide(
        memberships: List<SessionMembership> = listOf(runner),
        tracking: TrackingState.Tracking? = null,
        choice: Choice? = null,
        canResume: Boolean = true,
    ) = decideRestore(memberships, tracking, choice, now, canResume)

    @Test fun `no membership goes to create or join`() {
        assertEquals(RestoreDecision.CreateOrJoin, decide(memberships = emptyList()))
    }

    @Test fun `a runner who has never chosen is asked`() {
        assertEquals(RestoreDecision.Consent(runner), decide())
    }

    @Test fun `a viewer goes straight to the map`() {
        assertEquals(RestoreDecision.Map(viewer, isSharing = false), decide(memberships = listOf(viewer)))
    }

    @Test fun `still sharing in this process stays on the map sharing`() {
        val tracking = TrackingState.Tracking("s1", now.plusSeconds(600))
        assertEquals(RestoreDecision.Map(runner, isSharing = true), decide(tracking = tracking))
    }

    @Test fun `a runner who chose view only is not asked again`() {
        assertEquals(
            RestoreDecision.Map(runner, isSharing = false),
            decide(choice = Choice.ViewOnly("s1")),
        )
    }

    @Test fun `a share that has not run out resumes without asking, keeping the real total`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(2))
        assertEquals(
            RestoreDecision.ResumeSharing(runner, remaining = Duration.ofHours(1), total = Duration.ofHours(2)),
            decide(choice = choice),
        )
    }

    @Test fun `a share that has run out asks again`() {
        val choice = Choice.Sharing("s1", expiresAt = now.minusSeconds(1), totalDuration = Duration.ofHours(2))
        assertEquals(RestoreDecision.Consent(runner), decide(choice = choice))
    }

    @Test fun `a share that cannot resume for lack of permission falls back to asking`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(1)), totalDuration = Duration.ofHours(2))
        assertEquals(RestoreDecision.Consent(runner), decide(choice = choice, canResume = false))
    }

    @Test fun `a choice for a different session does not count`() {
        val other = Choice.ViewOnly("some-other-session")
        assertEquals(RestoreDecision.Consent(runner), decide(choice = other))
    }

    @Test fun `the session the choice belongs to is preferred when there are several`() {
        val a = membership("a", "runner")
        val b = membership("b", "runner")
        val result = decide(memberships = listOf(a, b), choice = Choice.ViewOnly("b"))
        assertTrue(result is RestoreDecision.Map && result.membership == b && !result.isSharing)
    }

    @Test fun `total is never reported smaller than what remains`() {
        val choice = Choice.Sharing("s1", expiresAt = now.plus(Duration.ofHours(3)), totalDuration = Duration.ofHours(1))
        val result = decide(choice = choice) as RestoreDecision.ResumeSharing
        assertEquals(Duration.ofHours(3), result.total)
    }
}
