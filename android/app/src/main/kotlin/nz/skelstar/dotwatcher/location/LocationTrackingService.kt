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
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.MainActivity
import nz.skelstar.dotwatcher.data.AuthTokenStore
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import java.time.Duration
import java.time.Instant

/** Normal post cadence, matching iOS's `normalInterval`
 *  (ios/DotWatcher/DotWatcher/LocationManager.swift:201). Android has no satellite-tier
 *  connectivity signal yet, so unlike iOS there is no slower fallback cadence for now. */
private const val POST_INTERVAL_SECONDS = 15L

/** Options offered for how long a share session runs before it auto-stops, ascending — mirrors
 *  `LocationManager.trackingDurationOptions` (ios/DotWatcher/DotWatcher/LocationManager.swift:212).
 *  This is a cap on *automatic* sharing, not a mode switch: every option still auto-stops, the
 *  runner just picks how soon. */
val TRACKING_DURATION_OPTIONS: List<Duration> = listOf(2L, 4L, 8L, 24L).map { Duration.ofHours(it) }

sealed interface TrackingState {
    data object Stopped : TrackingState
    data class Tracking(val expiresAt: Instant, val lastError: String? = null) : TrackingState
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

    private lateinit var locationTracker: LocationTracker
    private lateinit var repository: DotWatcherRepository
    private lateinit var updateQueue: LocationUpdateQueue

    override fun onCreate() {
        super.onCreate()
        locationTracker = LocationTracker(applicationContext)
        val tokenStore = AuthTokenStore(applicationContext)
        val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL)
        repository = DotWatcherRepository(api, tokenStore)
        updateQueue = LocationUpdateQueue(applicationContext)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val sessionId = intent?.getStringExtra(EXTRA_SESSION_ID)
        val durationSeconds = intent?.getLongExtra(EXTRA_DURATION_SECONDS, -1) ?: -1

        if (sessionId == null || durationSeconds <= 0) {
            stopSelf()
            return START_NOT_STICKY
        }

        startForegroundWithNotification()
        startTracking(sessionId, Duration.ofSeconds(durationSeconds))

        // Not START_STICKY: if the OS kills this process, tracking should stay stopped rather
        // than silently restart without the runner's knowledge — matches the explicit-start,
        // explicit-stop model of a bounded share session (see TRACKING_DURATION_OPTIONS's kdoc).
        return START_NOT_STICKY
    }

    private fun startTracking(sessionId: String, duration: Duration) {
        trackingJob?.cancel()
        locationTracker.startHeadingUpdates()

        val expiresAt = Instant.now().plus(duration)
        _state.value = TrackingState.Tracking(expiresAt)

        trackingJob = serviceScope.launch {
            while (isActive) {
                val now = Instant.now()
                if (now >= expiresAt) {
                    stopTracking()
                    stopSelf()
                    break
                }

                runCatching {
                    val capture = locationTracker.captureOnce()
                    val postedAt = Instant.now()
                    QueuedCapture(
                        sessionId = sessionId,
                        latitude = capture.latitude,
                        longitude = capture.longitude,
                        heading = capture.heading,
                        timestamp = postedAt.toString(),
                    )
                }.fold(
                    onSuccess = { captured -> postOrEnqueue(captured) },
                    onFailure = { error -> recordError(error.message) },
                )

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
        val response = result.getOrNull()
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
        )

    private fun recordSuccess() {
        val current = _state.value
        if (current is TrackingState.Tracking && current.lastError != null) {
            _state.value = current.copy(lastError = null)
        }
    }

    private fun recordError(message: String?) {
        val current = _state.value
        if (current is TrackingState.Tracking) {
            _state.value = current.copy(lastError = message)
        }
    }

    private fun stopTracking() {
        trackingJob?.cancel()
        trackingJob = null
        locationTracker.stopHeadingUpdates()
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

        private val _state = MutableStateFlow<TrackingState>(TrackingState.Stopped)
        val state: StateFlow<TrackingState> = _state.asStateFlow()

        fun start(context: Context, sessionId: String, duration: Duration) {
            val intent = Intent(context, LocationTrackingService::class.java)
                .putExtra(EXTRA_SESSION_ID, sessionId)
                .putExtra(EXTRA_DURATION_SECONDS, duration.seconds)
            context.startForegroundService(intent)
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, LocationTrackingService::class.java))
            _state.value = TrackingState.Stopped
        }
    }
}
