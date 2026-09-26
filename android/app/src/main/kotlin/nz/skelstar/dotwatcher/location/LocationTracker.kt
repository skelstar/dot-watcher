package nz.skelstar.dotwatcher.location

import android.annotation.SuppressLint
import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import com.google.android.gms.location.CurrentLocationRequest
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import kotlinx.coroutines.tasks.await

/** A single capture: GPS fix plus whatever heading the compass had at that moment, if any. */
data class Capture(
    val latitude: Double,
    val longitude: Double,
    /** True-north compass bearing in degrees (0-360), or null when unavailable — e.g. the
     *  runner is stationary and the rotation-vector sensor hasn't settled. Mirrors iOS's rule
     *  of using the device compass directly rather than deriving bearing from consecutive fixes
     *  (see repo root README.md, "Position payload"). */
    val heading: Double?,
)

/**
 * Foreground-only location + heading capture for Milestone 1. Uses
 * [com.google.android.gms.location.FusedLocationProviderClient] for position (Android's rough
 * equivalent of iOS's CLLocationManager) and the rotation-vector sensor for heading — never
 * derived from consecutive GPS fixes, matching iOS/web.
 *
 * Caller must already hold ACCESS_FINE_LOCATION (or ACCESS_COARSE_LOCATION) before calling
 * [captureOnce]. Background operation (screen off, app backgrounded) is Milestone 2's job — see
 * .ai/plans/android-app.md.
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

    /**
     * Requests one fresh GPS fix. Suspends until the fix arrives (or throws on failure/timeout);
     * callers on a fixed timer should call this each tick rather than holding open a continuous
     * location stream, which is the simplest correct behavior while tracking is foreground-only.
     */
    @SuppressLint("MissingPermission") // Caller is required to have checked permissions first.
    suspend fun captureOnce(): Capture {
        val request = CurrentLocationRequest.Builder()
            .setPriority(Priority.PRIORITY_HIGH_ACCURACY)
            .build()
        val location = fusedLocationClient.getCurrentLocation(request, null).await()
            ?: error("No location available (GPS fix not yet acquired).")

        return Capture(
            latitude = location.latitude,
            longitude = location.longitude,
            heading = latestHeading,
        )
    }
}
