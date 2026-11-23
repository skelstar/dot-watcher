package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.tween
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
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

/** Space Mono Regular (OFL, android/third_party): every sign's label, title and subtitle. Monospaced, so a title's
 *  width depends only on how many characters it has (see FittedSignTitle in ui/landing/SignButton.kt). */
val SignFontFamily = FontFamily(
    Font(R.font.space_mono_regular, weight = FontWeight.Normal),
)

/** Red Hat Text (OFL, android/third_party): the app's text font everywhere except the signs. Four static weights;
 *  Compose uses the nearest one for any weight in between (ExtraBold draws as Bold). Set on every style of the
 *  app's Typography (ui/theme/Theme.kt), so screens pick it up without naming it. */
val UiFontFamily = FontFamily(
    Font(R.font.red_hat_text_regular, weight = FontWeight.Normal),
    Font(R.font.red_hat_text_medium, weight = FontWeight.Medium),
    Font(R.font.red_hat_text_semibold, weight = FontWeight.SemiBold),
    Font(R.font.red_hat_text_bold, weight = FontWeight.Bold),
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
    /** The palette's display name from the server, shown beside the version as the release name. Null when built in or unnamed. */
    val name: String? = null,
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
 * The server's palette in use right now, or null to use [BuiltInSignColors]. Set at launch
 * ([nz.skelstar.dotwatcher.DotWatcherApplication.onCreate]), when the app comes back to the front, and as soon as a
 * fetch returns a different palette. Compose state, so the start screen redraws when it changes; the screens read it
 * through [animatedSignColors] so the change fades in.
 */
object AppliedSignColors {
    var palette by mutableStateOf<SignColors?>(null)
}

/** How long a new palette takes to fade in. */
const val PALETTE_FADE_MILLIS = 600

/** The colours the signs draw with: [AppliedSignColors], fading between palettes when it changes. */
@Composable
fun animatedSignColors(): SignColors {
    val target = AppliedSignColors.palette ?: BuiltInSignColors
    val spec = tween<Color>(PALETTE_FADE_MILLIS)
    return SignColors(
        goBg = animateColorAsState(target.goBg, spec, label = "goBg").value,
        goFg = animateColorAsState(target.goFg, spec, label = "goFg").value,
        goBar = animateColorAsState(target.goBar, spec, label = "goBar").value,
        altBg = animateColorAsState(target.altBg, spec, label = "altBg").value,
        altFg = animateColorAsState(target.altFg, spec, label = "altFg").value,
        accent = animateColorAsState(target.accent, spec, label = "accent").value,
        accentInk = animateColorAsState(target.accentInk, spec, label = "accentInk").value,
    )
}

/** The start screen's background: the palette's screen colour, or the theme's surface when there is none. */
@Composable
fun animatedScreenColor(): Color = animateColorAsState(
    AppliedSignColors.palette?.screen ?: MaterialTheme.colorScheme.surface,
    tween(PALETTE_FADE_MILLIS),
    label = "screen",
).value
