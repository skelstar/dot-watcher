package nz.skelstar.dotwatcher.ui.nearby

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Group
import androidx.compose.material.icons.filled.Replay
import androidx.compose.material.icons.filled.Schedule
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.SessionUiState
import nz.skelstar.dotwatcher.nearby.NEARBY_RADIUS_OPTIONS
import nz.skelstar.dotwatcher.nearby.NearbyDraft
import nz.skelstar.dotwatcher.nearby.NearbyOrganiser
import nz.skelstar.dotwatcher.network.NearbyEvent

// Nearby join (see .ai/plans/nearby-join.md): what tapping a nearby run shows, the Create dialog's switch, the
// organiser's controls on the map, and the one-active-session prompt.

/**
 * What tapping a nearby run shows: who will see you, and when. Not a choice between runner and viewer:
 * watching without sharing is the shared watch link. [state] is the start screen's, so the sheet stays up
 * while joining and shows what went wrong; on success the app moves on and the sheet goes with its screen.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NearbyJoinSheet(
    event: NearbyEvent,
    state: SessionUiState,
    onJoin: () -> Unit,
    onDismiss: () -> Unit,
) {
    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(horizontal = 28.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(
                    text = event.eyebrow.uppercase(),
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Text(text = event.title, style = MaterialTheme.typography.headlineSmall)
                Text(text = event.subtitle, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }

            InfoRow(Icons.Default.Group, "Everyone in the run sees your name and live location.")
            InfoRow(
                Icons.Default.Schedule,
                "Only while you're sharing. You choose how long, and you can leave any time.",
            )
            InfoRow(Icons.Default.Replay, "A replay of the run is kept afterwards.")

            (state as? SessionUiState.Error)?.let {
                Text(text = it.message, color = MaterialTheme.colorScheme.error)
            }

            Button(
                onClick = onJoin,
                enabled = state !is SessionUiState.Loading,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (state is SessionUiState.Loading) "Joining…" else "Join this run")
            }
            TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) {
                Text("Not now")
            }
        }
    }
}

@Composable
private fun InfoRow(icon: ImageVector, text: String) {
    Row(verticalAlignment = Alignment.Top) {
        Icon(
            imageVector = icon,
            contentDescription = null,
            modifier = Modifier.size(24.dp),
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(modifier = Modifier.width(12.dp))
        Text(text = text, modifier = Modifier.weight(1f))
    }
}

/** The "Let people nearby join" switch and range, inside the Create dialog. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NearbyCreateSection(
    draft: NearbyDraft,
    onChange: (NearbyDraft) -> Unit,
    hasPreciseLocation: Boolean,
) {
    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(text = "Let people nearby join", modifier = Modifier.weight(1f))
            Switch(checked = draft.isOn, onCheckedChange = { onChange(draft.copy(isOn = it)) })
        }
        Text(
            text = "People near you can find this run and join. Turn off any time.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (draft.isOn) {
            Text(text = "Range", style = MaterialTheme.typography.labelLarge)
            RadiusChips(selected = draft.radiusMeters, onSelect = { onChange(draft.copy(radiusMeters = it)) })
            Text(
                text = "GPS can be off by 10 to 50 m, so a wider range is easier to join from.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (!hasPreciseLocation) {
                Text(
                    text = "Precise location is off for Dot Watcher, so people nearby can't find this run. " +
                        "Turn it on in Settings.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RadiusChips(selected: Int, onSelect: (Int) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        NEARBY_RADIUS_OPTIONS.forEach { metres ->
            FilterChip(
                selected = selected == metres,
                onClick = { onSelect(metres) },
                label = { Text("$metres m") },
            )
        }
    }
}

/**
 * The organiser's nearby controls, opened from the map's join-location button: range, setting the join
 * location to here, stopping the run showing nearby, and End session (after a confirmation). The result of each
 * action is shown in the sheet.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NearbyOrganiserSheet(organiser: NearbyOrganiser, onDismiss: () -> Unit) {
    val scope = rememberCoroutineScope()
    var radius by remember(organiser.status.radiusMeters) { mutableStateOf(organiser.status.radiusMeters) }
    var message by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    var confirmEnd by remember { mutableStateOf(false) }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(horizontal = 28.dp, vertical = 8.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text(text = "Nearby join", style = MaterialTheme.typography.headlineSmall)
            Text(
                text = if (organiser.status.nearby) {
                    "People within ${organiser.status.radiusMeters} m of your join location can find this run " +
                        "and join. Move it with the button below, for example from the start to the finish."
                } else {
                    "Nearby join is off. Only people with the invite code can join."
                },
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )

            RadiusChips(selected = radius, onSelect = { radius = it })

            Button(
                onClick = {
                    scope.launch {
                        busy = true
                        message = organiser.setJoinLocation(radius)
                        busy = false
                    }
                },
                enabled = !busy,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(if (busy) "Working…" else "Set join location here")
            }

            if (organiser.status.nearby) {
                OutlinedButton(
                    onClick = {
                        scope.launch {
                            busy = true
                            message = organiser.stopShowing()
                            busy = false
                        }
                    },
                    enabled = !busy,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text("Stop showing this run nearby")
                }
            }

            message?.let { Text(text = it, style = MaterialTheme.typography.bodyMedium) }

            HorizontalDivider()

            TextButton(onClick = { confirmEnd = true }, modifier = Modifier.fillMaxWidth()) {
                Text("End session", color = MaterialTheme.colorScheme.error)
            }
        }
    }

    if (confirmEnd) {
        AlertDialog(
            onDismissRequest = { confirmEnd = false },
            title = { Text("End this session?") },
            text = { Text("This ends it for everyone. Replay stays available.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmEnd = false
                    scope.launch { organiser.endSession()?.let { message = it } }
                }) {
                    Text("End session", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = { TextButton(onClick = { confirmEnd = false }) { Text("Cancel") } },
        )
    }
}

/** Creating was refused because the person already has a session running. Their entries are kept, so ending
 *  the old one can carry straight on to creating the new one. */
@Composable
fun ActiveSessionDialog(onEndAndCreate: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("You already have an active session") },
        text = {
            Text("Only one session can run at a time. Ending it removes everyone from it. Its replay stays available.")
        },
        confirmButton = { TextButton(onClick = onEndAndCreate) { Text("End it and create this one") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
