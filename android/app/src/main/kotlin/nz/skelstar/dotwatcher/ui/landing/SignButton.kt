package nz.skelstar.dotwatcher.ui.landing

import android.graphics.Paint
import android.graphics.Rect
import android.graphics.Typeface
import android.provider.Settings
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.keyframes
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.foundation.Canvas
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
import androidx.compose.material3.LocalTextStyle
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
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalFontFamilyResolver
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onLongClick
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
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
 * the left, an icon on the right. [SignButton] is the cream one (Join, Create);
 * [HoldSignButton] is the navy Go sign.
 */
@Composable
fun SignButton(
    title: String,
    subtitle: String,
    icon: SignIcon,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    val colors = animatedSignColors()
    SignContent(
        title = title,
        subtitle = subtitle,
        titleSize = 26,
        icon = icon,
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
            icon = SignIcon.Chevrons,
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
            icon = SignIcon.Chevrons,
            ink = colors.accentInk,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
            eyebrow = eyebrow,
        )
    }
}

/**
 * The line icon on the right of a sign. All are drawn with the same stroke so they read as a set:
 * chevrons to head off (Go, events), an arrow going in to join a run, a plus to make a new one.
 */
enum class SignIcon { Chevrons, Enter, Plus }

/** The drawings are 56 units tall; chevrons are 40 wide, the others square. */
private const val IconBoxHeight = 56f
private const val IconStroke = 6.5f
private val SignIcon.boxWidth get() = if (this == SignIcon.Chevrons) 40f else 56f

/** How much of the span from title to subtitle the icon fills. The chevrons are drawn smaller
 *  (0.8 x 0.8) so they don't outweigh the lettering, as in .ai/mockups/go-sign-icons.html. */
private val SignIcon.scale get() = if (this == SignIcon.Chevrons) 0.64f else 0.8f

/** Draws [icon] filling the canvas, inset by half the stroke so the ink fills it exactly. */
private fun DrawScope.drawSignIcon(icon: SignIcon, color: Color, mirrored: Boolean) {
    val unit = size.height / IconBoxHeight
    val s = IconStroke / 2
    val stroke = Stroke(width = IconStroke * unit, cap = StrokeCap.Round, join = StrokeJoin.Round)
    fun line(vararg xy: Float) {
        val path = Path().apply {
            moveTo(xy[0] * unit, xy[1] * unit)
            for (i in 2 until xy.size step 2) lineTo(xy[i] * unit, xy[i + 1] * unit)
        }
        drawPath(path, color, style = stroke)
    }
    scale(scaleX = if (mirrored) -1f else 1f, scaleY = 1f) {
        when (icon) {
            SignIcon.Chevrons -> {
                line(s, s, s + 15, 28f, s, 56 - s)
                line(s + 18, s, s + 33, 28f, s + 18, 56 - s)
            }
            SignIcon.Enter -> {
                line(31f, s, 56 - s, s, 56 - s, 56 - s, 31f, 56 - s)
                line(s, 28f, 39f, 28f)
                line(27.5f, 16.5f, 39f, 28f, 27.5f, 39.5f)
            }
            SignIcon.Plus -> {
                line(28f, s, 28f, 56 - s)
                line(s, 28f, 56 - s, 28f)
            }
        }
    }
}

@Composable
private fun SignContent(
    title: String,
    subtitle: String,
    titleSize: Int,
    icon: SignIcon,
    ink: Color,
    modifier: Modifier = Modifier,
    alignBottom: Boolean = false,
    eyebrow: String? = null,
    nudging: Boolean = false,
) {
    val base = LocalTextStyle.current
    val eyebrowStyle = base.merge(
        TextStyle(fontFamily = SignFontFamily, fontWeight = FontWeight.ExtraBold, fontSize = 11.sp, letterSpacing = 1.sp),
    )
    val titleStyle = base.merge(
        TextStyle(
            fontFamily = SignFontFamily,
            fontWeight = FontWeight.ExtraBold,
            fontSize = titleSize.sp,
            lineHeight = (titleSize * 1.05f).sp,
        ),
    )
    val subtitleStyle = base.merge(
        TextStyle(fontFamily = SignFontFamily, fontWeight = FontWeight.ExtraBold, fontSize = 15.sp, lineHeight = 18.sp),
    )
    val placement = iconPlacement(icon.scale, eyebrow?.uppercase(), eyebrowStyle, title, titleStyle, subtitle, subtitleStyle)
    val mirrored = LocalLayoutDirection.current == LayoutDirection.Rtl && icon != SignIcon.Plus

    Box(modifier = modifier, contentAlignment = if (alignBottom) Alignment.BottomStart else Alignment.TopStart) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.Top) {
            Column(modifier = Modifier.weight(1f)) {
                eyebrow?.let { Text(text = it.uppercase(), color = ink, style = eyebrowStyle) }
                Text(text = title, color = ink, style = titleStyle)
                Text(
                    text = subtitle,
                    color = ink,
                    style = subtitleStyle,
                    modifier = Modifier.padding(top = SubtitleGap),
                )
            }
            Spacer(Modifier.width(12.dp))
            val iconModifier = Modifier
                .padding(top = placement.top)
                .size(width = placement.height * (icon.boxWidth / IconBoxHeight), height = placement.height)
            Canvas(if (nudging && animationsEnabled()) iconModifier.nudge() else iconModifier) {
                drawSignIcon(icon, ink, mirrored)
            }
        }
    }
}

private val SubtitleGap = 3.dp

private class IconPlacement(val top: Dp, val height: Dp)

/**
 * Where a sign's icon goes: [scale] of the height from the top of the title's capitals to the baseline
 * of the line under it, centred on that span. Measured from the text's own layout, from the top of the text.
 */
@Composable
private fun iconPlacement(
    scale: Float,
    eyebrow: String?,
    eyebrowStyle: TextStyle,
    title: String,
    titleStyle: TextStyle,
    subtitle: String,
    subtitleStyle: TextStyle,
): IconPlacement {
    val measurer = rememberTextMeasurer()
    val density = LocalDensity.current
    val fontResolver = LocalFontFamilyResolver.current
    return remember(scale, eyebrow, title, subtitle, titleStyle, subtitleStyle, eyebrowStyle, density, fontResolver) {
        with(density) {
            val titleTop = eyebrow?.let { measurer.measure(it, eyebrowStyle).size.height.toFloat() } ?: 0f
            val titleLayout = measurer.measure(title, titleStyle)
            val subtitleLayout = measurer.measure(subtitle, subtitleStyle)
            val typeface = fontResolver.resolve(SignFontFamily, FontWeight.ExtraBold).value as? Typeface
            val bounds = Rect()
            Paint().apply {
                this.typeface = typeface ?: Typeface.SERIF
                textSize = titleStyle.fontSize.toPx()
            }.getTextBounds("H", 0, 1, bounds)
            val capHeight = -bounds.top.toFloat()
            val capTop = titleTop + titleLayout.firstBaseline - capHeight
            val subtitleBaseline = titleTop + titleLayout.size.height + SubtitleGap.toPx() + subtitleLayout.firstBaseline
            val span = subtitleBaseline - capTop
            val height = span * scale
            IconPlacement(top = (capTop + (span - height) / 2).toDp(), height = height.toDp())
        }
    }
}

private const val NudgeMillis = 3500
private const val NudgeStepMillis = 210
private const val NudgePush = 7f

/**
 * Two quick 7dp pushes toward the chevrons' direction, then a rest, every 3.5 seconds: a small
 * "this way" on the Go sign. Moves the drawing only, so the sign's layout doesn't change.
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
