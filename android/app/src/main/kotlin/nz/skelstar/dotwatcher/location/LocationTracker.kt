package nz.skelstar.dotwatcher.location

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.location.Location
import android.os.Looper
import android.os.SystemClock
import android.util.Log
import androidx.core.content.ContextCompat
import com.google.android.gms.location.CurrentLocationRequest
import com.google.android.gms.location.Granularity
import com.google.android.gms.location.LocationCallback
import com.google.android.gms.location.LocationRequest
import com.google.android.gms.location.LocationResult
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import kotlinx.coroutines.tasks.await
import java.time.Instant

private const val TAG = "LocationTracker"

/** Longest we wait for the fused provider to produce a fix before giving up on this tick. */
private const val FIX_TIMEOUT_MS = 10_000L

/** Oldest a fix may be and still count as "now". Anything older is a cached location being
 *  handed back, which would otherwise be posted as if the runner were still standing there.
 *  Must exceed [UPDATE_INTERVAL_MS] so the latest streamed fix is still usable at the next tick. */
private const val MAX_FIX_AGE_MS = 15_000L

/** How often the fused provider streams fixes while tracking. Kept running between ticks so the
 *  GPS stays warm — a cold one-shot request each tick often missed its window under tree cover. */
private const val UPDATE_INTERVAL_MS = 10_000L
private const val MIN_UPDATE_INTERVAL_MS = 5_000L

/** Worst horizontal accuracy (metres, 68% confidence radius) we'll post. Cell/Wi-Fi-derived fixes
 *  are typically hundreds of metres to kilometres; a real GPS fix on a walk is well under this. */
private const val MAX_ACCURACY_METRES = 100f

/** A single capture: GPS fix plus whatever heading the compass had at that moment, if any. */
data class Capture(
    val latitude: Double,
    val longitude: Double,
    /** True-north compass bearing in degrees (0-360), or null when unavailable — e.g. the
     *  runner is stationary and the rotation-vector sensor hasn't settled. Mirrors iOS's rule
     *  of using the device compass directly rather than deriving bearing from consecutive fixes
     *  (see repo root README.md, "Position payload"). */
    val heading: Double?,
    /** When the fix was actually taken (the location's own time), not when it was posted — so a
     *  late or cached fix can't masquerade as a current one. */
    val fixedAt: Instant,
)

/**
 * One-shot location + heading capture, used from [LocationTrackingService]'s tracking loop. Uses
 * [com.google.android.gms.location.FusedLocationProviderClient] for position (Android's rough
 * equivalent of iOS's CLLocationManager) and the rotation-vector sensor for heading — never
 * derived from consecutive GPS fixes, matching iOS/web.
 *
 * Caller must already hold ACCESS_FINE_LOCATION (or ACCESS_COARSE_LOCATION) before calling
 * [captureOnce]; running from a foreground service (rather than a plain background app) is what
 * makes this call reliable with the screen off — see .ai/plans/android-app.md's Milestone 2 notes.
 */
