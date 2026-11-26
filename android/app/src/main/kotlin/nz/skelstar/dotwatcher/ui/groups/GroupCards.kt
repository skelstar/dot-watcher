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
import nz.skelstar.dotwatcher.groups.GroupOrganiserActions
import nz.skelstar.dotwatcher.ui.nearby.EntryCodeSection
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.network.SessionMembership

// Groups (see .ai/plans/go-and-groups.md, mock-up .ai/mockups/go-and-groups-before-go.html, option B): a group
// joined from the start screen waits for Go. This card says so, and is where the person leaves it.

/**
 * One group the person is in, on the start screen. [isOut] is whether they are tracking right now: out,
 * the group can see them; not out, nobody can yet. With [organiser] (their own group) the card carries the
 * organiser's controls, since a group has no session map: Ping here, Choose on map (a join location somewhere
 * else), the entry code, and End group in place of Leave.
 */
@Composable
fun GroupInCard(
    group: SessionMembership,
    isOut: Boolean,
    onLeave: () -> Unit,
    modifier: Modifier = Modifier,
    /** When the group ends; null (an older server) shows no time. */
    time: GroupTime? = null,
    organiser: GroupOrganiserActions? = null,
) {
    var confirmLeave by remember { mutableStateOf(false) }
    var confirmEnd by remember { mutableStateOf(false) }
    var showCode by remember { mutableStateOf(false) }
    var showPicker by remember { mutableStateOf(false) }
    /** A nearby group's entry code, loaded for the dashed code box; null while loading or when there's none. */
    var entryCode by remember(group.sessionId) { mutableStateOf<String?>(null) }
    var message by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val entryCodeActions = organiser?.entryCode
    LaunchedEffect(group.sessionId, entryCodeActions != null) {
        entryCodeActions?.load()?.onSuccess { entryCode = it }
    }
    // The code joiners need (mock-up .ai/mockups/go-and-groups-join.html): a nearby group's entry code when
    // one is set, otherwise a private group's invite code.
    val shownCode: Pair<String, String>? = when {
        group.nearby == true -> entryCode?.let { "Entry code" to it }
        group.inviteCode.isNotEmpty() -> "Invite code" to group.inviteCode
        else -> null
    }
    OutlinedCard(
        modifier = modifier.fillMaxWidth(),
        border = BorderStroke(2.dp, MaterialTheme.colorScheme.outline),
    ) {
        Column(
            modifier = Modifier.padding(start = 16.dp, top = 12.dp, end = 16.dp, bottom = 4.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            val label = if (organiser != null) "YOUR GROUP" else "YOU'RE IN"
            Text(
                text = time?.let { "$label · ENDS ${it.endsAt} · ${it.left.uppercase()}" } ?: label,
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text(text = group.sessionName, style = MaterialTheme.typography.titleLarge)
            Text(
                text = when {
                    organiser != null && group.nearby == true ->
                        "People near your join location see it on their start screen. You don't need to be out."
                    organiser != null -> "Only people with the invite code can join."
                    isOut -> "You're out, so everyone in the group can see you by name."
                    else -> "Not out yet. Nobody in the group can see you until you press Go."
                },
                style = MaterialTheme.typography.bodyMedium,
            )
            if (organiser == null) {
                TextButton(onClick = { confirmLeave = true }) {
                    Text("Leave group", color = MaterialTheme.colorScheme.error)
                }
            } else {
                shownCode?.let { (label, code) ->
                    CodeBox(
                        label = label,
                        code = code,
                        // Invite codes are fixed; only a nearby group's entry code can change.
                        changeLabel = if (entryCodeActions != null) (if (showCode) "Done" else "Change") else null,
                        onChange = { showCode = !showCode },
                    )
                }
                // Two rows: where people join (Ping here, or a point chosen on the map), then the code and End.
                if (organiser.ping != null || organiser.setJoinLocationAt != null) {
                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        organiser.ping?.let { ping ->
                            TextButton(
                                onClick = {
                                    scope.launch {
                                        busy = true
                                        message = ping()
                                        busy = false
                                    }
                                },
                                enabled = !busy,
                            ) { Text("Ping here") }
                        }
                        if (organiser.setJoinLocationAt != null) {
                            TextButton(onClick = { showPicker = true }, enabled = !busy) { Text("Choose on map") }
                        }
                    }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    // With a code set, the box above carries Change; this is only for adding one.
                    if (entryCodeActions != null && entryCode == null) {
                        TextButton(onClick = { showCode = !showCode }) {
                            Text(if (showCode) "Hide code" else "Entry code")
                        }
                    }
                    TextButton(onClick = { confirmEnd = true }, enabled = !busy) {
                        Text("End group", color = MaterialTheme.colorScheme.error)
                    }
                }
                if (showCode) entryCodeActions?.let { EntryCodeSection(it, onChanged = { code -> entryCode = code }) }
                if (showPicker) {
                    JoinLocationPickerDialog(
                        group = group,
                        organiser = organiser,
                        onDismiss = { showPicker = false },
                        onSet = { message = it },
                    )
                }
                message?.let { Text(text = it, style = MaterialTheme.typography.bodyMedium) }
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

    if (confirmEnd && organiser != null) {
        AlertDialog(
            onDismissRequest = { confirmEnd = false },
            title = { Text("End ${group.sessionName}?") },
            text = { Text("It ends for everyone and nobody new can join. Anyone out carries on with their own run.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmEnd = false
                    scope.launch {
                        busy = true
                        message = organiser.end()
                        busy = false
                    }
                }) { Text("End group", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { confirmEnd = false }) { Text("Keep it") } },
        )
    }
}

/** A join code inside a dashed box, with Change on the right when [changeLabel] is set. */
@Composable
private fun CodeBox(label: String, code: String, changeLabel: String?, onChange: () -> Unit) {
    val ink = MaterialTheme.colorScheme.onSurface
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier
            .padding(top = 6.dp)
            .fillMaxWidth()
            .drawBehind {
                drawRoundRect(
                    color = ink,
                    cornerRadius = CornerRadius(8.dp.toPx()),
                    style = Stroke(
                        width = 1.5.dp.toPx(),
                        pathEffect = PathEffect.dashPathEffect(floatArrayOf(4.dp.toPx(), 3.dp.toPx())),
                    ),
                )
            }
            .padding(start = 10.dp, end = 2.dp, top = 2.dp, bottom = 2.dp)
            .heightIn(min = 40.dp),
    ) {
        Text(text = label, style = MaterialTheme.typography.bodyMedium)
        SelectionContainer(modifier = Modifier.weight(1f)) {
            Text(
                text = code,
                fontFamily = FontFamily.Monospace,
                fontWeight = FontWeight.Bold,
                fontSize = 18.sp,
                letterSpacing = 2.sp,
            )
        }
        changeLabel?.let { TextButton(onClick = onChange) { Text(it) } }
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
    /** The organiser's controls for groups the person organised; null shows Leave on every card. */
    organiserActions: ((SessionMembership) -> GroupOrganiserActions)? = null,
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
                    organiser = organiserActions?.takeIf { group.isOwner }?.invoke(group),
                )
            }
            error?.let { Text(text = it, color = MaterialTheme.colorScheme.error) }
            TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("Done") }
        }
    }
}
