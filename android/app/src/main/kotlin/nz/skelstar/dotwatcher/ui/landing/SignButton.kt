package nz.skelstar.dotwatcher.ui.landing

import android.provider.Settings
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
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
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onLongClick
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.ui.casual.holdToComplete
import nz.skelstar.dotwatcher.ui.theme.SignFontFamily
import nz.skelstar.dotwatcher.ui.theme.animatedSignColors

private val BarHeight = 12.dp
private val FrameWidth = 2.dp

/**
 * A full-width rectangular "sign" button for the start screen: title and a smaller line under it on
 * the left, an arrow on the right. [SignButton] is the cream one (Join, Create);
 * [HoldSignButton] is the navy Go sign.
 */
@Composable
fun SignButton(
    title: String,
    subtitle: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val colors = animatedSignColors()
    SignContent(
        title = title,
        subtitle = subtitle,
        titleSize = 26,
        arrowSize = 30.dp,
        ink = colors.altFg,
        modifier = modifier
            .fillMaxWidth()
            .background(colors.altBg)
            .border(FrameWidth, colors.altFg, RectangleShape)
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
    )
}

/**
 * The Go sign, about 2.2x the height of [SignButton] with a sand bar across the top. Held rather
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
    val colors = animatedSignColors()
    // Read through derivedStateOf so the fill's frames don't recompose the sign, only the start and end.
    val held by remember { derivedStateOf { progress.value > 0f } }

    Column(
        modifier = modifier
            .fillMaxWidth()
            .semantics {
                contentDescription = "$title. $subtitle."
                onLongClick(label = "Start tracking") { onComplete(); true }
            },
    ) {
        Box(Modifier.fillMaxWidth().height(BarHeight * 1.5f).background(colors.goBar))
        SignContent(
            title = title,
            subtitle = subtitle,
            titleSize = 44,
            arrowSize = 36.dp,
            ink = colors.goFg,
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 162.dp)
                .background(colors.goBg)
                .drawBehind {
                    drawRect(
                        colors.goFg.copy(alpha = 0.28f),
                        size = Size(size.width * progress.value, size.height),
                    )
                }
                .pointerInput(Unit) {
                    detectTapGestures(onPress = { holdToComplete(progress, scope, haptic, onComplete) })
                }
                .padding(horizontal = 20.dp, vertical = 18.dp),
            alignBottom = true,
            nudging = !held,
        )
    }
}

/**
 * A public event happening near the runner, shown under Go in orange with ink text and a ink bar so it
 * stands out from both the navy Go and the cream signs. No frame: it would make the sign look inset. [eyebrow] is the small caps line above the title
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
    val colors = animatedSignColors()
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(colors.accent)
            .clickable(role = Role.Button, onClick = onClick),
    ) {
        Box(Modifier.fillMaxWidth().height(BarHeight).background(colors.accentInk))
        SignContent(
            title = title,
            subtitle = subtitle,
            titleSize = 28,
            arrowSize = 30.dp,
            ink = colors.accentInk,
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
    nudging: Boolean = false,
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
        val arrow = Modifier.size(arrowSize)
        Icon(
            imageVector = Icons.AutoMirrored.Filled.ArrowForward,
            contentDescription = null,
            tint = ink,
            modifier = if (nudging && animationsEnabled()) arrow.nudge() else arrow,
        )
    }
}

private const val NudgeMillis = 3500
private const val NudgeStepMillis = 210
private const val NudgePush = 7f

/**
 * Two quick 7dp pushes toward the arrow's direction, then a rest, every 3.5 seconds: a small "this
 * way" on the Go arrow. Moves the drawing only, so the sign's layout doesn't change.
 */
@Composable
private fun Modifier.nudge(): Modifier {
    val x by rememberInfiniteTransition(label = "arrow nudge").animateFloat(
        initialValue = 0f,
        targetValue = 0f,
        animationSpec = infiniteRepeatable(
            keyframes {
                durationMillis = NudgeMillis
                NudgePush at NudgeStepMillis using FastOutSlowInEasing
                0f at NudgeStepMillis * 2 using FastOutSlowInEasing
                NudgePush at NudgeStepMillis * 3 using FastOutSlowInEasing
                0f at NudgeStepMillis * 4
            },
        ),
        label = "arrow nudge x",
    )
    val rtl = LocalLayoutDirection.current == LayoutDirection.Rtl
    return graphicsLayer { translationX = x.dp.toPx() * if (rtl) -1 else 1 }
}

/** False when the system's animator duration scale is off (Settings > Accessibility > Remove animations). */
@Composable
private fun animationsEnabled(): Boolean {
    val resolver = LocalContext.current.contentResolver
    return remember(resolver) {
        Settings.Global.getFloat(resolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) != 0f
    }
}
