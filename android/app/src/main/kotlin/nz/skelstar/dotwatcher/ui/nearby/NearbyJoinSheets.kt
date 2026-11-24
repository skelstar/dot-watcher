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
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.filled.Flag
import androidx.compose.material.icons.filled.Visibility
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.SessionUiState
import nz.skelstar.dotwatcher.nearby.NEARBY_RADIUS_OPTIONS
import nz.skelstar.dotwatcher.nearby.cleanEntryCode
import nz.skelstar.dotwatcher.nearby.isValidEntryCode
import nz.skelstar.dotwatcher.nearby.NearbyDraft
import nz.skelstar.dotwatcher.nearby.NearbyOrganiser
import nz.skelstar.dotwatcher.nearby.EntryCodeActions
import nz.skelstar.dotwatcher.network.NearbyEvent

// Nearby join (see .ai/plans/nearby-join.md): what tapping a nearby run shows, the Create dialog's switch, the
// organiser's controls on the map, and the one-active-session prompt.

/**
 * What tapping a nearby run shows: who will see you, and when. Not a choice between runner and viewer:
 * watching without sharing is the shared watch link. [state] is the start screen's, so the sheet stays up
 * while joining and shows what went wrong; on success the app moves on and the sheet goes with its screen
 * (or, for a group, the start screen closes it once the group shows as joined).
 *
 * [asGroup] is the "groups" feature (see .ai/plans/go-and-groups.md): joining only changes who sees you, and
 * nothing happens until you press Go. When the run asks for an entry code ([NearbyEvent.needsCode]) the sheet
 * has a field for it and [onJoin] gets what was typed.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NearbyJoinSheet(
    event: NearbyEvent,
    state: SessionUiState,
    onJoin: (entryCode: String?) -> Unit,
    onDismiss: () -> Unit,
    asGroup: Boolean = false,
    /** "Join and Go now": joins, then starts Go. Null hides it (not a group, or already out). */
    onJoinAndGo: ((entryCode: String?) -> Unit)? = null,
) {
    val needsCode = asGroup && event.needsCode == true
    var code by remember { mutableStateOf("") }
    val trimmedCode = code.trim()
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

            if (asGroup) {
                InfoRow(
                    Icons.Default.Group,
                    "Everyone in this group sees you by name on their map, wherever they are, while you're out.",
                )
                InfoRow(
                    Icons.Default.Flag,
                    "Not out yet? Join now and the group waits. Nobody sees you until you press Go.",
                )
                InfoRow(Icons.Default.Schedule, "Until the group ends. You can leave any time.")
                InfoRow(Icons.Default.Visibility, "Your run stays yours: joining doesn't change how you track.")
            } else {
                InfoRow(Icons.Default.Group, "Everyone in the run sees your name and live location.")
                InfoRow(
                    Icons.Default.Schedule,
                    "Only while you're sharing. You choose how long, and you can leave any time.",
                )
                InfoRow(Icons.Default.Replay, "A replay of the run is kept afterwards.")
            }

            if (needsCode) {
                OutlinedTextField(
                    value = code,
                    onValueChange = { code = cleanEntryCode(it) },
                    label = { Text("Entry code") },
                    supportingText = { Text("The organiser gave entrants this code.") },
                    singleLine = true,
                    isError = state is SessionUiState.Error,
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters),
                    modifier = Modifier.fillMaxWidth(),
                )
            }

            (state as? SessionUiState.Error)?.let {
                Text(text = it.message, color = MaterialTheme.colorScheme.error)
            }

            Button(
                onClick = { onJoin(if (needsCode) trimmedCode else null) },
                enabled = state !is SessionUiState.Loading && (!needsCode || isValidEntryCode(trimmedCode)),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(
                    when {
                        state is SessionUiState.Loading -> "Joining…"
                        asGroup -> "Join group"
                        else -> "Join this run"
                    }
                )
            }
            onJoinAndGo?.let { joinAndGo ->
                OutlinedButton(
                    onClick = { joinAndGo(if (needsCode) trimmedCode else null) },
                    enabled = state !is SessionUiState.Loading && (!needsCode || isValidEntryCode(trimmedCode)),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text("Join and Go now")
                }
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
    /** The "groups" feature: offers "Ask for a code to join". */
    groupsAvailable: Boolean = false,
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
            if (groupsAvailable) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(text = "Ask for a code to join", modifier = Modifier.weight(1f))
                    Switch(checked = draft.askForCode, onCheckedChange = { onChange(draft.copy(askForCode = it)) })
                }
                if (draft.askForCode) {
                    OutlinedTextField(
                        value = draft.entryCode,
                        onValueChange = { onChange(draft.copy(entryCode = cleanEntryCode(it))) },
                        label = { Text("Entry code") },
                        supportingText = {
                            Text("Give it to entrants. People nearby still see the run, but can't join without it.")
                        },
                        singleLine = true,
                        isError = draft.entryCode.isNotEmpty() && !draft.isEntryCodeValid,
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters),
                        modifier = Modifier.fillMaxWidth(),
                    )
                } else {
                    Text(
                        text = "Off: anyone nearby can join with one tap.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
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

            organiser.entryCode?.let { actions ->
                HorizontalDivider()
                EntryCodeSection(actions)
            }

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

/**
 * The organiser's entry code, inside their nearby sheet: what it is now, and a field to change or remove it.
 * People already in stay in; only joins from now on need the new code.
 */
@Composable
fun EntryCodeSection(actions: EntryCodeActions) {
    val scope = rememberCoroutineScope()
    var current by remember { mutableStateOf<String?>(null) }
    var loaded by remember { mutableStateOf(false) }
    var typed by remember { mutableStateOf("") }
    var message by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }

    LaunchedEffect(actions) {
        actions.load().fold(
            onSuccess = { current = it },
            onFailure = { message = it.message ?: "Couldn't load the entry code." },
        )
        loaded = true
    }

    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text(text = "Entry code", style = MaterialTheme.typography.titleMedium)
        Text(
            text = when {
                !loaded -> "Loading…"
                current == null -> "None. Anyone nearby can join with one tap."
                else -> "People need $current to join. Give it to entrants; anyone walking past can't join without it."
            },
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        OutlinedTextField(
            value = typed,
            onValueChange = { typed = cleanEntryCode(it) },
            label = { Text(if (current == null) "New code" else "Change to") },
            supportingText = { Text("4 to 8 letters or numbers. People already in stay in.") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters),
            modifier = Modifier.fillMaxWidth(),
        )
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = {
                    scope.launch {
                        busy = true
                        message = actions.change(typed)
                        actions.load().onSuccess { current = it }
                        typed = ""
                        busy = false
                    }
                },
                enabled = !busy && loaded && isValidEntryCode(typed),
                modifier = Modifier.weight(1f),
            ) { Text(if (current == null) "Ask for code" else "Change code") }
            if (current != null) {
                OutlinedButton(
                    onClick = {
                        scope.launch {
                            busy = true
                            message = actions.change(null)
                            actions.load().onSuccess { current = it }
                            busy = false
                        }
                    },
                    enabled = !busy,
                    modifier = Modifier.weight(1f),
                ) { Text("Remove code") }
            }
        }
        message?.let { Text(text = it, style = MaterialTheme.typography.bodyMedium) }
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
