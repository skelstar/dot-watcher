package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.ui.map.RunnerColorPalette

/** How the local user's own dot looks on the map: blue, white border, two letters. Anything shorter
 *  than two letters is padded with "?" — "??" before the runner has typed, "D?" after one. */
@Composable
fun InitialsDot(initials: String) {
    val padded = initials.take(2).uppercase().padEnd(2, '?')
    Box(
        modifier = Modifier
            .size(44.dp)
            .clip(CircleShape)
            .background(Color(RunnerColorPalette.colorAt(RunnerColorPalette.CURRENT_USER_INDEX)))
            .border(2.5.dp, Color.White, CircleShape),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = padded,
            color = Color.White,
            fontSize = 15.sp,
            fontWeight = FontWeight.Bold,
        )
    }
}