class LocationTracker(context: Context) {
    private val appContext = context.applicationContext
    private val fusedLocationClient = LocationServices.getFusedLocationProviderClient(appContext)
    private val sensorManager = appContext.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val rotationVectorSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR)

    private var latestHeading: Double? = null
    private val headingListener = object : SensorEventListener {
        override fun onSensorChanged(event: SensorEvent) {
            val rotationMatrix = FloatArray(9)
            SensorManager.getRotationMatrixFromVector(rotationMatrix, event.values)
            val orientation = FloatArray(3)
            SensorManager.getOrientation(rotationMatrix, orientation)
            val azimuthRadians = orientation[0]
            val azimuthDegrees = Math.toDegrees(azimuthRadians.toDouble())
            latestHeading = (azimuthDegrees + 360.0) % 360.0
        }

        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit
    }

    fun startHeadingUpdates() {
        rotationVectorSensor?.let {
            sensorManager.registerListener(headingListener, it, SensorManager.SENSOR_DELAY_NORMAL)
        }
    }

    fun stopHeadingUpdates() {
        sensorManager.unregisterListener(headingListener)
    }

    /** Most recent fix streamed by [startLocationUpdates]; read by [captureOnce]. */
    @Volatile private var latestLocation: Location? = null
    private val locationCallback = object : LocationCallback() {
        override fun onLocationResult(result: LocationResult) {
            result.lastLocation?.let { latestLocation = it }
        }
    }
    private var updatesActive = false
    private val plausibility = FixPlausibilityFilter()

    /** Logs why a tick is being skipped (visible in logcat) and aborts the capture with the same
     *  message, which the service surfaces as the session's last error. */
    private fun skip(reason: String): Nothing {
        Log.w(TAG, "skipped tick: $reason")
        error(reason)
    }

    /** Starts streaming fixes so the GPS stays warm between ticks. Idempotent; no-op without
     *  precise location (captureOnce reports that error itself). */
    @SuppressLint("MissingPermission") // Guarded by hasPreciseLocation().
    fun startLocationUpdates() {
        if (updatesActive || !hasPreciseLocation()) return
        val request = LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, UPDATE_INTERVAL_MS)
            .setMinUpdateIntervalMillis(MIN_UPDATE_INTERVAL_MS)
            .setGranularity(Granularity.GRANULARITY_FINE)
            .build()
        fusedLocationClient.requestLocationUpdates(request, locationCallback, Looper.getMainLooper())
        updatesActive = true
    }

    fun stopLocationUpdates() {
        if (!updatesActive) return
        fusedLocationClient.removeLocationUpdates(locationCallback)
        updatesActive = false
        latestLocation = null
        synchronized(plausibility) { plausibility.reset() }
    }

    /** The streamed fix if it is recent enough, else null. */
    private fun freshStreamedLocation(): Location? =
        latestLocation?.takeIf { ageMillis(it) <= MAX_FIX_AGE_MS }

    private fun ageMillis(location: Location): Long =
        (SystemClock.elapsedRealtimeNanos() - location.elapsedRealtimeNanos) / 1_000_000

    /** True when the runner granted precise (fine) location, not just "approximate". */
    fun hasPreciseLocation(): Boolean =
        ContextCompat.checkSelfPermission(appContext, Manifest.permission.ACCESS_FINE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    /**
     * Requests one fresh GPS fix. Suspends until the fix arrives (or throws on failure/timeout);
     * callers on a fixed timer should call this each tick rather than holding open a continuous
     * location stream, which is the simplest correct behavior while tracking is foreground-only.
     *
     * Throws (rather than returning a poor position) when precise location isn't granted, no fix
     * arrives within [FIX_TIMEOUT_MS], the fix is older than [MAX_FIX_AGE_MS] or less accurate
     * than [MAX_ACCURACY_METRES], or it implies an impossible jump from the last accepted fix
     * ([FixPlausibilityFilter]) — a skipped tick is better than a dot in the wrong place.
     */
    @SuppressLint("MissingPermission") // Caller is required to have checked permissions first.
    suspend fun captureOnce(): Capture {
        // With only approximate location the fused provider returns blurred, slowly-updating
        // positions (often kilometres off and repeating), so refuse rather than post them.
        if (!hasPreciseLocation()) {
            skip("Precise location is off — turn on \"Use precise location\" for Dot Watcher in Settings.")
        }

        // Prefer the streamed fix; fall back to a one-shot request when updates aren't running
        // (e.g. a battery-paused manual "Send now") or nothing recent has arrived.
        val streamed = freshStreamedLocation()
        val source = if (streamed != null) "stream" else "one-shot"
        val location = streamed ?: run {
            val request = CurrentLocationRequest.Builder()
                .setPriority(Priority.PRIORITY_HIGH_ACCURACY)
                .setGranularity(Granularity.GRANULARITY_FINE)
                .setMaxUpdateAgeMillis(0)
                .setDurationMillis(FIX_TIMEOUT_MS)
                .build()
            fusedLocationClient.getCurrentLocation(request, null).await()
        } ?: skip("No GPS fix yet — will retry.")

        val ageMs = ageMillis(location)
        if (ageMs > MAX_FIX_AGE_MS) {
            skip("Only an old location (${ageMs / 1000}s) was available — will retry.")
        }
        if (!location.hasAccuracy() || location.accuracy > MAX_ACCURACY_METRES) {
            val detail = if (location.hasAccuracy()) "${location.accuracy.toInt()} m" else "unknown"
            skip("GPS accuracy too low ($detail) — will retry.")
        }

        val verdict = synchronized(plausibility) {
            plausibility.check(
                FixPoint(
                    latitude = location.latitude,
                    longitude = location.longitude,
                    accuracyMetres = location.accuracy,
                    elapsedSeconds = location.elapsedRealtimeNanos / 1_000_000_000.0,
                )
            )
        }
        if (verdict is Plausibility.Rejected) {
            skip("Position jumped ${verdict.distanceMetres.toInt()} m (limit ${verdict.allowedMetres.toInt()} m) — will retry.")
        }

        Log.d(TAG, "posted fix: source=$source accuracy=${location.accuracy.toInt()}m age=${ageMs / 1000}s")

        return Capture(
            latitude = location.latitude,
            longitude = location.longitude,
            heading = latestHeading,
            fixedAt = Instant.ofEpochMilli(location.time),
        )
    }
}
