package nz.skelstar.dotwatcher.location

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import nz.skelstar.dotwatcher.MainActivity
import nz.skelstar.dotwatcher.R
import nz.skelstar.dotwatcher.network.RunnerPosition
import java.time.Duration
import java.time.Instant

/**
 * Which crew members are newly out, from one Go post reply to the next (see .ai/plans/crew-out-alerts.md).
 * Pure logic, kept apart from the notification so it can be unit tested. Same rules as iOS's `CrewOutTracker`.
 */
class CrewOutTracker {
    /** Null until the first reply of a run: whoever is already out then sets the baseline silently. */
    private var lastOut: Set<String>? = null
    private val lastAlertedAt = mutableMapOf<String, Instant>()

    /** Forget everything, so the next reply sets a new baseline. Called when a run starts and stops. */
    fun reset() {
        lastOut = null
        lastAlertedAt.clear()
    }

    /** [out] is the crew on a Go run in this reply (not yourself, not crew in a named session). Returns the
     *  names to announce, sorted. */
    fun update(out: Set<String>, now: Instant): List<String> {
        val previous = lastOut
        lastOut = out
        if (previous == null) {
            out.forEach { lastAlertedAt[it] = now }
            return emptyList()
        }
        val fresh = (out - previous).filter { name ->
            val last = lastAlertedAt[name] ?: return@filter true
            Duration.between(last, now) >= COOLDOWN
        }.sorted()
        fresh.forEach { lastAlertedAt[it] = now }
        return fresh
    }

    companion object {
        /** A dot that drops out for a bad-signal stretch and comes back shouldn't alert twice. */
        val COOLDOWN: Duration = Duration.ofMinutes(30)

        /** The crew on a Go run in one reply: not yourself, not crew in a named session. */
        fun outNames(positions: List<RunnerPosition>): Set<String> =
            positions.filter { !it.isSelf && !it.inSession }.map { it.runnerName }.toSet()

        /** "DW: Mia has joined", "DW: Mia and Sam have joined", "DW: Jo, Mia and Sam have joined". The "DW:" says
         *  whose alert it is on a watch, which shows the text but not the app's icon. */
        fun title(names: List<String>): String = when (names.size) {
            0 -> ""
            1 -> "DW: ${names[0]} has joined"
            else -> "DW: ${names.dropLast(1).joinToString(", ")} and ${names.last()} have joined"
        }
    }
}

/** The "Crew alerts" switch in Settings. On by default. Plain SharedPreferences: not sensitive. */
object CrewAlertSettings {
    private const val PREFS_NAME = "crew_alerts"
    private const val KEY_ENABLED = "enabled"

    fun isEnabled(context: Context): Boolean =
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).getBoolean(KEY_ENABLED, true)

    fun setEnabled(context: Context, enabled: Boolean) {
        context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            .edit().putBoolean(KEY_ENABLED, enabled).apply()
    }
}

/**
 * The notification for crew who just started a Go run while you're on one. Fed from the reply to this phone's
 * own `POST /location` in [LocationTrackingService], so it works with the screen locked and needs no push. Its own
 * channel, separate from the low-importance tracking one, so either can be muted alone.
 */
class CrewOutAlerts(private val context: Context) {
    private val tracker = CrewOutTracker()

    @Synchronized
    fun reset() = tracker.reset()

    /** Feed one successful Go post reply. Synchronized: the loop and "Send now" can post at once. */
    @Synchronized
    fun handle(positions: List<RunnerPosition>, now: Instant = Instant.now()) {
        val fresh = tracker.update(CrewOutTracker.outNames(positions), now)
        if (fresh.isEmpty() || !CrewAlertSettings.isEnabled(context)) return
        post(fresh)
    }

    private fun post(names: List<String>) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) return

        context.getSystemService(NotificationManager::class.java)?.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "Crew alerts", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "When someone in your crew starts a Go run while you're on one."
            }
        )
        val open = PendingIntent.getActivity(
            context,
            REQUEST_CODE,
            Intent(context, MainActivity::class.java)
                .putExtra(EXTRA_OPEN_GO_MAP, true)
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_stat_dot)
            .setContentTitle(CrewOutTracker.title(names))
            .setContentText(if (names.size == 1) "Started a Go run" else "Started Go runs")
            .setAutoCancel(true)
            .setContentIntent(open)
            .build()
        // One id per set of names, so a repeat replaces rather than stacks.
        NotificationManagerCompat.from(context).notify("crew-out-${names.joinToString("-")}", NOTIFICATION_ID, notification)
    }

    companion object {
        private const val CHANNEL_ID = "crew_alerts"
        private const val NOTIFICATION_ID = 2
        private const val REQUEST_CODE = 2

        /** On the intent a tap opens: show the Go map if a Go run is still being tracked. */
        const val EXTRA_OPEN_GO_MAP = "open_go_map"
    }
}
