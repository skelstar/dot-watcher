package nz.skelstar.dotwatcher

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat

/**
 * Android's location background-tracking permissions come in a strict sequence a single request
 * can't collapse: foreground location first, then (only once granted) background location as a
 * separate ask, per Android 10+'s policy — see .ai/plans/android-app.md's Milestone 2 notes. iOS
 * grants the CLLocationManager-equivalent of both in one prompt (README.md, "ios" section), so
 * this multi-step dance has no direct iOS analogue.
 */
class MainActivity : ComponentActivity() {
    private var hasBackgroundLocationPermission by mutableStateOf(false)

    private val requestForegroundLocation = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { grants ->
        val foregroundGranted = grants[Manifest.permission.ACCESS_FINE_LOCATION] == true ||
            grants[Manifest.permission.ACCESS_COARSE_LOCATION] == true
        if (foregroundGranted) {
            requestBackgroundLocationIfNeeded()
        }
    }

    private val requestBackgroundLocation = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        hasBackgroundLocationPermission = granted
        if (granted) requestBatteryOptimizationExemptionIfNeeded()
    }

    private val requestNotificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* Notification denial only affects whether the tracking notification is visible, not
          whether tracking itself works — nothing to gate on here. */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            requestNotificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }

        if (isForegroundLocationGranted()) {
            hasBackgroundLocationPermission = isBackgroundLocationGranted()
            if (!hasBackgroundLocationPermission) {
                requestBackgroundLocationIfNeeded()
            } else {
                requestBatteryOptimizationExemptionIfNeeded()
            }
        } else {
            requestForegroundLocation.launch(
                arrayOf(Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION)
            )
        }

        setContent {
            DotWatcherApp(hasBackgroundLocationPermission = hasBackgroundLocationPermission)
        }
    }

    private fun requestBackgroundLocationIfNeeded() {
        if (isBackgroundLocationGranted()) {
            hasBackgroundLocationPermission = true
            requestBatteryOptimizationExemptionIfNeeded()
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            requestBackgroundLocation.launch(Manifest.permission.ACCESS_BACKGROUND_LOCATION)
        } else {
            // Below Android 10, foreground location grants background access too — no separate
            // permission exists.
            hasBackgroundLocationPermission = true
            requestBatteryOptimizationExemptionIfNeeded()
        }
    }

    /**
     * OEM battery optimization is the single biggest platform-parity risk versus iOS (see
     * .ai/plans/android-app.md) — this exemption prompt only removes the *stock* Android Doze/App
     * Standby restriction. Samsung/Xiaomi/etc.'s own "let this app run in background" toggles are
     * separate, vendor-specific settings screens this can't reach, and are left as a manual step
     * for the runner (worth surfacing in-app copy in a later milestone).
     */
    private fun requestBatteryOptimizationExemptionIfNeeded() {
        val powerManager = getSystemService(PowerManager::class.java) ?: return
        if (powerManager.isIgnoringBatteryOptimizations(packageName)) return

        val intent = Intent(
            Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
            Uri.parse("package:$packageName"),
        )
        // Some OEM/Play builds restrict this action and have nothing to resolve it, which would
        // otherwise throw ActivityNotFoundException — treat "can't ask" the same as "declined".
        if (intent.resolveActivity(packageManager) != null) {
            startActivity(intent)
        }
    }

    private fun isForegroundLocationGranted(): Boolean =
        ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_COARSE_LOCATION) ==
            PackageManager.PERMISSION_GRANTED

    private fun isBackgroundLocationGranted(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.Q ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_BACKGROUND_LOCATION) ==
            PackageManager.PERMISSION_GRANTED
}
