package nz.skelstar.dotwatcher.nearby

import nz.skelstar.dotwatcher.location.Fix
import nz.skelstar.dotwatcher.network.NearbyStatus

// Nearby join: an organiser lets people near them find and join a session with no invite code. The server side
// is SessionsController / EventsController; the plan is .ai/plans/nearby-join.md. Everything here is reached
// only by accounts with the "events" feature.

/** The ranges an organiser can choose, in metres. The server accepts exactly these. */
val NEARBY_RADIUS_OPTIONS = listOf(50, 100, 250, 500)

const val NEARBY_DEFAULT_RADIUS_METRES = 100

/** The most characters a public title can have (the server limit). */
const val NEARBY_MAX_TITLE_LENGTH = 30

/** The worst accuracy the server accepts for placing a join location. */
private const val MAX_JOIN_LOCATION_ACCURACY_METRES = 100.0

/** A fix is good enough to place a join location when its accuracy is known and within 100 m. */
fun Fix.isUsableForJoinLocation(): Boolean =
    accuracyMetres?.let { it <= MAX_JOIN_LOCATION_ACCURACY_METRES } == true

/** What a nearby session is created with: the range, and the organiser's position, which becomes the first
 *  join location. */
data class NearbyCreate(val radiusMeters: Int, val fix: Fix)

/** The Create dialog's nearby choices, before there is a position. */
data class NearbyDraft(
    val isOn: Boolean = false,
    val title: String = "",
    val radiusMeters: Int = NEARBY_DEFAULT_RADIUS_METRES,
) {
    val trimmedTitle: String get() = title.trim()
}

/** The session that stopped a new one being created (409), kept with what was being created so ending it
 *  can carry straight on. */
data class ActiveSessionConflict(
    val sessionId: String,
    val sessionName: String?,
    val nearby: NearbyDraft?,
)

/**
 * What the map offers the organiser of a nearby session. Null on the map for everyone else (a joiner, or a
 * session made without nearby).
 */
class NearbyOrganiser(
    val status: NearbyStatus,
    /** Takes a fresh fix and makes it the join location; [radiusMeters] null keeps the current range.
     *  Returns words for the person, whether it worked or not. */
    val setJoinLocation: suspend (radiusMeters: Int?) -> String,
    /** Stops the session being shown nearby (members stay). Returns words for the person. */
    val stopShowing: suspend () -> String,
    /** Ends the session for everyone, after the person has confirmed. Null on success (the app then leaves the
     *  map); otherwise words for what went wrong, and the person stays. */
    val endSession: suspend () -> String?,
)
