package nz.skelstar.dotwatcher.location

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.content.getSystemService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.MainActivity
import nz.skelstar.dotwatcher.data.AuthTokenStore
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.data.ShareChoiceStore
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import java.time.Duration
import java.time.Instant

/** Normal post cadence, matching iOS's `normalInterval`
 *  (ios/DotWatcher/DotWatcher/LocationManager.swift:201). Android has no satellite-tier
 *  connectivity signal yet, so unlike iOS there is no slower fallback cadence for now. */
const val POST_INTERVAL_SECONDS = 15L

/** Options offered for how long a share session runs before it auto-stops, ascending — mirrors
 *  `LocationManager.trackingDurationOptions` (ios/DotWatcher/DotWatcher/LocationManager.swift:212).
 *  This is a cap on *automatic* sharing, not a mode switch: every option still auto-stops, the
 *  runner just picks how soon. */
val TRACKING_DURATION_OPTIONS: List<Duration> = listOf(2L, 4L, 8L, 24L).map { Duration.ofHours(it) }

/** The casual session's id. Fixed on the server (SessionStore.CasualSessionId) so every server and
 *  client agrees; a tracking run for this id is a casual one, with no membership behind it. */
const val CASUAL_SESSION_ID = "ca5ca100-0000-4000-8000-000000000001"

sealed interface TrackingState {
    data object Stopped : TrackingState
    data class Tracking(
        val sessionId: String,
        val expiresAt: Instant,
        val lastError: String? = null,
        /** How long this session was set to run for in total — the original choice plus any
         *  extensions. Lets the UI draw the countdown arc against the real span rather than
         *  whatever happened to be left when it first looked. */
        val totalDuration: Duration = Duration.ZERO,
        /** True while automatic tracking is paused for a critical battery (see
         *  [BatteryReader.CRITICAL_THRESHOLD]): no GPS fixes, no automatic posts, until the
         *  runner overrides, sends manually, or plugs in. Mirrors iOS's `pausedForBattery`;
         *  design in .ai/plans/critical-battery-pause.md. */
        val pausedForBattery: Boolean = false,
    ) : TrackingState
}

/**
 * Foreground service that owns the background-capable tracking loop: captures a position on the
 * clock-aligned cadence ([nextPostAt]) and posts it, surviving the screen being locked or the
 * app being backgrounded — the gap Milestone 1's in-composable timer couldn't close (that timer
 * only ran while LiveMapScreen was on-screen and the process wasn't suspended). A failed post is
 * queued rather than dropped ([LocationUpdateQueue], [postOrEnqueue]/[flushQueue] — see
 * .ai/plans/offline-location-queue.md) and retried once a later post succeeds; the queue is
 * persisted, so it survives this service being killed mid-outage and even outlives one sharing
 * session ending, flushing on the next successful post whenever tracking is next started.
 *
 * State is exposed via the [state] singleton flow rather than a bound-service interface, since
 * the UI only needs to observe progress/errors, not call back into the service directly.
 */
class LocationTrackingService : Service() {
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var trackingJob: Job? = null

    /** Set when the runner explicitly chose "Auto" while battery was critical: stays set for the
     *  rest of this tracking session (an explicit choice shouldn't need repeating every tick),
     *  and is reset by [startTracking]. Written from the main thread, read by the loop. */
    @Volatile private var batteryOverride = false

    private lateinit var locationTracker: LocationTracker
    private lateinit var repository: DotWatcherRepository
    private lateinit var updateQueue: LocationUpdateQueue
    private lateinit var shareChoices: ShareChoiceStore

