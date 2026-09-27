package nz.skelstar.dotwatcher.location

import android.content.Context
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.io.File

/** One capture that failed to post, held onto for a later retry. Carries the session it was
 *  captured for, so a queued point is never flushed against a different session than the one
 *  the runner was in at capture time (e.g. if they leave and rejoin elsewhere while offline). */
@Serializable
data class QueuedCapture(
    val sessionId: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double?,
    val timestamp: String,
)

/**
 * Persisted, capped queue for [POST /location] calls that failed to send — see
 * .ai/plans/offline-location-queue.md. Backed by a single JSON file in the app's private files
 * directory (not SharedPreferences/EncryptedSharedPreferences, which suit small key-value state
 * better than an unbounded-shaped list) so queued positions survive the process being killed —
 * the scenario this exists for, since a long dead zone is exactly when a foreground service is
 * also most likely to be reclaimed under memory pressure.
 *
 * Not thread-safe beyond what [LocationTrackingService]'s single-threaded tracking loop already
 * guarantees (one coroutine calling enqueue/drain in sequence, never concurrently).
 */
class LocationUpdateQueue(context: Context) {
    private val file = File(context.filesDir, QUEUE_FILE_NAME)
    private val json = Json { ignoreUnknownKeys = true }

    /** Adds a failed capture to the queue, dropping the oldest entry first if already at the
     *  cap — the queue holds the most *recent* history of an outage, not the earliest, since a
     *  viewer cares more about where the runner is now than exactly where they were at the start
     *  of a long dead zone. */
    fun enqueue(capture: QueuedCapture) {
        val current = readAll().toMutableList()
        current.add(capture)
        while (current.size > MAX_QUEUE_SIZE) {
            current.removeAt(0)
        }
        writeAll(current)
    }

    /** Returns every queued capture, oldest first, and clears the queue. Callers are expected to
     *  post each one in order; if a post partway through fails, re-[enqueue] the remainder rather
     *  than assume [drain]'s caller already persisted anything itself. */
    fun drain(): List<QueuedCapture> {
        val current = readAll()
        if (current.isNotEmpty()) writeAll(emptyList())
        return current
    }

    fun isEmpty(): Boolean = readAll().isEmpty()

    private fun readAll(): List<QueuedCapture> {
        if (!file.exists()) return emptyList()
        return runCatching { json.decodeFromString<List<QueuedCapture>>(file.readText()) }
            .getOrDefault(emptyList())
    }

    private fun writeAll(captures: List<QueuedCapture>) {
        runCatching { file.writeText(json.encodeToString(captures)) }
    }

    private companion object {
        const val QUEUE_FILE_NAME = "location_update_queue.json"
        // ~50 minutes of backlog at the current 15s posting cadence
        // (LocationTrackingService.POST_INTERVAL_SECONDS) — generous enough for a real dead
        // zone without growing unbounded through, e.g., airplane mode left on by accident.
        const val MAX_QUEUE_SIZE = 200
    }
}
