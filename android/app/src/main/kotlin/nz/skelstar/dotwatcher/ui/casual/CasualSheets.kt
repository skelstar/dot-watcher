package nz.skelstar.dotwatcher.ui.casual

import androidx.compose.foundation.background
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Error
import androidx.compose.material3.Icon
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.unit.Dp
import nz.skelstar.dotwatcher.network.CrewMember
import nz.skelstar.dotwatcher.ui.map.RunnerColorPalette
import nz.skelstar.dotwatcher.ui.map.initialsFor
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.location.TRACKING_DURATION_OPTIONS
import java.time.Duration

/** How long a casual run shares for unless the person picks otherwise. Shorter than the 24 hours
 *  a session defaults to: a run is rarely longer than a few hours, and 24 is still one tap away. */
val DEFAULT_CASUAL_DURATION: Duration = Duration.ofHours(4)

/**
 * Shown every time Go is held, before anything starts. Holds the "Who can see me" switches, so they
 * can be changed without leaving this sheet, and asks how long to share for (sharing always stops on
 * its own, the same choices as the session consent screen).
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BeforeYouGoSheet(
    /** This person's runner name, for the initials on their own dot. */
    me: String,
    crew: List<CrewMember>,
    visibleToCrew: Boolean,
    visibleToNonCrew: Boolean,
    hideStartArea: Boolean,
    error: String?,
    onCrewVisibleChange: (Boolean) -> Unit,
    onVisibleChange: (Boolean) -> Unit,
    onHideStartAreaChange: (Boolean) -> Unit,
    onGo: (Duration) -> Unit,
    onDismiss: () -> Unit,
) {
    var duration by remember { mutableStateOf(DEFAULT_CASUAL_DURATION) }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Before you go", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                SectionHeader("WHO CAN SEE ME")
                WhoCanSeeMeControls(
                    me = me,
                    crew = crew,
                    visibleToCrew = visibleToCrew,
                    visibleToNonCrew = visibleToNonCrew,
                    hideStartArea = hideStartArea,
                    error = error,
                    onCrewVisibleChange = onCrewVisibleChange,
                    onVisibleChange = onVisibleChange,
                    onHideStartAreaChange = onHideStartAreaChange,
                )
            }

            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                SectionHeader("SHARE FOR")
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TRACKING_DURATION_OPTIONS.forEach { option ->
                        FilterChip(
                            selected = option == duration,
                            onClick = { duration = option },
                            label = { Text("${option.toHours()}h") },
                        )
                    }
                }
                Text(
                    text = "Sharing always stops on its own. This picks how soon. Hold the red Stop to end it sooner.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            Button(
                onClick = { onGo(duration) },
                colors = ButtonDefaults.buttonColors(containerColor = GoGreen),
                modifier = Modifier.fillMaxWidth().height(48.dp),
            ) {
                Text("Go", fontWeight = FontWeight.Bold)
            }
        }
    }
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.labelSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
}

/** "who aren't in your crew" is underlined: it is the group the setting is about. */
private val HIDE_START_AREA_BODY = buildAnnotatedString {
    append("People ")
    withStyle(SpanStyle(textDecoration = TextDecoration.Underline)) { append("who aren't in your crew") }
    append(" don't see you until you are 300 m from where you started.")
}

/**
 * Who can see a casual run. Crew can, unless the person turns "My crew" off, which also stops them
 * seeing their crew. The nearby switch
 * controls strangers: off until the person turns it on, then remembered. Only people who have also
 * turned it on can be seen, and they see each other, so turning it off also stops showing them
 * others. With both off the person is private: nobody sees them on the map, but a watch link still
 * works.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WhoCanSeeMeSheet(
    me: String,
    crew: List<CrewMember>,
    visibleToCrew: Boolean,
    visibleToNonCrew: Boolean,
    hideStartArea: Boolean,
    error: String?,
    onCrewVisibleChange: (Boolean) -> Unit,
    onVisibleChange: (Boolean) -> Unit,
    onHideStartAreaChange: (Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Who can see me", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            WhoCanSeeMeControls(
                me = me,
                crew = crew,
                visibleToCrew = visibleToCrew,
                visibleToNonCrew = visibleToNonCrew,
                hideStartArea = hideStartArea,
                error = error,
                onCrewVisibleChange = onCrewVisibleChange,
                onVisibleChange = onVisibleChange,
                onHideStartAreaChange = onHideStartAreaChange,
            )
        }
    }
}

/**
 * The "Who can see me" switches, shared by that sheet (from the map) and "Before you go". A small
 * picture of who sees you sits above them and follows each switch. Each switch saves as soon as it
 * changes.
 */
