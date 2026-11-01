package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import nz.skelstar.dotwatcher.R

/** The start screen's "sign" colours, from the Navy and orange palette (Palette 2) in .ai/mockups/landing-signs.html. */
object SignPalette {
    val Navy = Color(0xFF1F3350)
    val Sand = Color(0xFFF2EFE4)
    val Cream = Color(0xFFF6F1E4)
    val Orange = Color(0xFFF28C28)
    val Ink = Color(0xFF1F2A3D)
}

/** EB Garamond (OFL, android/third_party) at ExtraBold, the nearest free face to the ITC Garamond
 *  on the park signs. One variable font file; variation settings need API 26, our minSdk. */
@OptIn(androidx.compose.ui.text.ExperimentalTextApi::class)
val SignFontFamily = FontFamily(
    Font(
        R.font.eb_garamond,
        weight = FontWeight.ExtraBold,
        variationSettings = FontVariation.Settings(FontVariation.weight(800)),
    ),
)

/**
 * Every colour the start screen's signs and background use. The built-in set is [BuiltInSignColors];
 * a palette from the server (see data/RemotePalette.kt) replaces it as a whole, never in part.
 * [screen] is null for the built-in colours, which leave the theme's own background alone.
 */
data class SignColors(
    val goBg: Color,
    val goFg: Color,
    val goBar: Color,
    val altBg: Color,
    val altFg: Color,
    val accent: Color,
    val accentInk: Color,
    val screen: Color? = null,
)

val BuiltInSignColors = SignColors(
    goBg = SignPalette.Navy,
    goFg = SignPalette.Sand,
    goBar = SignPalette.Sand,
    altBg = SignPalette.Cream,
    altFg = SignPalette.Navy,
    accent = SignPalette.Orange,
    accentInk = SignPalette.Ink,
)

/**
 * The server's palette for this run of the app, or null to use [BuiltInSignColors]. Set once, in
 * [nz.skelstar.dotwatcher.DotWatcherApplication.onCreate], before any screen draws, so a new palette shows from
 * the next launch and never changes part-way through.
 */
object AppliedSignColors {
    @Volatile
    var palette: SignColors? = null
}

val currentSignColors: SignColors
    get() = AppliedSignColors.palette ?: BuiltInSignColors
