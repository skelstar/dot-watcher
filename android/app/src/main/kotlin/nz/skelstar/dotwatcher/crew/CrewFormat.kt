package nz.skelstar.dotwatcher.crew

import nz.skelstar.dotwatcher.network.CrewCodePreview
import nz.skelstar.dotwatcher.network.CrewMember
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.TextStyle
import java.util.Locale

// Wording and small derivations for the crew UI, kept free of Compose/Android so they can be tested.

/** What crew members see for a person: the nickname they chose, else their account initials. */
fun CrewMember.label(): String = nickname?.takeIf { it.isNotBlank() } ?: displayName

fun CrewCodePreview.label(): String = nickname?.takeIf { it.isNotBlank() } ?: displayName

/** How many faces the main-screen row shows before collapsing the rest into "+N". */
const val CREW_ROW_FACES = 4

/** What the slim crew row on the start screen shows. [faces] are in the order given (the server
 *  lists the most recently added first). */
data class CrewRowSummary(
    val faces: List<CrewMember>,
    val extra: Int,
    val title: String,
    val meta: String,
)

fun crewRowSummary(members: List<CrewMember>): CrewRowSummary =
    if (members.isEmpty()) {
        CrewRowSummary(emptyList(), 0, "Your crew", "Add people that you know")
    } else {
        CrewRowSummary(
            faces = members.take(CREW_ROW_FACES),
            extra = (members.size - CREW_ROW_FACES).coerceAtLeast(0),
            title = "Your crew",
            meta = if (members.size == 1) "1 person" else "${members.size} people",
        )
    }

/** "since March" for this year, "since March 2025" otherwise; [zone] is where "March" is read. */
fun crewSinceLabel(since: String, now: Instant, zone: ZoneId): String {
    val then = runCatching { OffsetDateTime.parse(since).toInstant().atZone(zone) }.getOrNull()
        ?: return "in your crew"
    val month = then.month.getDisplayName(TextStyle.FULL, Locale.ENGLISH)
    return if (then.year == now.atZone(zone).year) "since $month" else "since $month ${then.year}"
}

/** "Expires in 23 h" / "Expires in 40 min" / "Expired", for the code the person is about to send. */
fun crewExpiryLabel(expiresAt: String, now: Instant): String {
    val expiry = runCatching { OffsetDateTime.parse(expiresAt).toInstant() }.getOrNull() ?: return ""
    val left = Duration.between(now, expiry)
    return when {
        left.isNegative || left.isZero -> "Expired"
        left.toHours() >= 1 -> "Expires in ${left.toHours()} h"
        else -> "Expires in ${maxOf(1, left.toMinutes())} min"
    }
}

/** The server's answers to a redeem or preview, in words. 404 is deliberately vague: an unknown,
 *  expired, used or blocked code all look the same. */
fun crewCodeFailureMessage(httpStatus: Int): String = when (httpStatus) {
    400 -> "That's your own crew code."
    404 -> "That code has already been used or has expired. Ask them to send you a new one."
    409 -> "Your crew, or theirs, is full."
    429 -> "Too many attempts. Try again later."
    else -> "Something went wrong (error $httpStatus). Try again."
}

/** Nickname rules, matching the server: trimmed, 1-30 characters, no control characters. A blank
 *  value clears the nickname, which is a valid choice (null here). */
const val MAX_NICKNAME_LENGTH = 30

fun cleanNickname(raw: String): String? = raw.trim().takeIf { it.isNotEmpty() }

fun isValidNickname(raw: String): Boolean {
    val cleaned = cleanNickname(raw) ?: return true
    return cleaned.length <= MAX_NICKNAME_LENGTH && cleaned.none { it.isISOControl() }
}
