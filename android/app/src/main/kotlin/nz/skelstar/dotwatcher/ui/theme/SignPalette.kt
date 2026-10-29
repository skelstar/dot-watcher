package nz.skelstar.dotwatcher.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontVariation
import androidx.compose.ui.text.font.FontWeight
import nz.skelstar.dotwatcher.R

/** The start screen's "sign" colours, from the Berry and cream palette (Palette 5) in .ai/mockups/landing-signs.html. */
object SignPalette {
    val Berry = Color(0xFF5A1F3A)
    val Honey = Color(0xFFF6D58A)
    val Cream = Color(0xFFF8EFE2)
    val Amber = Color(0xFFE9B949)
    val Plum = Color(0xFF3D1226)
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
