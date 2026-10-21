package nz.skelstar.dotwatcher

import nz.skelstar.dotwatcher.data.ShareChoiceStore
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.SessionMembership
import java.time.Duration
import java.time.Instant

/** Where to put the runner when the app comes back (see [decideRestore]). */
sealed interface RestoreDecision {
    data object CreateOrJoin : RestoreDecision
    data class Map(val membership: SessionMembership, val isSharing: Boolean) : RestoreDecision

    /** Ask how long to share for: a runner who has not chosen yet, or whose choice has run out. */
    data class Consent(val membership: SessionMembership) : RestoreDecision

    /** Start sharing again without asking: they chose a duration that has not run out. [total] is the
     *  original span (including extensions) so the countdown arc stays honest. */
    data class ResumeSharing(
        val membership: SessionMembership,
        val remaining: Duration,
        val total: Duration,
    ) : RestoreDecision
}

/**
 * Decides where the app lands after the process was recreated (locking the phone is often enough
 * for Android to kill a backgrounded app). The rule is that a runner is only asked for a duration
 * when they have not made a choice that still holds:
 *
 * - already sharing in this process → the map, sharing;
 * - chose "view only" for this session → the map, not sharing;
 * - chose a duration that has not run out → resume sharing quietly ([canResumeSharing] is false if
 *   the location permissions it needs are gone, which falls back to asking);
 * - otherwise a runner is asked, and a viewer goes straight to the map.
 */
fun decideRestore(
    memberships: List<SessionMembership>,
    tracking: TrackingState.Tracking?,
    choice: ShareChoiceStore.Choice?,
    now: Instant,
    canResumeSharing: Boolean,
): RestoreDecision {
    val membership = memberships.firstOrNull { it.sessionId == tracking?.sessionId }
        ?: memberships.firstOrNull { it.sessionId == choice?.sessionId }
        ?: memberships.firstOrNull()
        ?: return RestoreDecision.CreateOrJoin

    if (tracking?.sessionId == membership.sessionId) return RestoreDecision.Map(membership, isSharing = true)
    if (membership.role != "runner") return RestoreDecision.Map(membership, isSharing = false)

    return when {
        choice is ShareChoiceStore.Choice.ViewOnly && choice.sessionId == membership.sessionId ->
            RestoreDecision.Map(membership, isSharing = false)

        choice is ShareChoiceStore.Choice.Sharing && choice.sessionId == membership.sessionId &&
            choice.expiresAt.isAfter(now) && canResumeSharing ->
            RestoreDecision.ResumeSharing(
                membership,
                remaining = Duration.between(now, choice.expiresAt),
                total = maxOf(choice.totalDuration, Duration.between(now, choice.expiresAt)),
            )

        else -> RestoreDecision.Consent(membership)
    }
}
