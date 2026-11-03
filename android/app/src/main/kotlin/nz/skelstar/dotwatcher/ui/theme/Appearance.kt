package nz.skelstar.dotwatcher.ui.theme

import android.content.Context
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.Composable

/** The Settings "Appearance" choice, as on iOS: follow the phone, or force light or dark. */
enum class AppearanceMode(val label: String) {
    System("System"),
    Light("Light"),
    Dark("Dark"),
    ;

    @Composable
    fun isDark(): Boolean = when (this) {
        System -> isSystemInDarkTheme()
        Light -> false
        Dark -> true
    }
}

/** The chosen [AppearanceMode], kept on this phone. Plain SharedPreferences: not sensitive, and it is
 *  deliberately not cleared on sign-out (iOS keeps it too). */
class AppearancePrefs(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var mode: AppearanceMode
        get() = AppearanceMode.entries.firstOrNull { it.name == prefs.getString(KEY_MODE, null) }
            ?: AppearanceMode.System
        set(value) = prefs.edit().putString(KEY_MODE, value.name).apply()

    private companion object {
        const val PREFS_NAME = "dotwatcher_appearance"
        const val KEY_MODE = "mode"
    }
}
