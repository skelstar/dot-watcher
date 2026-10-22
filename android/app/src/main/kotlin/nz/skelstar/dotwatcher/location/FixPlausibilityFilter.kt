package nz.skelstar.dotwatcher.location

import kotlin.math.cos
import kotlin.math.hypot

/** Fastest we believe a runner can move: 120 km/h, so a car on the open road is fine while a
 *  kilometres-away jump inside one tick is not. */
const val MAX_PLAUSIBLE_SPEED_MPS = 120.0 / 3.6

/** A gap longer than this is treated as this long when sizing the allowed distance. Without the
 *  cap, a long silence (GPS lost for ten minutes) would allow a jump of many kilometres and a bad
 *  fix right after it would sail through. */
const val MAX_GAP_SECONDS = 60.0

/** Consecutive rejected fixes that agree with each other before we accept that the runner really
 *  did move that far (GPS lost, then reacquired elsewhere) and re-anchor on the new position. */
const val CONFIRMATIONS_TO_REANCHOR = 2

/** A position fix reduced to what plausibility needs. [elapsedSeconds] is any monotonic clock. */
data class FixPoint(
    val latitude: Double,
    val longitude: Double,
    val accuracyMetres: Float,
    val elapsedSeconds: Double,
)

sealed interface Plausibility {
    data object Accepted : Plausibility
    data class Rejected(val distanceMetres: Double, val allowedMetres: Double) : Plausibility
}

/**
 * Rejects fixes that imply an impossible speed from the last accepted fix — a fused/network fix
 * occasionally lands kilometres away while still claiming a tight accuracy, which the accuracy cap
 * alone can't catch. Stateful; reset it whenever tracking pauses or restarts so the first fix
 * afterwards is taken as it comes.
 *
 * A rejected fix is remembered. If the next fix is consistent with it (within what the runner
 * could have covered in between) the move was real, so after [CONFIRMATIONS_TO_REANCHOR] such
 * fixes in a row the filter accepts and re-anchors instead of rejecting forever.
 */
class FixPlausibilityFilter(
    private val maxSpeedMps: Double = MAX_PLAUSIBLE_SPEED_MPS,
    private val maxGapSeconds: Double = MAX_GAP_SECONDS,
    private val confirmations: Int = CONFIRMATIONS_TO_REANCHOR,
) {
    private var anchor: FixPoint? = null
    private var pending: FixPoint? = null
    private var pendingCount = 0

    fun reset() {
        anchor = null
        pending = null
        pendingCount = 0
    }

    fun check(fix: FixPoint): Plausibility {
        val last = anchor
        if (last == null) {
            anchor = fix
            return Plausibility.Accepted
        }

        val distance = distanceMetres(last, fix)
        val allowed = allowedMetres(last, fix)
        if (distance <= allowed) {
            accept(fix)
            return Plausibility.Accepted
        }

        // Too far from where we last were. Is it at least consistent with the previous reject?
        val previous = pending
        val consistent = previous != null && distanceMetres(previous, fix) <= allowedMetres(previous, fix)
        pendingCount = if (consistent) pendingCount + 1 else 1
        pending = fix
        if (pendingCount >= confirmations) {
            accept(fix)
            return Plausibility.Accepted
        }
        return Plausibility.Rejected(distance, allowed)
    }

    private fun allowedMetres(from: FixPoint, to: FixPoint): Double {
        val gap = (to.elapsedSeconds - from.elapsedSeconds).coerceIn(0.0, maxGapSeconds)
        return maxSpeedMps * gap + from.accuracyMetres + to.accuracyMetres
    }

    private fun accept(fix: FixPoint) {
        anchor = fix
        pending = null
        pendingCount = 0
    }

    private fun distanceMetres(a: FixPoint, b: FixPoint): Double {
        val metresPerDegree = 111_320.0
        val dx = (b.longitude - a.longitude) * cos(Math.toRadians(a.latitude)) * metresPerDegree
        val dy = (b.latitude - a.latitude) * metresPerDegree
        return hypot(dx, dy)
    }
}
