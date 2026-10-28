package nz.skelstar.dotwatcher.ui.crew

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material.icons.filled.Share
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.crew.CrewConfirm
import nz.skelstar.dotwatcher.crew.CrewLink
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.crew.CrewUiState
import nz.skelstar.dotwatcher.crew.MAX_NICKNAME_LENGTH
import nz.skelstar.dotwatcher.crew.crewExpiryLabel
import nz.skelstar.dotwatcher.crew.crewSinceLabel
import nz.skelstar.dotwatcher.crew.label
import nz.skelstar.dotwatcher.network.CrewMember
import nz.skelstar.dotwatcher.ui.CodeBoxField
import nz.skelstar.dotwatcher.ui.CrewDot
import nz.skelstar.dotwatcher.ui.colorFor
import java.time.Instant
import java.time.ZoneId

// The crew sheets, opened from the "Your crew" row on the start screen. Crew sheet -> Add sheet ->
// (Show my code) is the whole flow; the "Add X to your crew?" confirmation lives at the app level
// (CrewConfirmDialog) because a crew link can arrive on any screen.

/** Which crew sheet is open. They stack as a flow: the crew, then adding someone, then (from there)
 *  showing your own code; dismissing steps back one. */
enum class CrewStage { None, List, Add, MyCode }

/** The crew sheets for [stage]. Shared by the start screen and the casual map, which both open it. */
@Composable
fun CrewFlow(crew: CrewModel, state: CrewUiState, stage: CrewStage, onStage: (CrewStage) -> Unit) {
    when (stage) {
        CrewStage.None -> Unit
        CrewStage.List -> CrewSheet(
            crew = crew,
            state = state,
            onAdd = { onStage(CrewStage.Add) },
            onDismiss = { onStage(CrewStage.None) },
        )
        CrewStage.Add -> AddCrewSheet(
            crew = crew,
            state = state,
            onShowMyCode = { onStage(CrewStage.MyCode) },
            onDismiss = { onStage(CrewStage.List) },
        )
        CrewStage.MyCode -> MyCrewCodeSheet(
            crew = crew,
            state = state,
            onDismiss = { onStage(CrewStage.List) },
        )
    }
}

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun CrewSheet(
    crew: CrewModel,
    state: CrewUiState,
    onAdd: () -> Unit,
    onDismiss: () -> Unit,
) {
    var selectedId by remember { mutableStateOf<String?>(null) }
    var confirmRemove by remember { mutableStateOf<CrewMember?>(null) }
    val selected = state.members.firstOrNull { it.userId == selectedId }

    // Opening the crew is what clears the start screen's "joined" note.
    LaunchedEffect(Unit) { crew.markMembersSeen() }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Your crew", style = MaterialTheme.typography.titleLarge)
            Text(
                text = "People you have chosen. They see you in their crew too, and you can remove each other " +
                    "at any time. Removing someone takes effect straight away.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )

            Text("In your crew · ${state.members.size}", style = MaterialTheme.typography.titleSmall)
            if (state.members.isEmpty()) {
                Text(
                    text = "No one yet. Ask someone to send you a crew code, or send them yours.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                FlowRow(
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    state.members.forEach { member ->
                        val on = member.userId == selectedId
                        Column(
                            horizontalAlignment = Alignment.CenterHorizontally,
                            modifier = Modifier
                                .width(72.dp)
                                .background(
                                    if (on) MaterialTheme.colorScheme.surfaceVariant else androidx.compose.ui.graphics.Color.Transparent,
                                    RoundedCornerShape(12.dp),
                                )
                                .clickable { selectedId = if (on) null else member.userId }
                                .padding(vertical = 8.dp)
                                .semantics { contentDescription = "${member.label()}, in your crew" },
                        ) {
                            CrewDot(initials = member.displayName, color = state.colorFor(member.userId), size = 38.dp)
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = member.label(),
                                style = MaterialTheme.typography.labelSmall,
                                maxLines = 1,
                                color = if (on) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
                if (selected == null) {
                    Text(
                        text = "Tap someone to see their details.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                } else {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp))
                            .padding(12.dp),
                    ) {
                        CrewDot(initials = selected.displayName, color = state.colorFor(selected.userId))
                        Spacer(modifier = Modifier.width(12.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Text(selected.label(), style = MaterialTheme.typography.titleMedium)
                            Text(
                                text = "Crew " + crewSinceLabel(selected.since, Instant.now(), ZoneId.systemDefault()),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        TextButton(onClick = { confirmRemove = selected }) {
                            Text("Remove", color = MaterialTheme.colorScheme.error)
                        }
                    }
                }
            }

            state.removeError?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            }

            Button(onClick = onAdd, modifier = Modifier.fillMaxWidth()) { Text("Add someone") }
        }
    }

    confirmRemove?.let { member ->
        AlertDialog(
            onDismissRequest = { confirmRemove = null },
            title = { Text("Remove ${member.label()} from your crew?") },
            text = { Text("They'll no longer be in your crew and you won't be in theirs. They aren't told.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmRemove = null
                    selectedId = null
                    crew.remove(member.userId)
                }) { Text("Remove", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { confirmRemove = null }) { Text("Cancel") } },
        )
    }
}

/** Enter a code someone sent you, or go and show your own. There is no scanner here: to scan a QR
 *  code, people open their phone's camera, which opens the app through the link the code holds. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddCrewSheet(
    crew: CrewModel,
    state: CrewUiState,
    onShowMyCode: () -> Unit,
    onDismiss: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    val clipboard = LocalClipboardManager.current

    // Once someone has been added the work here is done.
    LaunchedEffect(state.confirm) {
        if (state.confirm is CrewConfirm.Done) onDismiss()
    }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Add to your crew", style = MaterialTheme.typography.titleLarge)
            Text(
                text = "Ask them to send you their crew code, then enter it here. If they show you a QR code, " +
                    "just open your camera and point it at the code.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("Enter their crew code", style = MaterialTheme.typography.labelMedium)
                TextButton(onClick = {
                    clipboard.getText()?.text?.let { pasted ->
                        code = CrewLink.extractFromPaste(pasted) ?: pasted.trim().take(CrewLink.CODE_LENGTH).uppercase()
                    }
                }) {
                    Icon(Icons.Default.ContentPaste, contentDescription = null, modifier = Modifier.size(18.dp))
                    Spacer(modifier = Modifier.width(4.dp))
                    Text("Paste")
                }
            }
            CodeBoxField(
                value = code,
                onValueChange = { code = it },
                length = CrewLink.CODE_LENGTH,
                extractCode = CrewLink::extractFromPaste,
                modifier = Modifier.fillMaxWidth(),
            )

            Button(
                onClick = { crew.beginAdd(code) },
                enabled = code.length == CrewLink.CODE_LENGTH && state.confirm == null,
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Add") }

            OutlinedButton(onClick = onShowMyCode, modifier = Modifier.fillMaxWidth()) {
                Text("Show my code instead")
            }
        }
    }
}

/** The code to send to one person: a QR for in person, the code itself, and a Share button. The
 *  first time, it asks what name the crew should see (skippable; the account's initials are used
 *  otherwise). */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MyCrewCodeSheet(
    crew: CrewModel,
    state: CrewUiState,
    onDismiss: () -> Unit,
) {
    val context = LocalContext.current
    // Naming comes first only when no nickname is set, and only once per opening of the sheet.
    var naming by remember { mutableStateOf(state.nickname == null) }
    var draft by remember { mutableStateOf("") }

    LaunchedEffect(naming) {
        if (!naming) crew.loadMyCode()
    }

    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true)) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            if (naming) {
                NamePrompt(
                    draft = draft,
                    onDraftChange = {
                        draft = it
                        crew.clearNicknameError()
                    },
                    error = state.nicknameError,
                    onSave = { crew.setNickname(draft) { naming = false } },
                    onSkip = { naming = false },
                )
            } else {
                CodeContent(state = state, context = context, onNewCode = crew::newMyCode)
            }
        }
    }
}

