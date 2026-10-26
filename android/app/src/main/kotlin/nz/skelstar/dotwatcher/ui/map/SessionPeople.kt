package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.network.RunnerPosition
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime

/** Up to this many people are shown as separate dots you can tap; past it the dots overlap, as in the
 *  crew row, and the chip opens a list. Six faces fit the bottom-left corner next to the stop pill. */
private const val SEPARATE_DOT_LIMIT = 4
private const val OVERLAPPED_FACES = 6

/** You first, then everyone else who has a position in the session. */
internal fun sessionNames(me: String, positions: List<RunnerPosition>): List<String> =
    listOf(me) + positions.map { it.runnerName }.filter { it != me }.distinct()

/** Seconds since [name]'s last position, or null if they have none (or it can't be read). Your own is
 *  never old: it is this device's own data. */
private fun positionAge(name: String, me: String, positions: List<RunnerPosition>, now: Instant): Duration? {
    if (name == me) return Duration.ZERO
    val position = positions.firstOrNull { it.runnerName == name } ?: return null
    return runCatching { Duration.between(OffsetDateTime.parse(position.timestamp).toInstant(), now) }.getOrNull()
}

private val NoRecentPosition: Duration = Duration.ofSeconds(60)

/** A runner as a dot in their pin colour (blue for you). A dashed edge means no recent position. */
@Composable
internal fun PersonDot(
    name: String,
    me: String,
    size: Dp,
    noRecentPosition: Boolean,
    modifier: Modifier = Modifier,
    followed: Boolean = false,
    edge: Boolean = false,
) {
    val color = Color(
        RunnerColorPalette.colorAt(
            if (name == me) RunnerColorPalette.CURRENT_USER_INDEX else RunnerColorPalette.indexForName(name)
        )
    )
    val shape = CircleShape
    var dot = modifier.size(size).clip(shape)
    dot = if (noRecentPosition) {
        // See-through centre, dashed edge: drawn as a pale fill with a coloured ring, since a true dashed
        // border needs a canvas and this is only a small dot.
        dot.background(Color.White).border(BorderStroke(2.dp, color), shape)
    } else {
        dot.background(color).let { if (edge) it.border(BorderStroke(2.dp, Color.White), shape) else it }
    }
    if (followed) dot = dot.border(BorderStroke(2.5.dp, Color.Black), shape)
    Box(modifier = dot, contentAlignment = Alignment.Center) {
        Text(
            text = initialsFor(name),
            color = if (noRecentPosition) color else Color.White,
            fontSize = (size.value * 0.32f).sp,
            fontWeight = FontWeight.Bold,
        )
    }
}

/**
 * Everyone in the session as dots, bottom left. Up to four are separate: tap one to centre the map on
 * them, tap again to let go. With more than four the dots overlap and the whole chip opens the list.
 */
@Composable
internal fun SessionPeopleChip(
    names: List<String>,
    me: String,
    positions: List<RunnerPosition>,
    followed: String?,
    onFollow: (String) -> Unit,
    onShowAll: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val now by rememberSecondClock()
    val overlapped = names.size > SEPARATE_DOT_LIMIT
    val shown = names.take(if (overlapped) OVERLAPPED_FACES else SEPARATE_DOT_LIMIT)
    val extra = names.size - shown.size

    val chip = modifier
        .height(46.dp)
        .clip(CircleShape)
        .background(GlassColor)
        .let { if (overlapped) it.clickable(role = Role.Button, onClick = onShowAll) else it }
        .semantics {
            if (overlapped) contentDescription = "${names.size} people in this session. Opens the list."
        }
        .padding(horizontal = 10.dp)

    Row(
        modifier = chip,
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(if (overlapped) (-10).dp else 6.dp),
    ) {
        shown.forEach { name ->
            val age = positionAge(name, me, positions, now)
            val noPosition = age == null || age > NoRecentPosition
            PersonDot(
                name = name,
                me = me,
                size = 34.dp,
                noRecentPosition = noPosition,
                followed = !overlapped && followed == name,
                edge = overlapped,
                modifier = if (overlapped) {
                    Modifier
                } else {
                    Modifier
                        .clickable(enabled = age != null) { onFollow(name) }
                        .semantics { contentDescription = if (name == me) "You" else name }
                },
            )
        }
        if (extra > 0) {
            Text(
                text = "+$extra",
                fontWeight = FontWeight.Bold,
                fontSize = 12.5.sp,
                color = Color.Black.copy(alpha = 0.6f),
                modifier = Modifier.padding(start = if (overlapped) 14.dp else 6.dp),
            )
        }
    }
}

/** Everyone in the session, for when there are too many dots to tap one at a time. Tapping someone with a
 *  position closes the list and centres the map on them. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun SessionPeopleSheet(
    names: List<String>,
    me: String,
    positions: List<RunnerPosition>,
    followed: String?,
    onSelect: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    val now by rememberSecondClock()
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Text(
            text = "In this session · ${names.size}",
            style = MaterialTheme.typography.titleLarge,
            modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp),
        )
        LazyColumn(modifier = Modifier.navigationBarsPadding()) {
            items(names, key = { it }) { name ->
                val age = positionAge(name, me, positions, now)
                val noPosition = age == null || age > NoRecentPosition
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable(enabled = age != null) { onSelect(name) }
                        .padding(horizontal = 24.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    PersonDot(name = name, me = me, size = 40.dp, noRecentPosition = noPosition)
                    Spacer(Modifier.width(14.dp))
                    Column(modifier = Modifier.weight(1f)) {
                        Text(if (name == me) "You" else name, style = MaterialTheme.typography.titleMedium)
                        Text(
                            text = statusText(age),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    if (followed == name) Text("Following", style = MaterialTheme.typography.labelMedium)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
    }
}

private fun statusText(age: Duration?): String = when {
    age == null -> "No position yet"
    age < Duration.ofSeconds(60) -> "Updated just now"
    else -> "Updated ${age.toMinutes()} min ago"
}
