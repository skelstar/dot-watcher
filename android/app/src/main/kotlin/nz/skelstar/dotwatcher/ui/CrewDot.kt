package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.ui.map.RunnerColorPalette

/** A person's colour, the same every time and on every screen: hashed from their user id into the
 *  runner palette (which keeps blue for the local user). */
fun crewColor(userId: String): Color =
    Color(RunnerColorPalette.colorAt(RunnerColorPalette.indexForName(userId)))

/**
 * A person as a coloured circle with their two initials. The dark ring around it is the "crew mark":
 * a circle with a ring is someone in your crew, wherever it appears (the start-screen row, the crew
 * sheet and, later, the map). The ring takes [RING_WIDTH] + [GAP] on each side outside [size]. The
 * ring is never the only signal: callers give the dot a content description naming the person.
 */
@Composable
fun CrewDot(
    initials: String,
    color: Color,
    modifier: Modifier = Modifier,
    size: Dp = 34.dp,
    isCrew: Boolean = true,
) {
    val face = @Composable {
        Box(
            modifier = Modifier
                .size(size)
                .clip(CircleShape)
                .background(color),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = initials.take(2).uppercase().padEnd(2, '?'),
                color = Color.White,
                fontSize = (size.value * 0.34f).sp,
                fontWeight = FontWeight.Bold,
            )
        }
    }
    if (isCrew) {
        Box(
            modifier = modifier
                .border(RING_WIDTH, MaterialTheme.colorScheme.onSurface, CircleShape)
                .padding(GAP),
        ) { face() }
    } else {
        Box(modifier = modifier) { face() }
    }
}

val RING_WIDTH = 2.dp
val GAP = 2.dp