@Composable
private fun WhoCanSeeMeControls(
    me: String,
    crew: List<CrewMember>,
    visibleToCrew: Boolean,
    visibleToNonCrew: Boolean,
    hideStartArea: Boolean,
    error: String?,
    onCrewVisibleChange: (Boolean) -> Unit,
    onVisibleChange: (Boolean) -> Unit,
    onHideStartAreaChange: (Boolean) -> Unit,
) {
    // Only while nearby is on does "Hide my start area" matter, so only then can the start be exposed.
    val startExposed = visibleToNonCrew && !hideStartArea
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        WhoSeesYouStrip(
            me = me,
            crew = crew,
            crewVisible = visibleToCrew,
            nearbyVisible = visibleToNonCrew,
            startHidden = visibleToNonCrew && hideStartArea,
        )
        SwitchRow(
            title = "My crew",
            checked = visibleToCrew,
            enabled = true,
            onCheckedChange = onCrewVisibleChange,
        )
        SwitchRow(
            title = "Show me to people nearby",
            checked = visibleToNonCrew,
            enabled = true,
            onCheckedChange = onVisibleChange,
        )
        SwitchRow(
            title = "Hide my start area",
            checked = hideStartArea,
            enabled = visibleToNonCrew,
            onCheckedChange = onHideStartAreaChange,
        ) {
            if (startExposed) {
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    Icon(
                        imageVector = Icons.Filled.Error,
                        contentDescription = null,
                        tint = WarningOrange,
                        modifier = Modifier.padding(top = 1.dp).size(15.dp),
                    )
                    Text(
                        text = "Anyone nearby can see where you set off from, which may be your home.",
                        style = MaterialTheme.typography.bodySmall,
                        color = WarningOrange,
                    )
                }
            } else {
                Text(
                    HIDE_START_AREA_BODY,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        if (!visibleToCrew && !visibleToNonCrew) {
            Text(
                text = "You're private: nobody sees you on the map, and you don't see your crew. You can " +
                    "still send someone a link to watch you. On the map, tap Share.",
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier
                    .fillMaxWidth()
                    .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(10.dp))
                    .padding(12.dp),
            )
        }
        if (error != null) {
            Text(error, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
        }
    }
}

private val WarningOrange = Color(0xFFE07800)

@Composable
private fun SwitchRow(
    title: String,
    checked: Boolean,
    enabled: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    body: (@Composable () -> Unit)? = null,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            body?.invoke()
        }
        Spacer(Modifier.width(12.dp))
        Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
    }
}

// Who-sees-you strip: drawn on a 300 x 84 grid, scaled to the width it is given.
private const val STRIP_W = 300f
private const val STRIP_H = 84f
private val STRIP_START = Offset(36f, 66f)
private val STRIP_CREW = listOf(Offset(132f, 46f), Offset(164f, 40f), Offset(262f, 20f))
private val STRIP_NEARBY = listOf(Offset(94f, 24f), Offset(212f, 64f))
/** Stand-ins when the person has no crew yet, drawn without initials. */
private val PLACEHOLDER_CREW = listOf(Color(0xFFE8642C), Color(0xFF2FA84F), Color(0xFF8E44AD))
private val NEARBY_GREY = Color(0xFF9AA3AB)

/**
 * A small map strip above the "Who can see me" switches: this person at their start, up to three
 * of their crew along a route, and two anonymous nearby dots. Turning a switch off fades its dots
 * away, which shows the two-way part (with "My crew" off you don't see them either). With the start
 * hidden, a dashed ring circles the start and this person's dot is half see-through. Decorative: the
 * switches carry the meaning, so it is hidden from TalkBack.
 */
