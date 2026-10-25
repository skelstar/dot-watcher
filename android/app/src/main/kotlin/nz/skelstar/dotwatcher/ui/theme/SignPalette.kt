package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import nz.skelstar.dotwatcher.R

/** The start screen's "sign" colours, from the Coffee Go palette in .ai/mockups/landing-signs.html. */
object SignPalette {
    val Coffee = Color(0xFF2B2118)
    val Camel = Color(0xFFAF9164)
    val Ivory = Color(0xFFF7F3E3)
    val Lava = Color(0xFF6F1A07)
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
