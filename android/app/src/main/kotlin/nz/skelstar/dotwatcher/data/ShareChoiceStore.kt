package nz.skelstar.dotwatcher.data

import android.content.Context
import java.time.Duration
import java.time.Instant

/**
 * What the runner chose last time for their session, so that coming back to the app does not ask
 * again. Android freely kills a backgrounded app's process (locking the phone is enough), and the
 * restored app used to treat "runner in a session, nothing sharing" as needing consent every time,
 * so unlocking meant being asked for a duration again.
 *
 * Two kinds of choice: "sharing until [Sharing.expiresAt]" (written by the tracking service, which
 * also moves it when the session is extended) and "view only" (declined, or stopped). Neither is
 * sensitive, so this is plain SharedPreferences rather than the encrypted token store.
 */
class ShareChoiceStore(context: Context) {
    sealed interface Choice {
        val sessionId: String

        data class Sharing(
            override val sessionId: String,
            val expiresAt: Instant,
            val totalDuration: Duration,
        ) : Choice

    }

    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun read(): Choice? {
        val sessionId = prefs.getString(KEY_SESSION_ID, null) ?: return null
        return when (prefs.getString(KEY_MODE, null)) {
            MODE_SHARING -> {
                val expiresAt = prefs.getLong(KEY_EXPIRES_AT, 0L).takeIf { it > 0 } ?: return null
                Choice.Sharing(
                    sessionId,
                    Instant.ofEpochMilli(expiresAt),
                    Duration.ofSeconds(prefs.getLong(KEY_TOTAL_SECONDS, 0L)),
                )
            }
            else -> null
        }
    }

    fun recordSharing(sessionId: String, expiresAt: Instant, totalDuration: Duration) {
        prefs.edit()
            .putString(KEY_SESSION_ID, sessionId)
            .putString(KEY_MODE, MODE_SHARING)
            .putLong(KEY_EXPIRES_AT, expiresAt.toEpochMilli())
            .putLong(KEY_TOTAL_SECONDS, totalDuration.seconds)
            .apply()
    }

    /** Forgets a *sharing* choice only. The tracking service calls this when sharing ends. */
    fun clearSharing() {
        if (prefs.getString(KEY_MODE, null) == MODE_SHARING) prefs.edit().clear().apply()
    }

    /** Forgets everything: leaving a session, signing out, deleting the account. */
    fun clear() {
        prefs.edit().clear().apply()
    }

    private companion object {
        const val PREFS_NAME = "dotwatcher_share_choice"
        const val KEY_SESSION_ID = "session_id"
        const val KEY_MODE = "mode"
        const val KEY_EXPIRES_AT = "expires_at"
        const val KEY_TOTAL_SECONDS = "total_seconds"
        const val MODE_SHARING = "sharing"
    }
}