    override fun onCreate() {
        super.onCreate()
        locationTracker = LocationTracker(applicationContext)
        val tokenStore = AuthTokenStore(applicationContext)
        val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL, applicationContext)
        repository = DotWatcherRepository(api, tokenStore)
        updateQueue = LocationUpdateQueue(applicationContext)
        shareChoices = ShareChoiceStore(applicationContext)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_SEND_NOW -> return handleSendNow()
            ACTION_RESUME_AUTO -> return handleResumeAuto()
        }
        if (intent?.action == ACTION_EXTEND) {
            val extraSeconds = intent.getLongExtra(EXTRA_DURATION_SECONDS, -1)
            val extension = Duration.ofSeconds(extraSeconds)
            var extended = false
            if (extraSeconds > 0) {
                _state.update { current ->
                    if (current is TrackingState.Tracking) {
                        extended = true
                        current.copy(
                            expiresAt = current.expiresAt.plus(extension),
                            totalDuration = current.totalDuration.plus(extension),
                        )
                    } else {
                        current
                    }
                }
            }
            (_state.value as? TrackingState.Tracking)?.takeIf { extended }?.let {
                shareChoices.recordSharing(it.sessionId, it.expiresAt, it.totalDuration)
            }
            // Nothing was running (this intent just created the service): don't leave it idle.
            if (!extended && _state.value !is TrackingState.Tracking) stopSelf()
            return START_NOT_STICKY
        }

        val sessionId = intent?.getStringExtra(EXTRA_SESSION_ID)
        val durationSeconds = intent?.getLongExtra(EXTRA_DURATION_SECONDS, -1) ?: -1
        val totalSeconds = intent?.getLongExtra(EXTRA_TOTAL_SECONDS, -1)?.takeIf { it > 0 } ?: durationSeconds

        if (sessionId == null || durationSeconds <= 0) {
            stopSelf()
            return START_NOT_STICKY
        }

        startForegroundWithNotification()
        startTracking(sessionId, Duration.ofSeconds(durationSeconds), Duration.ofSeconds(totalSeconds))

        // Not START_STICKY: if the OS kills this process, tracking should stay stopped rather
        // than silently restart without the runner's knowledge — matches the explicit-start,
        // explicit-stop model of a bounded share session (see TRACKING_DURATION_OPTIONS's kdoc).
        return START_NOT_STICKY
    }

    /** One manual capture + post, for the battery card's "Send now". Does *not* resume automatic
     *  tracking — one position, not a mode change (see critical-battery-pause.md). */
    private fun handleSendNow(): Int {
        val tracking = _state.value as? TrackingState.Tracking
        if (tracking == null) {
            stopSelf()
            return START_NOT_STICKY
        }
        serviceScope.launch {
            runCatching {
                val capture = locationTracker.captureOnce()
                QueuedCapture(
                    sessionId = tracking.sessionId,
                    latitude = capture.latitude,
                    longitude = capture.longitude,
                    heading = capture.heading,
                    timestamp = capture.fixedAt.toString(),
                    batteryLevel = BatteryReader.read(applicationContext).level,
                )
            }.fold(
                onSuccess = { captured -> postOrEnqueue(captured) },
                onFailure = { error -> recordError(error.message) },
            )
        }
        return START_NOT_STICKY
    }

    /** "Auto": explicit override back to full automatic tracking despite a critical battery. */
    private fun handleResumeAuto(): Int {
        if (_state.value !is TrackingState.Tracking) {
            stopSelf()
            return START_NOT_STICKY
        }
        batteryOverride = true
        // Resume at once rather than waiting out the loop's sleep for its next pass.
        applyBatteryPause(shouldPause = false)
        return START_NOT_STICKY
    }

    /** Moves into/out of the paused state, stopping or restarting the heading sensor and the
     *  streamed location updates with it. No-op if already in the requested state. */
    private fun applyBatteryPause(shouldPause: Boolean) {
        var changed = false
        _state.update { current ->
            if (current is TrackingState.Tracking && current.pausedForBattery != shouldPause) {
                changed = true
                current.copy(pausedForBattery = shouldPause)
            } else {
                current
            }
        }
        if (!changed) return
        if (shouldPause) {
            locationTracker.stopHeadingUpdates()
            locationTracker.stopLocationUpdates()
        } else {
            locationTracker.startHeadingUpdates()
            locationTracker.startLocationUpdates()
        }
    }

    private fun startTracking(sessionId: String, duration: Duration, total: Duration) {
        trackingJob?.cancel()
        batteryOverride = false
        locationTracker.startHeadingUpdates()
        locationTracker.startLocationUpdates()

        val initialExpiry = Instant.now().plus(duration)
        _state.value = TrackingState.Tracking(sessionId, initialExpiry, totalDuration = total)
        shareChoices.recordSharing(sessionId, initialExpiry, total)

        trackingJob = serviceScope.launch {
            while (isActive) {
                val now = Instant.now()
                // Re-read each pass: the runner can extend the session while this loop sleeps.
                val expiresAt = (_state.value as? TrackingState.Tracking)?.expiresAt ?: initialExpiry
                if (now >= expiresAt) {
                    // Auto-stop ends the casual run now rather than leaving it to time out.
                    if (sessionId == CASUAL_SESSION_ID) runCatching { repository.casualStop() }
                    stopTracking()
                    stopSelf()
                    break
                }

                // Critical battery pauses automatic tracking: skip the capture entirely (that is
                // what actually saves power). Charging, or the runner's explicit override, lifts it.
                val battery = BatteryReader.read(applicationContext)
                val pause = !batteryOverride && BatteryReader.isCritical(battery)
                applyBatteryPause(pause)

                if (!pause) {
                    runCatching {
                        val capture = locationTracker.captureOnce()
                        QueuedCapture(
                            sessionId = sessionId,
                            latitude = capture.latitude,
                            longitude = capture.longitude,
                            heading = capture.heading,
                            timestamp = capture.fixedAt.toString(),
                            batteryLevel = battery.level,
                        )
                    }.fold(
                        onSuccess = { captured -> postOrEnqueue(captured) },
                        onFailure = { error -> recordError(error.message) },
                    )
                }

                val beforeDelay = Instant.now()
                val delayMillis = Duration.between(beforeDelay, nextPostAt(beforeDelay, POST_INTERVAL_SECONDS))
                    .toMillis()
                    .coerceAtLeast(0)
                delay(delayMillis)
            }
        }
    }

    /**
     * Posts [captured]; on any failure (thrown exception, e.g. no network, or a non-2xx HTTP
     * response — Retrofit doesn't throw for the latter, so both must be checked explicitly)
     * queues it instead of dropping it (see .ai/plans/offline-location-queue.md). A *successful*
     * post is also the trigger to flush anything already queued from an earlier outage — no
     * separate connectivity check is needed, since a live post succeeding is itself the signal
     * that the path to the server is back.
     */
    private suspend fun postOrEnqueue(captured: QueuedCapture) {
        val result = runCatching { repository.postCapture(captured) }
        var response = result.getOrNull()

        // A casual post is refused (403) once its run has closed (stopped on another device, or
        // closed by the server after a long gap). Starting again is free and idempotent, so do it
        // and send this position once more rather than losing it.
        if (response != null && response.code() == 403 && captured.sessionId == CASUAL_SESSION_ID) {
            val restarted = runCatching { repository.casualStart() }.getOrNull()?.isSuccessful == true
            if (restarted) response = runCatching { repository.postCapture(captured) }.getOrNull()
        }

        when {
            response != null && response.isSuccessful -> {
                recordSuccess()
                flushQueue()
            }
            // Permanent failure (see flushQueue's kdoc) — retrying would never help, so report
            // it but don't queue it.
            response != null && response.code() in 400..499 ->
                recordError("Rejected (${response.code()}) — not queued for retry.")
            else -> {
                updateQueue.enqueue(captured)
                val message = result.exceptionOrNull()?.message
                    ?: "Server error (${response?.code()}) — queued for retry."
                recordError(message)
            }
        }
    }

    /**
     * Sends every queued capture, oldest first. A retryable failure (network exception, or a
     * 5xx/no-response from the server) stops the flush and re-queues everything from that point
     * on, so a connectivity drop mid-flush doesn't lose what hasn't been sent yet. A 4xx
     * response is treated as permanent instead — most plausibly 403, from `POST /location`'s
     * `CanWriteLocation` check (server/Stores/SessionStore.cs) rejecting a queued capture for a
     * session the runner has since left. That capture can never become postable no matter how
     * many times it's retried, so it's dropped rather than requeued: without this, one
     * permanently-403'd entry queued ahead of genuinely retryable ones would jam the whole flush
     * on every future attempt, forever.
     */
    private suspend fun flushQueue() {
        val queued = updateQueue.drain()
        if (queued.isEmpty()) return

        queued.forEachIndexed { index, captured ->
            val response = runCatching { repository.postCapture(captured) }.getOrNull()
            when {
                response != null && response.isSuccessful -> Unit
                response != null && response.code() in 400..499 -> Unit // permanent — drop it
                else -> {
                    // Retryable — put this one and everything after it back, oldest first, then
                    // stop. forEachIndexed is inline, so this "return" exits flushQueue() itself,
                    // not just this iteration.
                    queued.drop(index).forEach(updateQueue::enqueue)
                    return
                }
            }
        }
    }

    private suspend fun DotWatcherRepository.postCapture(captured: QueuedCapture) =
        postLocation(
            sessionId = captured.sessionId,
            latitude = captured.latitude,
            longitude = captured.longitude,
            heading = captured.heading,
            timestamp = captured.timestamp,
            batteryLevel = captured.batteryLevel,
        )

    private fun recordSuccess() {
        _state.update { current ->
            if (current is TrackingState.Tracking && current.lastError != null) current.copy(lastError = null) else current
        }
    }

    private fun recordError(message: String?) {
        _state.update { current ->
            if (current is TrackingState.Tracking) current.copy(lastError = message) else current
        }
    }

    private fun stopTracking() {
        trackingJob?.cancel()
        trackingJob = null
        locationTracker.stopHeadingUpdates()
        locationTracker.stopLocationUpdates()
        shareChoices.clearSharing()
        _state.value = TrackingState.Stopped
    }

    override fun onDestroy() {
        stopTracking()
        serviceScope.cancel()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun startForegroundWithNotification() {
        val channel = NotificationChannel(
            NOTIFICATION_CHANNEL_ID,
            "Location sharing",
            NotificationManager.IMPORTANCE_LOW,
        )
        val notificationManager = getSystemService<NotificationManager>()
        notificationManager?.createNotificationChannel(channel)

        val openAppIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE,
        )

        val notification: Notification = NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setContentTitle("Sharing your location")
            .setContentText("Dot Watcher is sending your position to this session.")
            .setSmallIcon(android.R.drawable.ic_menu_mylocation)
            .setOngoing(true)
            .setContentIntent(openAppIntent)
            .build()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    companion object {
        private const val NOTIFICATION_CHANNEL_ID = "location_tracking"
        private const val NOTIFICATION_ID = 1
        private const val EXTRA_SESSION_ID = "session_id"
        private const val EXTRA_DURATION_SECONDS = "duration_seconds"
        private const val EXTRA_TOTAL_SECONDS = "total_seconds"
        private const val ACTION_EXTEND = "nz.skelstar.dotwatcher.action.EXTEND_TRACKING"
        private const val ACTION_SEND_NOW = "nz.skelstar.dotwatcher.action.SEND_NOW"
        private const val ACTION_RESUME_AUTO = "nz.skelstar.dotwatcher.action.RESUME_AUTO"

        private val _state = MutableStateFlow<TrackingState>(TrackingState.Stopped)
        val state: StateFlow<TrackingState> = _state.asStateFlow()

        /** [totalDuration] is only for resuming a share the runner already chose: [duration] is then
         *  what is left, and the total keeps the countdown arc measured against the real span. */
        fun start(context: Context, sessionId: String, duration: Duration, totalDuration: Duration = duration) {
            val intent = Intent(context, LocationTrackingService::class.java)
                .putExtra(EXTRA_SESSION_ID, sessionId)
                .putExtra(EXTRA_DURATION_SECONDS, duration.seconds)
                .putExtra(EXTRA_TOTAL_SECONDS, totalDuration.seconds)
            context.startForegroundService(intent)
        }

        /** Adds [extension] to the running session's auto-stop time. A no-op if nothing is being
         *  shared. The service is already in the foreground, so a plain `startService` suffices. */
        fun extend(context: Context, extension: Duration) {
            val intent = Intent(context, LocationTrackingService::class.java)
                .setAction(ACTION_EXTEND)
                .putExtra(EXTRA_DURATION_SECONDS, extension.seconds)
            context.startService(intent)
        }

        /** Battery card's "Send now": one manual position, tracking stays paused. */
        fun sendNow(context: Context) {
            context.startService(
                Intent(context, LocationTrackingService::class.java).setAction(ACTION_SEND_NOW)
            )
        }

        /** Battery card's "Auto": override the critical-battery pause for the rest of this session. */
        fun resumeAutomatic(context: Context) {
            context.startService(
                Intent(context, LocationTrackingService::class.java).setAction(ACTION_RESUME_AUTO)
            )
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, LocationTrackingService::class.java))
            _state.value = TrackingState.Stopped
        }
    }
}