@Composable
private fun NamePrompt(
    draft: String,
    onDraftChange: (String) -> Unit,
    error: String?,
    onSave: () -> Unit,
    onSkip: () -> Unit,
) {
    Text("What should your crew call you?", style = MaterialTheme.typography.titleLarge, textAlign = TextAlign.Center)
    Text(
        text = "People you add see this name. If you skip it they see your initials. You can change it later in your account.",
        style = MaterialTheme.typography.bodyMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        textAlign = TextAlign.Center,
    )
    OutlinedTextField(
        value = draft,
        onValueChange = { if (it.length <= MAX_NICKNAME_LENGTH) onDraftChange(it.replace("\n", "")) },
        label = { Text("Your name") },
        singleLine = true,
        isError = error != null,
        supportingText = error?.let { { Text(it) } },
        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Words),
        modifier = Modifier.fillMaxWidth(),
    )
    Button(onClick = onSave, enabled = draft.isNotBlank(), modifier = Modifier.fillMaxWidth()) { Text("Continue") }
    TextButton(onClick = onSkip, modifier = Modifier.fillMaxWidth()) { Text("Skip") }
}

@Composable
private fun CodeContent(state: CrewUiState, context: Context, onNewCode: () -> Unit) {
    Text("Your crew code", style = MaterialTheme.typography.titleLarge)
    val code = state.myCode

    when {
        code != null -> {
            val url = CrewLink.url(BuildConfig.WEB_BASE_URL, code.code)
            QrCode(content = url, contentDescription = "QR code for crew code ${code.code.toCharArray().joinToString(" ")}")
            Text(
                text = code.code.chunked(4).joinToString("  "),
                fontSize = 30.sp,
                fontFamily = FontFamily.Monospace,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.semantics { contentDescription = "Crew code ${code.code.toCharArray().joinToString(" ")}" },
            )
            Text(
                text = crewExpiryLabel(code.expiresAt, Instant.now()),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text(
                text = "Send this to one person. It works once.",
                style = MaterialTheme.typography.titleSmall,
                textAlign = TextAlign.Center,
            )
            Text(
                text = "In person, they can scan the QR code with their camera. Otherwise share it as a message.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
            Button(onClick = { context.shareCrewCode(code.code) }, modifier = Modifier.fillMaxWidth()) {
                Icon(Icons.Default.Share, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(8.dp))
                Text("Share code")
            }
            TextButton(onClick = onNewCode, enabled = !state.myCodeLoading, modifier = Modifier.fillMaxWidth()) {
                Text("Make a new code")
            }
            Text(
                text = "Making a new code stops this one working.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        state.myCodeLoading -> Box(modifier = Modifier.padding(32.dp)) { CircularProgressIndicator() }
        else -> {
            Text(
                text = state.myCodeError ?: "Couldn't make a code. Try again.",
                color = MaterialTheme.colorScheme.error,
                textAlign = TextAlign.Center,
            )
            OutlinedButton(onClick = onNewCode, modifier = Modifier.fillMaxWidth()) { Text("Try again") }
        }
    }
}

/** Opens the share sheet with the crew-code message: a link that opens the app (or the web
 *  fallback), then the plain code. Mirrors how session invite codes are shared. */
private fun Context.shareCrewCode(code: String) {
    val sendIntent = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, CrewLink.shareMessage(BuildConfig.WEB_BASE_URL, code))
    }
    startActivity(Intent.createChooser(sendIntent, "Send crew code"))
}
