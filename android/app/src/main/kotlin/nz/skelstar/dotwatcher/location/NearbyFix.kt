package nz.skelstar.dotwatcher.location

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import com.google.android.gms.location.CurrentLocationRequest
import com.google.android.gms.location.Granularity
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import kotlinx.coroutines.tasks.await

/** A position for a nearby-join call. Plain data, so the models that carry it don't depend on Android. */
data class Fix(val latitude: Double, val longitude: Double, val accuracyMetres: Double?)

/**
 * One current position on demand, for finding and joining runs near the person and for the organiser's join
 * location. It never starts continuous updates: tracking is [LocationTracker]'s job, and only while sharing.
 * The app already asks for location when it opens (MainActivity), so this asks for nothing itself.
 */
class NearbyFixSource(context: Context) {
    private val appContext = context.applicationContext
    private val fusedLocationClient = LocationServices.getFusedLocationProviderClient(appContext)

    private fun granted(permission: String) =
        ContextCompat.checkSelfPermission(appContext, permission) == PackageManager.PERMISSION_GRANTED

    val hasPermission: Boolean
        get() = granted(Manifest.permission.ACCESS_FINE_LOCATION) || granted(Manifest.permission.ACCESS_COARSE_LOCATION)

    /** With only approximate location the fused provider's fixes are blurred to a kilometre or more, far too
     *  coarse to place or reach a join location within 50 to 500 m. */
    val hasPreciseLocation: Boolean
        get() = granted(Manifest.permission.ACCESS_FINE_LOCATION)

    /** The current position, or null without permission or when none arrives in time. */
    @SuppressLint("MissingPermission") // Checked just above.
    suspend fun current(): Fix? {
        if (!hasPermission) return null
        val request = CurrentLocationRequest.Builder()
            .setPriority(Priority.PRIORITY_HIGH_ACCURACY)
            .setGranularity(Granularity.GRANULARITY_FINE)
            .setMaxUpdateAgeMillis(MAX_FIX_AGE_MS)
            .setDurationMillis(FIX_TIMEOUT_MS)
            .build()
        val location = runCatching { fusedLocationClient.getCurrentLocation(request, null).await() }.getOrNull()
            ?: return null
        return Fix(
            latitude = location.latitude,
            longitude = location.longitude,
            accuracyMetres = if (location.hasAccuracy()) location.accuracy.toDouble() else null,
        )
    }

    private companion object {
        const val MAX_FIX_AGE_MS = 10_000L
        const val FIX_TIMEOUT_MS = 10_000L
    }
}
