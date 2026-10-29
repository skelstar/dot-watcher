package nz.skelstar.dotwatcher.ui.landing

import androidx.compose.animation.core.Animatable
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onLongClick
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.ui.casual.holdToComplete
import nz.skelstar.dotwatcher.ui.theme.SignFontFamily
import nz.skelstar.dotwatcher.ui.theme.SignPalette

private val BarHeight = 12.dp
private val FrameWidth = 2.dp

/**
 * A full-width rectangular "sign" button for the start screen: title and a smaller line under it on
 * the left, an arrow on the right. [SignButton] is the cream one (Join, Create);
 * [HoldSignButton] is the berry Go sign.
 */
@Composable
fun SignButton(
    title: String,
    subtitle: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    SignContent(
        title = title,
        subtitle = subtitle,
        titleSize = 26,
        arrowSize = 30.dp,
        ink = SignPalette.Berry,
        modifier = modifier
            .fillMaxWidth()
            .background(SignPalette.Cream)
            .border(FrameWidth, SignPalette.Berry, RectangleShape)
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
    )
}

/**
 * The Go sign, about 2.2x the height of [SignButton] with a honey bar across the top. Held rather
 * than tapped, as the round Go button was: a lighter fill sweeps across while it is pressed and
 * [onComplete] runs when full, so a pocket can't start tracking. TalkBack gets a long-press action.
 */
@Composable
fun HoldSignButton(
    title: String,
    subtitle: String,
    onComplete: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val progress = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    val haptic = LocalHapticFeedback.current

    Column(
        modifier = modifier
            .fillMaxWidth()
            .semantics {
                contentDescription = "$title. $subtitle."
                onLongClick(label = "Start tracking") { onComplete(); true }
            },
    ) {
        Box(Modifier.fillMaxWidth().height(BarHeight * 1.5f).background(SignPalette.Honey))
        SignContent(
            title = title,
            subtitle = subtitle,
            titleSize = 44,
            arrowSize = 36.dp,
            ink = SignPalette.Honey,
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 162.dp)
                .background(SignPalette.Berry)
                .drawBehind {
                    drawRect(
                        SignPalette.Honey.copy(alpha = 0.28f),
                        size = Size(size.width * progress.value, size.height),
                    )
                }
                .pointerInput(Unit) {
                    detectTapGestures(onPress = { holdToComplete(progress, scope, haptic, onComplete) })
                }
                .padding(horizontal = 20.dp, vertical = 18.dp),
            alignBottom = true,
        )
    }
}

/**
 * A public event happening near the runner, shown under Go in amber with plum text and a plum bar so it
 * stands out from both the berry Go and the cream signs. No frame: it would make the sign look inset. [eyebrow] is the small caps line above the title
 * ("Happening near you . 2.3 km").
 */
@Composable
fun EventSignButton(
    eyebrow: String,
    title: String,
    subtitle: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(SignPalette.Amber)
            .clickable(role = Role.Button, onClick = onClick),
    ) {
        Box(Modifier.fillMaxWidth().height(BarHeight).background(SignPalette.Plum))
        SignContent(
            title = title,
            subtitle = subtitle,
            titleSize = 28,
            arrowSize = 30.dp,
            ink = SignPalette.Plum,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
            eyebrow = eyebrow,
        )
    }
}

@Composable
private fun SignContent(
    title: String,
    subtitle: String,
    titleSize: Int,
    arrowSize: Dp,
    ink: Color,
    modifier: Modifier = Modifier,
    alignBottom: Boolean = false,
    eyebrow: String? = null,
) {
    Row(
        modifier = modifier,
        verticalAlignment = if (alignBottom) Alignment.Bottom else Alignment.CenterVertically,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            eyebrow?.let {
                Text(
                    text = it.uppercase(),
                    color = ink,
                    fontFamily = SignFontFamily,
                    fontWeight = FontWeight.ExtraBold,
                    fontSize = 11.sp,
                    letterSpacing = 1.sp,
                )
            }
            Text(
                text = title,
                color = ink,
                fontFamily = SignFontFamily,
                fontWeight = FontWeight.ExtraBold,
                fontSize = titleSize.sp,
                lineHeight = (titleSize * 1.05f).sp,
            )
            Text(
                text = subtitle,
                color = ink,
                fontFamily = SignFontFamily,
                fontWeight = FontWeight.ExtraBold,
                fontSize = 15.sp,
                lineHeight = 18.sp,
                modifier = Modifier.padding(top = 3.dp),
            )
        }
        Spacer(Modifier.width(12.dp))
        Icon(
            imageVector = Icons.AutoMirrored.Filled.ArrowForward,
            contentDescription = null,
            tint = ink,
            modifier = Modifier.size(arrowSize),
        )
    }
}
