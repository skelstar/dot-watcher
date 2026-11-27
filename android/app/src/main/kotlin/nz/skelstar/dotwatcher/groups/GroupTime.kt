package nz.skelstar.dotwatcher.groups

import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/** When a group ends, worded for the person: the local clock time and how long is left. */
data class GroupTime(
    /** e.g. "12:00", in the phone's time zone. */
    val endsAt: String,
    /** e.g. "2 h 48 m left", "35 m left", or "Ending now" in the last minute. */
    val left: String,
)

/**
 * Words for when a group ends ([endsAtIso] is the server's `endsAt`). Null when it is missing or can't be read
 * (an older server), or the group has already ended, so the caller simply shows nothing.
 */
fun groupTime(endsAtIso: String?, now: Instant, zone: ZoneId = ZoneId.systemDefault()): GroupTime? {
    // OffsetDateTime, not Instant.parse: the server writes "+00:00", which older Android java.time can't read
    // as an Instant.
    val ends = endsAtIso?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() } ?: return null
    if (!ends.isAfter(now)) return null
    val clock = DateTimeFormatter.ofPattern("H:mm", Locale.getDefault()).withZone(zone).format(ends)
    return GroupTime(endsAt = clock, left = timeLeft(Duration.between(now, ends)))
}

/**
 * When a group ends, for "until it ends, …": the clock time if that's today, else the day too ("Sun 18:00").
 * Null when the server didn't say or it has already ended. Matches iOS's `groupEndWords`.
 */
fun groupEndWords(endsAtIso: String?, now: Instant, zone: ZoneId = ZoneId.systemDefault()): String? {
    val ends = endsAtIso?.let { runCatching { OffsetDateTime.parse(it).toInstant() }.getOrNull() } ?: return null
    if (!ends.isAfter(now)) return null
    val sameDay = ends.atZone(zone).toLocalDate() == now.atZone(zone).toLocalDate()
    val pattern = if (sameDay) "H:mm" else "EEE H:mm"
    return DateTimeFormatter.ofPattern(pattern, Locale.getDefault()).withZone(zone).format(ends)
}

private fun timeLeft(left: Duration): String {
    val minutes = left.toMinutes()
    return when {
        minutes < 1 -> "Ending now"
        minutes < 60 -> "$minutes m left"
        else -> {
            val hours = minutes / 60
            val rest = minutes % 60
            if (rest == 0L) "$hours h left" else "$hours h $rest m left"
        }
    }
}
