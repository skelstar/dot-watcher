package nz.skelstar.dotwatcher.ui.groups

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Surface
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.groups.groupTime
import java.time.Instant
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
import nz.skelstar.dotwatcher.groups.GroupTime
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
    /** When the group ends; null (an older server) shows no time. */
    time: GroupTime? = null,
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
                text = time?.let { "YOU'RE IN · ENDS ${it.endsAt} · ${it.left.uppercase()}" } ?: "YOU'RE IN",
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

/**
 * Top-left on the Go map, under the wordmark: "‹ Home" to reach the start screen while tracking carries on (to
 * join a group while out), and, when the person is in any groups, a chip naming the newest with its time left.
 * Tapping the chip opens [GroupsOnMapSheet].
 */
@Composable
fun GoMapGroupBar(
    groups: List<SessionMembership>,
    now: Instant,
    onHome: () -> Unit,
    onOpenGroups: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(8.dp)) {
        MapPill(onClick = onHome, contentDescription = "Back to the start screen") {
            Text(text = "‹ Home", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = Color(0xFF1B2321))
        }
        groups.firstOrNull()?.let { newest ->
            val time = groupTime(newest.endsAt, now)
            MapPill(onClick = onOpenGroups, contentDescription = "Your groups") {
                Column {
                    Text(
                        text = buildString {
                            append(if (groups.size > 1) "${groups.size} GROUPS" else "GROUP")
                            time?.let { append(" · ${it.left.uppercase()}") }
                        },
                        fontSize = 9.sp,
                        letterSpacing = 0.8.sp,
                        color = Color(0xFF5A6662),
                    )
                    Text(text = newest.sessionName, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color(0xFF1B2321))
                }
            }
        }
    }
}

@Composable
private fun MapPill(onClick: () -> Unit, contentDescription: String, content: @Composable () -> Unit) {
    Surface(
        shape = RoundedCornerShape(14.dp),
        color = Color.White.copy(alpha = 0.94f),
        shadowElevation = 3.dp,
        modifier = Modifier.clickable(role = Role.Button, onClickLabel = contentDescription, onClick = onClick),
    ) {
        Row(modifier = Modifier.padding(horizontal = 12.dp, vertical = 7.dp)) { content() }
    }
}

/** The groups the person is in, from the Go map: each with its end time and Leave. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GroupsOnMapSheet(
    groups: List<SessionMembership>,
    now: Instant,
    onLeave: (sessionId: String) -> Unit,
    onDismiss: () -> Unit,
    error: String? = null,
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(horizontal = 24.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(text = "Your groups", style = MaterialTheme.typography.headlineSmall)
            Text(
                text = "Everyone in these groups sees you by name while you're out.",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (groups.isEmpty()) {
                Text("You're not in any groups now.")
            }
            groups.forEach { group ->
                GroupInCard(
                    group = group,
                    isOut = true,
                    time = groupTime(group.endsAt, now),
                    onLeave = { onLeave(group.sessionId) },
                )
            }
            error?.let { Text(text = it, color = MaterialTheme.colorScheme.error) }
            TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("Done") }
        }
    }
}
