package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Placeholder palette; revisit alongside the iOS/web visual language (RunnerColorPalette.swift)
// once the map screen lands in Milestone 1.
private val DotWatcherGreen = Color(0xFF1B4332)
private val DotWatcherYellow = Color(0xFFFFD60A)

private val LightColors = lightColorScheme(
    primary = DotWatcherGreen,
    secondary = DotWatcherYellow,
)

private val DarkColors = darkColorScheme(
    primary = DotWatcherYellow,
    secondary = DotWatcherGreen,
)

@Composable
fun DotWatcherTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val colorScheme = if (darkTheme) DarkColors else LightColors
    MaterialTheme(
        colorScheme = colorScheme,
        content = content,
    )
}
