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
 * only ran while LiveMapScreen was on-screen and the process wasn't suspended).
 *
 * State is exposed via the [state] singleton flow rather than a bound-service interface, since
 * the UI only needs to observe progress/errors, not call back into the service directly.
 */
class LocationTrackingService : Service() {
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var trackingJob: Job? = null

    private lateinit var locationTracker: LocationTracker
    private lateinit var repository: DotWatcherRepository

    override fun onCreate() {
        super.onCreate()
        locationTracker = LocationTracker(applicationContext)
        val tokenStore = AuthTokenStore(applicationContext)
        val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL)
        repository = DotWatcherRepository(api, tokenStore)
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
                    repository.postLocation(
                        sessionId = sessionId,
                        latitude = capture.latitude,
                        longitude = capture.longitude,
                        heading = capture.heading,
                        timestamp = postedAt.toString(),
                    )
                }.onFailure { error ->
                    val current = _state.value
                    if (current is TrackingState.Tracking) {
                        _state.value = current.copy(lastError = error.message)
                    }
                }

                val beforeDelay = Instant.now()
                val delayMillis = Duration.between(beforeDelay, nextPostAt(beforeDelay, POST_INTERVAL_SECONDS))
                    .toMillis()
                    .coerceAtLeast(0)
                delay(delayMillis)
            }
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
