package nz.skelstar.dotwatcher.ui.groups

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.network.SessionMembership

// Groups (see .ai/plans/go-and-groups.md, mock-up .ai/mockups/go-and-groups-before-go.html, option B): a group
// joined from the start screen waits for Go. This card says so, and is where the person leaves it.

/**
 * One group the person is in, on the start screen. [isOut] is whether they are tracking right now: out,
 * the group can see them; not out, nobody can yet.
 */
@Composable
fun GroupInCard(
    group: SessionMembership,
    isOut: Boolean,
    onLeave: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var confirmLeave by remember { mutableStateOf(false) }
    OutlinedCard(
        modifier = modifier.fillMaxWidth(),
        border = BorderStroke(2.dp, MaterialTheme.colorScheme.outline),
    ) {
        Column(
            modifier = Modifier.padding(start = 16.dp, top = 12.dp, end = 16.dp, bottom = 4.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(
                text = "YOU'RE IN",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text(text = group.sessionName, style = MaterialTheme.typography.titleLarge)
            Text(
                text = if (isOut) {
                    "You're out, so everyone in the group can see you by name."
                } else {
                    "Not out yet. Nobody in the group can see you until you press Go."
                },
                style = MaterialTheme.typography.bodyMedium,
            )
            TextButton(onClick = { confirmLeave = true }) {
                Text("Leave group", color = MaterialTheme.colorScheme.error)
            }
        }
    }

    if (confirmLeave) {
        AlertDialog(
            onDismissRequest = { confirmLeave = false },
            title = { Text("Leave ${group.sessionName}?") },
            text = { Text("The group stops seeing you by name. Your crew still can.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmLeave = false
                    onLeave()
                }) { Text("Leave") }
            },
            dismissButton = {
                TextButton(onClick = { confirmLeave = false }) { Text("Stay in") }
            },
        )
    }
}
