package nz.skelstar.dotwatcher.ui.casual

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.ui.semantics.semantics
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
import java.time.Instant
import androidx.compose.material.icons.outlined.Group
import nz.skelstar.dotwatcher.groups.groupEndWords
import nz.skelstar.dotwatcher.network.SessionMembership

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
    /** The groups the person is in (only with the "groups" feature): the yellow circle and a line each. */
    groups: List<SessionMembership> = emptyList(),
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
                    groups = groups,
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
    groups: List<SessionMembership> = emptyList(),
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
                groups = groups,
            )
        }
    }
}

/**
 * The "Who can see me" switches, shared by that sheet (from the map) and "Before you go". A small
 * picture of who sees you sits above them and follows each switch. Each switch saves as soon as it
 * changes.
 *
 * In a group (mock-up "Before you go · in a group" on the "Group button on the start screen" canvas) the
 * picture puts this person inside a yellow circle with the group, a key explains it, and each group gets a
 * line under "My crew": the group sees them by name whatever the switches say, until it ends or they leave.
 * "Hide my start area" isn't shown then (groups start at a meeting point); the saved setting still applies.
 * Matches iOS's `WhoCanSeeMeControls`.
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
    groups: List<SessionMembership> = emptyList(),
) {
    val inGroup = groups.isNotEmpty()
    val now = remember { Instant.now() }
    // Only while nearby is on does "Hide my start area" matter, so only then can the start be exposed.
    val startExposed = visibleToNonCrew && !hideStartArea
    Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
        WhoSeesYouStrip(
            me = me,
            crew = crew,
            crewVisible = visibleToCrew,
            nearbyVisible = visibleToNonCrew,
            startHidden = !inGroup && visibleToNonCrew && hideStartArea,
            inGroup = inGroup,
        )
        if (inGroup) {
            val first = crew.firstOrNull()
            WhoSeesYouKey(
                groupName = if (groups.size == 1) groups[0].sessionName else "your groups",
                // The first crew dot's colour, as drawn in the picture.
                crewColor = first?.let {
                    Color(RunnerColorPalette.colorAt(RunnerColorPalette.indexForName(it.nickname ?: it.displayName)))
                } ?: PLACEHOLDER_CREW[0],
            )
        }
        SwitchRow(
            title = "My crew",
            checked = visibleToCrew,
            enabled = true,
            onCheckedChange = onCrewVisibleChange,
            body = { if (inGroup) SwitchCaption("By name, anywhere") },
        )
        groups.forEach { GroupSeesYouLine(it, now) }
        SwitchRow(
            title = "Show me to people nearby",
            checked = visibleToNonCrew,
            enabled = true,
            onCheckedChange = onVisibleChange,
            body = { if (inGroup) SwitchCaption("Anyone else within 5 km, as a dot with no name") },
        )
        if (!inGroup) SwitchRow(
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
        // In a group they're never private: the group always sees them.
        if (!visibleToCrew && !visibleToNonCrew && !inGroup) {
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

/** The join-area yellow (as on the organiser's map), for the group circle and its key. */
private val GroupFill = Color(0xFFFACC15).copy(alpha = 0.35f)
private val GroupStroke = Color(0xFFCA8A04)

@Composable
private fun SwitchCaption(text: String) {
    Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

/** Under the picture while in a group: what the circle, the crew colour and the grey dots mean. */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun WhoSeesYouKey(groupName: String, crewColor: Color) {
    @Composable
    fun item(fill: Color, stroke: Color?, text: String) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(
                Modifier
                    .size(11.dp)
                    .background(fill, CircleShape)
                    .then(if (stroke != null) Modifier.border(1.5.dp, stroke, CircleShape) else Modifier),
            )
            Text(text, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
    FlowRow(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
        item(GroupFill, GroupStroke, "In $groupName, by name")
        item(crewColor, null, "Crew, by name")
        item(NEARBY_GREY, null, "Others, no name")
    }
}

/** One group under "My crew": everyone in it sees this person by name until it ends. Not a switch: joining was
 *  the consent, and leaving (from the group sign) is the way out. */
@Composable
private fun GroupSeesYouLine(group: SessionMembership, now: Instant) {
    val until = groupEndWords(group.endsAt, now)?.let { "until it ends, $it" } ?: "until it ends"
    Row(
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier
            .fillMaxWidth()
            .background(GroupFill.copy(alpha = 0.21f), RoundedCornerShape(12.dp))
            .padding(12.dp)
            .semantics(mergeDescendants = true) {},
    ) {
        Icon(Icons.Outlined.Group, contentDescription = null, modifier = Modifier.size(22.dp))
        Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(group.sessionName, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            Text(
                "Everyone in it sees you by name $until. Leave it from the group sign.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

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
// In a group: the circle around the start, two members in it, and the nearby dots clear of it.
private val STRIP_GROUP_CENTER = Offset(58f, 50f)
private const val STRIP_GROUP_RADIUS = 38f
private val STRIP_GROUP_MEMBERS = listOf(Offset(66f, 30f), Offset(80f, 56f))
private val STRIP_GROUP_MEMBER_COLORS = listOf(Color(0xFF37474F), Color(0xFF00796B))
private val STRIP_GROUP_NEARBY = listOf(Offset(200f, 18f), Offset(212f, 64f))

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
    inGroup: Boolean = false,
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
            if (inGroup) {
                val center = Offset(STRIP_GROUP_CENTER.x * px, STRIP_GROUP_CENTER.y * px)
                drawCircle(GroupFill, radius = STRIP_GROUP_RADIUS * px, center = center)
                drawCircle(GroupStroke, radius = STRIP_GROUP_RADIUS * px, center = center, style = Stroke(1.5.dp.toPx()))
            }
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
        if (inGroup) {
            STRIP_GROUP_MEMBERS.forEachIndexed { index, spot ->
                StripDot(spot, scale, STRIP_GROUP_MEMBER_COLORS[index], null, shown = 1f)
            }
        }
        (if (inGroup) STRIP_GROUP_NEARBY else STRIP_NEARBY).forEach { spot ->
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
