package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.disabled
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** The four ways to start something, in the order the design lists them. Only [Group] is how
 *  sessions work today; the others are shown so the direction is visible, and are not tappable. */
enum class StartKind(
    val title: String,
    val description: String,
    val available: Boolean,
) {
    Private(
        "Private",
        "Invite only. Crew/watchers that have not been invited see nothing at all.",
        available = false,
    ),
    CrewAndInvites(
        "Crew + invites",
        "Everyone joined will see you, your 'crew' watching see you, everyone else sees a grey dot.",
        available = false,
    ),
    Group(
        "Group",
        "Everyone joined will see you. Anyone with the invite code can see you. Good for clubs.",
        available = true,
    ),
    Race(
        "Race",
        "Organiser sees everyone, 'racers' see their crew, otherwise “grey dots” (depending on settings).",
        available = false,
    ),
}

/** The "Start a new one" list from the main-screen design: a card per [StartKind], each with a
 *  small picture of who can see whom, and a footer line. [onGroup] is the only live action. */
@Composable
fun StartOptions(onGroup: () -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        StartKind.entries.forEach { kind ->
            StartOptionCard(kind, onClick = if (kind == StartKind.Group) onGroup else null)
        }
        Text(
            text = "You can make this stricter later, never looser.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.fillMaxWidth().padding(top = 2.dp, bottom = 8.dp),
            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
        )
    }
}

// iOS's secondarySystemBackground, a step lighter than the grouped cards above it (the design's
// option cards sit on a paler fill than the Join card).
private val OptionCardLight = Color(0xFFF2F2F7)
private val OptionCardDark = Color(0xFF1C1C1E)

@Composable
private fun StartOptionCard(kind: StartKind, onClick: (() -> Unit)?) {
    val enabled = kind.available && onClick != null
    val fill = if (isSystemInDarkTheme()) OptionCardDark else OptionCardLight
    val shape = RoundedCornerShape(14.dp)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .alpha(if (enabled) 1f else 0.5f)
            .clip(shape)
            .background(fill, shape)
            .then(if (enabled) Modifier.clickable(role = Role.Button, onClick = onClick!!) else Modifier)
            // One item for a screen reader: title, "COMING SOON" (when it is) and the description.
            .semantics(mergeDescendants = true) { if (!enabled) disabled() }
            .padding(horizontal = 12.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(11.dp),
    ) {
        StartIllustration(kind)
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(kind.title, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                if (!kind.available) ComingSoonTag()
            }
            Text(
                text = kind.description,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        if (enabled) {
            Text("›", fontSize = 22.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun ComingSoonTag() {
    Text(
        text = "COMING SOON",
        fontSize = 9.5.sp,
        fontWeight = FontWeight.Bold,
        letterSpacing = 0.4.sp,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier
            .background(Color(0x1F8E8E93), RoundedCornerShape(6.dp))
            .padding(horizontal = 6.dp, vertical = 3.dp),
    )
}

// --- Illustrations ----------------------------------------------------------------------
// Drawn on the design's 60 x 40 grid and scaled to the box, so the coordinates below are the
// design's own. A trail across the box, and dots on it: blue is you, grey is someone you can't
// name, and colours are people named to you.

private val IllBlue = Color(0xFF007AFF)
private val IllOrange = Color(0xFFE8590C)
private val IllGreen = Color(0xFF2F9E44)
private val IllPurple = Color(0xFF8E44AD)
private val IllGrey = Color(0xFF9AA5A1)
private val IllTrail = Color(0xFFB9C9B4)
private val IllGround = Color(0xFFE6EEE3)

@Composable
private fun StartIllustration(kind: StartKind) {
    Canvas(
        modifier = Modifier
            .size(width = 60.dp, height = 42.dp)
            .clip(RoundedCornerShape(9.dp)),
    ) {
        scale(scaleX = size.width / 60f, scaleY = size.height / 40f, pivot = Offset.Zero) {
            when (kind) {
                StartKind.Private -> {
                    ground(IllGround)
                    trail()
                    dot(16f, 29f, 5f, IllBlue)
                }
                StartKind.CrewAndInvites -> {
                    ground(IllGround)
                    trail()
                    dot(16f, 29f, 5f, IllBlue)
                    dot(30f, 21f, 3.5f, IllGrey)
                    dot(43f, 13f, 3.5f, IllGrey)
                    dot(52f, 7f, 3.5f, IllGrey)
                }
                StartKind.Group -> {
                    ground(IllGround)
                    trail()
                    dot(16f, 29f, 5f, IllBlue)
                    dot(30f, 21f, 5f, IllOrange)
                    dot(43f, 13f, 5f, IllGreen)
                    dot(52f, 7f, 5f, IllPurple)
                }
                StartKind.Race -> race()
            }
        }
    }
}

private fun DrawScope.ground(color: Color) = drawRect(color)

private fun trailPath() = Path().apply {
    moveTo(5f, 34f)
    cubicTo(20f, 30f, 30f, 20f, 40f, 14f)
    cubicTo(50f, 8f, 52f, 6f, 57f, 4f)
}

private fun DrawScope.trail() =
    drawPath(trailPath(), IllTrail, style = Stroke(width = 5f, cap = StrokeCap.Round))

private fun DrawScope.dot(x: Float, y: Float, radius: Float, color: Color) =
    drawCircle(color, radius, Offset(x, y))

/** Left half is the organiser's view (everyone named), right half a racer's (grey except someone
 *  they know), split by a white line. */
private fun DrawScope.race() {
    drawRect(Color(0xFFD3E3CE), topLeft = Offset(0f, 0f), size = androidx.compose.ui.geometry.Size(30f, 40f))
    drawRect(Color(0xFFEEF3EC), topLeft = Offset(30f, 0f), size = androidx.compose.ui.geometry.Size(30f, 40f))
    drawPath(
        trailPath(),
        Color(0xFF7F9B79),
        style = Stroke(width = 2f, cap = StrokeCap.Round, pathEffect = PathEffect.dashPathEffect(floatArrayOf(0.1f, 3.6f))),
    )
    dot(13f, 31f, 4f, IllBlue)
    dot(23f, 26f, 4f, IllOrange)
    dot(38f, 16f, 3f, IllGrey)
    dot(46f, 10f, 4f, IllBlue)
    dot(54f, 6f, 3f, IllGrey)
    drawLine(Color.White, Offset(30f, 2f), Offset(30f, 38f), strokeWidth = 1.5f, cap = StrokeCap.Round)
}
