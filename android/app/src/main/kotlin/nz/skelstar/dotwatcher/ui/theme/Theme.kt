package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight

// Brand colors sourced from the actual app icon
// (ios/DotWatcher/DotWatcher/Assets.xcassets/AppIcon.appiconset/dotwatchr-icon-5a.png), not
// invented — iOS's in-app screens don't define a custom AccentColor (its colorset is empty, so
// SwiftUI falls back to plain system blue), so the icon is the only real brand reference. Sampled
// directly from the icon's pixels: the badge blue is exactly iOS's own system blue
// (UIColor.systemBlue / #007AFF), and the background green is the icon's topo-map fill (#A6D583).
private val BrandBlue = Color(0xFF007AFF)
private val BrandGreen = Color(0xFFA6D583)

// Material3 needs a full tonal palette, not just the two seed colors above — hand-derived tones
// (darker "on-*"/"container" shades, lighter surfaces) following Material3's standard
// light/dark contrast conventions, since generating one programmatically (e.g. dynamic color)
// would drop the specific brand hues sampled above.
private val LightColors = lightColorScheme(
    primary = BrandBlue,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFD6E8FF),
    onPrimaryContainer = Color(0xFF00274D),
    secondary = Color(0xFF3D6B2B), // darker shade of BrandGreen, readable as text/icons
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFDCF0CB), // light tint of BrandGreen
    onSecondaryContainer = Color(0xFF1A2E10),
    background = Color(0xFFFDFDFB),
    onBackground = Color(0xFF1A1C1E),
    surface = Color(0xFFFDFDFB),
    onSurface = Color(0xFF1A1C1E),
    surfaceVariant = Color(0xFFE1E3DE),
    onSurfaceVariant = Color(0xFF44483F),
    outline = Color(0xFF75796E),
    error = Color(0xFFBA1A1A),
    onError = Color.White,
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF9FC9FF), // lightened BrandBlue for dark-surface contrast
    onPrimary = Color(0xFF00325C),
    primaryContainer = Color(0xFF004881),
    onPrimaryContainer = Color(0xFFD6E8FF),
    secondary = Color(0xFFA1D48A), // lightened BrandGreen for dark-surface contrast
    onSecondary = Color(0xFF163A0A),
    secondaryContainer = Color(0xFF2A5220),
    onSecondaryContainer = Color(0xFFDCF0CB),
    background = Color(0xFF121311),
    onBackground = Color(0xFFE3E3DF),
    surface = Color(0xFF121311),
    onSurface = Color(0xFFE3E3DF),
    surfaceVariant = Color(0xFF44483F),
    onSurfaceVariant = Color(0xFFC5C8BC),
    outline = Color(0xFF8E9288),
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005),
)

// A light typographic pass — bolder headline weight matches iOS's frequent use of
// `.title2.bold()`/`.headline` for screen titles (CreateSessionView.swift,
// ShareLocationConsentView.swift), rather than Material3's default medium-weight headlines.
private val AppTypography = Typography().let { base ->
    base.copy(
        headlineMedium = base.headlineMedium.copy(fontWeight = FontWeight.Bold),
        headlineSmall = base.headlineSmall.copy(fontWeight = FontWeight.Bold),
        titleLarge = base.titleLarge.copy(fontWeight = FontWeight.Bold),
        titleMedium = base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    )
}

@Composable
fun DotWatcherTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    val colorScheme = if (darkTheme) DarkColors else LightColors
    MaterialTheme(
        colorScheme = colorScheme,
        typography = AppTypography,
        content = content,
    )
}
