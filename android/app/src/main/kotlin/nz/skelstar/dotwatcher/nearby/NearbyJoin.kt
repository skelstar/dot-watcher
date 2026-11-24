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
    /** Groups: "Ask for a code to join" (see .ai/plans/go-and-groups.md). */
    val askForCode: Boolean = false,
    val entryCode: String = "",
) {
    val trimmedTitle: String get() = title.trim()

    /** The code to send, or null when not asking for one. */
    val entryCodeToSend: String? get() = entryCode.trim().takeIf { askForCode && it.isNotEmpty() }

    /** Asking for a code needs a code the server accepts (4-8 letters or digits). */
    val isEntryCodeValid: Boolean get() = !askForCode || isValidEntryCode(entryCode.trim())
}

/** The server's limits for a group's entry code. */
const val ENTRY_CODE_MIN_LENGTH = 4
const val ENTRY_CODE_MAX_LENGTH = 8

/** Whether [code] is something the server accepts as an entry code: 4-8 letters A-Z or digits, any case. */
fun isValidEntryCode(code: String): Boolean =
    code.length in ENTRY_CODE_MIN_LENGTH..ENTRY_CODE_MAX_LENGTH &&
        code.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' }

/** What someone types into an entry code field, cleaned the way the server reads it: letters and digits only,
 *  upper case, no longer than the limit. */
fun cleanEntryCode(typed: String): String =
    typed.filter { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' }.uppercase().take(ENTRY_CODE_MAX_LENGTH)

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
    /** Where the join location was last set from this device (the server never returns it); null if unknown. */
    val meetingPoint: Fix?,
    /** Takes a fresh fix and makes it the join location; [radiusMeters] null keeps the current range.
     *  Returns words for the person, whether it worked or not. */
    val setJoinLocation: suspend (radiusMeters: Int?) -> String,
    /** Stops the session being shown nearby (members stay). Returns words for the person. */
    val stopShowing: suspend () -> String,
    /** Ends the session for everyone, after the person has confirmed. Null on success (the app then leaves the
     *  map); otherwise words for what went wrong, and the person stays. */
    val endSession: suspend () -> String?,
    /** The group's entry code, for accounts with "groups"; null hides the section. */
    val entryCode: EntryCodeActions? = null,
)

/** Reading and changing a group's entry code, for its organiser (see .ai/plans/go-and-groups.md). */
class EntryCodeActions(
    /** The current code, null when there is none; a failure carries words for the person. */
    val load: suspend () -> Result<String?>,
    /** Sets, changes or (null) clears the code. Returns words for the person. */
    val change: suspend (code: String?) -> String,
)
