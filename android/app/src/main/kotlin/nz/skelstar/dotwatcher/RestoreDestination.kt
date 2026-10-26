package nz.skelstar.dotwatcher

import nz.skelstar.dotwatcher.data.ShareChoiceStore
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.SessionMembership
import java.time.Duration
import java.time.Instant

/** Where to put the runner when the app comes back (see [decideRestore]). */
sealed interface RestoreDecision {
    data object CreateOrJoin : RestoreDecision

    /** Still sharing from this process: back to the map. */
    data class Map(val membership: SessionMembership) : RestoreDecision

    /** Start sharing again without asking: they chose a duration that has not run out. [total] is the
     *  original span (including extensions) so the countdown arc stays honest. */
    data class ResumeSharing(
        val membership: SessionMembership,
        val remaining: Duration,
        val total: Duration,
    ) : RestoreDecision

    /** Still a member of sessions from an earlier run with nothing to carry on: leave them, then
     *  start from the create/join screen. */
    data class LeaveStale(val memberships: List<SessionMembership>) : RestoreDecision
}

/**
 * Decides where the app lands after the process was recreated (locking the phone is often enough
 * for Android to kill a backgrounded app). A session doesn't carry over a relaunch, except while the
 * runner is still sharing:
 *
 * - already sharing in this process → the map;
 * - chose a duration that has not run out → resume sharing quietly ([canResumeSharing] is false if
 *   the location permissions it needs are gone, which counts as nothing to carry on);
 * - otherwise any session the server still lists is left, and the runner rejoins with the invite code.
 */
fun decideRestore(
    memberships: List<SessionMembership>,
    tracking: TrackingState.Tracking?,
    choice: ShareChoiceStore.Choice?,
    now: Instant,
    canResumeSharing: Boolean,
): RestoreDecision {
    if (memberships.isEmpty()) return RestoreDecision.CreateOrJoin

    memberships.firstOrNull { it.sessionId == tracking?.sessionId }
        ?.let { return RestoreDecision.Map(it) }

    if (choice is ShareChoiceStore.Choice.Sharing && choice.expiresAt.isAfter(now) && canResumeSharing) {
        memberships.firstOrNull { it.sessionId == choice.sessionId }?.let { membership ->
            val remaining = Duration.between(now, choice.expiresAt)
            return RestoreDecision.ResumeSharing(
                membership,
                remaining = remaining,
                total = maxOf(choice.totalDuration, remaining),
            )
        }
    }

    return RestoreDecision.LeaveStale(memberships)
}
