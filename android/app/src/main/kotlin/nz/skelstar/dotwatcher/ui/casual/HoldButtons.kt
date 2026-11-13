package nz.skelstar.dotwatcher.ui.casual

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.PressGestureScope
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.hapticfeedback.HapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.onLongClick
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/** How long Go must be held. Long enough to ignore a press in a pocket, short enough that
 *  nobody has to try twice. */
const val HOLD_MILLIS = 900

val GoGreen = Color(0xFF34C759)
val StopRed = Color(0xFFFF3B30)
private val StopRedFill = Color(0xFFB3261E)

/**
 * The press-and-hold for Go: [progress] fills over [HOLD_MILLIS] while the finger is
 * down, then [onComplete] runs with a firm tap. Letting go early drains it and does nothing.
 */
internal suspend fun PressGestureScope.holdToComplete(
    progress: Animatable<Float, *>,
    scope: CoroutineScope,
    haptic: HapticFeedback,
    onComplete: () -> Unit,
) {
    haptic.performHapticFeedback(HapticFeedbackType.TextHandleMove)
    val job = scope.launch {
        progress.animateTo(1f, tween(HOLD_MILLIS, easing = LinearEasing))
        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
        onComplete()
        progress.snapTo(0f)
    }
    tryAwaitRelease()
    if (job.isActive) {
        job.cancel()
        progress.animateTo(0f, tween(150))
    }
}

/**
 * The big Go button on the start screen. A ring fills around it while it is held; when full,
 * [onComplete] runs. Holding rather than tapping means it can't be started from a pocket or bag.
 * TalkBack users get a long-press action instead of a drag-and-hold.
 */
@Composable
fun HoldToGoButton(onComplete: () -> Unit, modifier: Modifier = Modifier) {
    val progress = remember { Animatable(0f) }
    val scope = rememberCoroutineScope()
    val haptic = LocalHapticFeedback.current
    val track = MaterialTheme.colorScheme.surfaceVariant

    Box(
        modifier = modifier
            .size(224.dp)
            .semantics {
                contentDescription = "Go. Hold to start tracking."
                onLongClick(label = "Start tracking") { onComplete(); true }
            }
            .pointerInput(Unit) {
                detectTapGestures(onPress = { holdToComplete(progress, scope, haptic, onComplete) })
            },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.size(224.dp)) {
            val stroke = Stroke(width = 10.dp.toPx(), cap = StrokeCap.Round)
            val inset = 5.dp.toPx()
            val arcSize = Size(size.width - 2 * inset, size.height - 2 * inset)
            drawArc(track, 0f, 360f, false, topLeft = androidx.compose.ui.geometry.Offset(inset, inset), size = arcSize, style = stroke)
            drawArc(
                GoGreen, -90f, 360f * progress.value, false,
                topLeft = androidx.compose.ui.geometry.Offset(inset, inset), size = arcSize, style = stroke,
            )
        }
        Box(
            modifier = Modifier.size(190.dp).clip(CircleShape).background(GoGreen),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = "Go",
                color = Color.White,
                fontSize = 60.sp,
                fontWeight = FontWeight.ExtraBold,
                letterSpacing = (-0.5).sp,
            )
        }
    }
}

/**
 * The red Stop pill on the map. A tap asks "Stop tracking?" and only the confirm ends the run, so a
 * bump or a stray tap can't. It used to be held like Go; it changed with iOS, where the touch reached
 * the held pill late over the map, so both apps behave the same.
 */
@Composable
fun StopPill(onStop: () -> Unit, modifier: Modifier = Modifier) {
    var confirming by remember { mutableStateOf(false) }
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()

    Box(
        modifier = modifier
            .height(48.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(if (pressed) StopRedFill else StopRed)
            .clickable(
                interactionSource = interaction,
                indication = null,
                onClickLabel = "Asks before stopping tracking",
                role = Role.Button,
            ) { confirming = true }
            .padding(horizontal = 22.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text("Stop", color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.Bold)
    }

    if (confirming) {
        AlertDialog(
            onDismissRequest = { confirming = false },
            title = { Text("Stop tracking?") },
            confirmButton = {
                TextButton(
                    onClick = { confirming = false; onStop() },
                    colors = ButtonDefaults.textButtonColors(contentColor = StopRed),
                ) { Text("Stop") }
            },
            dismissButton = { TextButton(onClick = { confirming = false }) { Text("Keep going") } },
        )
    }
}

/** Small helper for a stacked caption under the Go button. */
@Composable
fun GoCaption(title: String, body: String, modifier: Modifier = Modifier) {
    Column(modifier = modifier, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(
            text = title,
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
        Text(
            text = body,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 4.dp),
        )
    }
}