@Composable
private fun WhoSeesYouStrip(
    me: String,
    crew: List<CrewMember>,
    crewVisible: Boolean,
    nearbyVisible: Boolean,
    startHidden: Boolean,
) {
    val crewShown by animateFloatAsState(if (crewVisible) 1f else 0f, tween(250), label = "crew")
    val nearbyShown by animateFloatAsState(if (nearbyVisible) 1f else 0f, tween(250), label = "nearby")
    val ringShown by animateFloatAsState(if (startHidden) 1f else 0f, tween(250), label = "ring")
    val meAlpha by animateFloatAsState(if (startHidden) 0.5f else 1f, tween(250), label = "me")
    val background = Color(0xFF34C759).copy(alpha = 0.14f)
    val route = MaterialTheme.colorScheme.outlineVariant
    val ring = MaterialTheme.colorScheme.onSurfaceVariant

    BoxWithConstraints(
        modifier = Modifier
            .fillMaxWidth()
            .aspectRatio(STRIP_W / STRIP_H)
            .clip(RoundedCornerShape(12.dp))
            .background(background)
            .clearAndSetSemantics {},
    ) {
        val scale = maxWidth / STRIP_W
        Canvas(Modifier.matchParentSize()) {
            val px = size.width / STRIP_W
            val path = Path().apply {
                moveTo(16f * px, 70f * px)
                cubicTo(70f * px, 64f * px, 100f * px, 48f * px, 150f * px, 44f * px)
                cubicTo(200f * px, 40f * px, 240f * px, 24f * px, 286f * px, 14f * px)
            }
            drawPath(path, route, style = Stroke(width = 5f * px, cap = StrokeCap.Round))
            if (ringShown > 0f) {
                drawCircle(
                    color = ring.copy(alpha = ringShown),
                    radius = 24f * px * (0.4f + 0.6f * ringShown),
                    center = Offset(STRIP_START.x * px, STRIP_START.y * px),
                    style = Stroke(
                        width = 1.5.dp.toPx(),
                        pathEffect = PathEffect.dashPathEffect(floatArrayOf(3.dp.toPx(), 3.dp.toPx())),
                    ),
                )
            }
        }
        STRIP_NEARBY.forEach { spot ->
            StripDot(spot, scale, NEARBY_GREY, "?", nearbyShown)
        }
        STRIP_CREW.forEachIndexed { index, spot ->
            val member = crew.getOrNull(index)
            when {
                member != null -> StripDot(
                    spot,
                    scale,
                    Color(RunnerColorPalette.colorAt(RunnerColorPalette.indexForName(member.nickname ?: member.displayName))),
                    initialsFor(member.displayName),
                    crewShown,
                )
                crew.isEmpty() -> StripDot(spot, scale, PLACEHOLDER_CREW[index], null, crewShown)
            }
        }
        StripDot(
            STRIP_START,
            scale,
            Color(RunnerColorPalette.colorAt(RunnerColorPalette.CURRENT_USER_INDEX)),
            initialsFor(me),
            shown = 1f,
            alpha = meAlpha,
        )
    }
}

/** One dot on the strip, centred on [spot] (grid units). [shown] fades and shrinks it away. */
@Composable
private fun StripDot(spot: Offset, scale: Dp, color: Color, label: String?, shown: Float, alpha: Float = 1f) {
    if (shown <= 0f) return
    val size = scale * 22f
    Box(
        contentAlignment = Alignment.Center,
        modifier = Modifier
            .offset(x = scale * spot.x - size / 2, y = scale * spot.y - size / 2)
            .size(size)
            .graphicsLayer {
                this.alpha = shown * alpha
                scaleX = 0.4f + 0.6f * shown
                scaleY = 0.4f + 0.6f * shown
            }
            .background(Color.White, CircleShape)
            .padding(2.dp)
            .background(color, CircleShape),
    ) {
        if (label != null) {
            val fontSize = with(LocalDensity.current) { (scale * if (label == "?") 11f else 9f).toSp() }
            Text(label, color = Color.White, fontSize = fontSize, fontWeight = FontWeight.Bold, maxLines = 1)
        }
    }
}
