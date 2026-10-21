package nz.skelstar.dotwatcher.location

import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager

/** Battery percentage (0-100, or null if the OS can't say) and whether the device is on power. */
data class BatteryStatus(val level: Int?, val charging: Boolean)

/** Reads the sticky `ACTION_BATTERY_CHANGED` broadcast — no permission and no receiver needed,
 *  `registerReceiver(null, …)` just returns the last value. */
object BatteryReader {
    /** Critical-battery threshold in percent. Same number as iOS's `criticalBatteryThreshold`
     *  and the web client's `CRITICAL_BATTERY_THRESHOLD`; kept in sync by hand. */
    const val CRITICAL_THRESHOLD = 10

    fun read(context: Context): BatteryStatus {
        val intent = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
            ?: return BatteryStatus(level = null, charging = false)
        val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
        val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
        val status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
        val percent = if (level >= 0 && scale > 0) (level * 100 / scale) else null
        val charging = status == BatteryManager.BATTERY_STATUS_CHARGING ||
            status == BatteryManager.BATTERY_STATUS_FULL
        return BatteryStatus(percent, charging)
    }

    /** True when automatic tracking should pause: at/below the threshold and not charging. An
     *  unknown level is never treated as critical (matches the server's "null ≠ dead battery"). */
    fun isCritical(status: BatteryStatus): Boolean =
        !status.charging && (status.level?.let { it <= CRITICAL_THRESHOLD } ?: false)
}
