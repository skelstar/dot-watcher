package nz.skelstar.dotwatcher.ui.crew

import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.crew.CrewUiState
import nz.skelstar.dotwatcher.crew.crewRowSummary
import nz.skelstar.dotwatcher.crew.label
import nz.skelstar.dotwatcher.ui.CrewDot
import nz.skelstar.dotwatcher.ui.GroupedCard
import nz.skelstar.dotwatcher.ui.colorFor
import nz.skelstar.dotwatcher.ui.groupedCardColor

/**
 * The slim "Your crew" row at the top of the start screen: up to four faces, "+N" for the rest, and a count. Tapping opens the crew sheet. Draws nothing until
 * the server has said it has crew, so an app running against an older server shows no trace of it.
 */
@Composable
fun CrewRow(state: CrewUiState, onClick: () -> Unit, modifier: Modifier = Modifier) {
    if (!state.isAvailable) return
    val summary = crewRowSummary(state.members)

    GroupedCard(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClickLabel = "Open your crew", role = Role.Button, onClick = onClick),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (summary.faces.isEmpty()) {
                Box(
                    modifier = Modifier
                        .size(34.dp)
                        .border(2.dp, MaterialTheme.colorScheme.outline, CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(
                        imageVector = Icons.Default.Add,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.outline,
                    )
                }
            } else {
                Row(horizontalArrangement = Arrangement.spacedBy((-10).dp)) {
                    summary.faces.forEach { member ->
                        // No ring here: the row is already "Your crew", and overlapping rings looked
                        // messy. A card-coloured edge keeps overlapped dots apart, and 38 is the outer
                        // size the ringed 30 dot used to have.
                        CrewDot(
                            initials = member.displayName,
                            color = state.colorFor(member.userId),
                            size = 38.dp,
                            isCrew = false,
                            modifier = Modifier
                                .border(2.dp, groupedCardColor(), CircleShape)
                                .semantics { contentDescription = "${member.label()}, in your crew" },
                        )
                    }
                }
                if (summary.extra > 0) {
                    Text(
                        text = "+${summary.extra}",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(start = 8.dp),
                    )
                }
            }
            Spacer(modifier = Modifier.width(14.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(text = summary.title, style = MaterialTheme.typography.titleMedium)
                Text(
                    text = summary.meta,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Icon(
                imageVector = Icons.AutoMirrored.Filled.KeyboardArrowRight,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
