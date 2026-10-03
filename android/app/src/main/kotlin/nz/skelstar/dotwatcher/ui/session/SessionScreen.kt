package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.HelpOutline
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.SessionUiState
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.ui.CodeBoxField
import nz.skelstar.dotwatcher.ui.GroupDivider
import nz.skelstar.dotwatcher.ui.GroupedCard

/**
 * Lets a signed-in runner either start a new session ([POST /sessions]) or join an existing one
 * by invite code ([POST /session-invites/{inviteCode}/join] with role "runner" — repo root
 * README.md, "Auth model"), or tap to rejoin a session they've previously left
 * ([recentSessions], [GET /me/sessions/recent]) — matches iOS's `noSessionView`'s
 * `recentSessionsCard` (ContentView.swift). iOS shows this screen only when there's no active
 * session; Android's single linear flow reaches it the same way (via [leaveSession][
 * nz.skelstar.dotwatcher.AppViewModel.leaveSession] or first sign-in).
 *
 * Laid out to match the real iOS screen (media-files/Simulator Screenshot ... 16.19.49 / 16.23.23
 * / 16.20.27.png): an app header, a signed-in status row, a single "Join a Session" card (session
 * creation lives behind a "Create one" link that opens a dialog, not a second inline card), then
 * "Recent Sessions" below.
 *
 * No display-name field here or in the create dialog: iOS doesn't ask per-session either — it
 * auto-fills from the account's own registered display name on every login
 * (`LocationManager.swift`'s `authenticate(path:body:)` sets `runnerName =
 * session.user.displayName.uppercased()`), only falling back to a one-time 2-letter-initials
 * entry sheet in the rare case that's empty. Android leaves `displayName` unset on
 * create/join, which the server already defaults to the account's own display name
 * (`server/Controllers/SessionsController.cs`) — the same outcome as iOS's common path,
 * without needing to port that rarely-hit fallback sheet.
 */
@Composable
fun SessionScreen(
    state: SessionUiState,
    recentSessions: List<SessionMembership>,
    onCreateSession: (sessionName: String?) -> Unit,
    onJoinSession: (inviteCode: String) -> Unit,
    onRejoinSession: (inviteCode: String) -> Unit,
    onRefresh: () -> Unit,
    onSignOut: () -> Unit,
) {
    var inviteCode by remember { mutableStateOf("") }
    var showCreateDialog by remember { mutableStateOf(false) }
    val clipboard = LocalClipboardManager.current

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
    ) {
        AppHeader(onAccountClick = onSignOut)
        Spacer(modifier = Modifier.height(24.dp))

        StatusRow(onRefresh = onRefresh)
        Spacer(modifier = Modifier.height(24.dp))

        SectionLabel("Join a Session")
        GroupedCard(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.padding(16.dp)) {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = "Enter your invite code",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    TextButton(onClick = {
                        clipboard.getText()?.text?.let { pasted ->
                            inviteCode = pasted
                        }
                    }) {
                        Icon(
                            imageVector = Icons.Default.ContentPaste,
                            contentDescription = null,
                            modifier = Modifier.size(18.dp),
                        )
                        Spacer(modifier = Modifier.width(4.dp))
                        Text("Paste")
                    }
                }
                Spacer(modifier = Modifier.height(8.dp))
                CodeBoxField(
                    value = inviteCode,
                    onValueChange = { inviteCode = it },
                    length = 6,
                )
            }
            GroupDivider()
            CardActionRow(
                text = "Join Session",
                // Requires the full 6 characters, matching iOS's noSessionJoinCard
                // (`disabled(... || noSessionInviteCode.count < 6)`).
                enabled = state !is SessionUiState.Loading && inviteCode.length == 6,
                onClick = { onJoinSession(inviteCode) },
            )
        }

        Spacer(modifier = Modifier.height(16.dp))

        when (state) {
            is SessionUiState.Loading -> CircularProgressIndicator()
            is SessionUiState.Error -> Text(
                text = state.message,
                color = MaterialTheme.colorScheme.error,
            )
            SessionUiState.Idle -> Unit
        }

        if (recentSessions.isNotEmpty()) {
            Spacer(modifier = Modifier.height(24.dp))
            SectionLabel("Recent Sessions")
            RecentSessionsCard(
                sessions = recentSessions,
                enabled = state !is SessionUiState.Loading,
                onRejoin = onRejoinSession,
            )
        }

        Spacer(modifier = Modifier.height(24.dp))
        Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            TextButton(onClick = { showCreateDialog = true }) {
                Text("Session doesn't exist yet? Create one")
            }
        }

        Spacer(modifier = Modifier.height(24.dp))
        Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
            TextButton(onClick = onSignOut) {
                Text("Sign out")
            }
        }
    }

    if (showCreateDialog) {
        CreateSessionDialog(
            enabled = state !is SessionUiState.Loading,
            onCreate = { sessionName ->
                showCreateDialog = false
                onCreateSession(sessionName)
            },
            onCancel = { showCreateDialog = false },
        )
    }
}

/** App wordmark + badge + build info, with account/help icon buttons — matches the iOS header
 *  shown atop every no-session-screen state (media-files/Simulator Screenshot ... 16.19.49.png).
 *  No git-commit short-SHA (iOS's "1105bf8"): that needs a dedicated build-time step this app
 *  doesn't have yet, so this only shows [BuildConfig.VERSION_NAME]/[BuildConfig.VERSION_CODE],
 *  which are already available. */
@Composable
private fun AppHeader(onAccountClick: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top,
    ) {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(text = "dot", style = MaterialTheme.typography.headlineLarge)
                Box(
                    modifier = Modifier
                        .padding(horizontal = 4.dp)
                        .size(32.dp)
                        .background(MaterialTheme.colorScheme.primary, CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = "DW",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onPrimary,
                    )
                }
                Text(text = "watchr", style = MaterialTheme.typography.headlineLarge)
            }
            Text(
                text = "v${BuildConfig.VERSION_NAME} · build ${BuildConfig.VERSION_CODE}",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Row {
            IconButton(onClick = onAccountClick) {
                Icon(imageVector = Icons.Default.AccountCircle, contentDescription = "Account")
            }
            IconButton(onClick = {}) {
                Icon(imageVector = Icons.AutoMirrored.Filled.HelpOutline, contentDescription = "Help")
            }
        }
    }
}

/** Colored dot + "Signed in" + refresh button, matching iOS's status row (media-files/
 *  Simulator Screenshot ... 16.19.49.png) — this screen is only ever reached signed-in, so unlike
 *  iOS (which also shows "Idle" for its separate tracking/connection status), there's no other
 *  state this row needs to reflect yet. [onRefresh] re-fetches the recent-sessions list. */
@Composable
private fun StatusRow(onRefresh: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(
                modifier = Modifier
                    .size(10.dp)
                    .background(Color(0xFFFF9500), CircleShape),
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(text = "Signed in", style = MaterialTheme.typography.titleMedium)
        }
        IconButton(onClick = onRefresh) {
            Icon(imageVector = Icons.Default.Refresh, contentDescription = "Refresh")
        }
    }
}

/** Small caps-style section header above a [GroupedCard], matching the iOS screenshots'
 *  "Join a Session" / "Recent Sessions" labels. */
@Composable
private fun SectionLabel(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(start = 4.dp, bottom = 8.dp),
    )
}

/** A left-aligned text "button" styled as a plain row inside a [GroupedCard], matching iOS's
 *  full-width tappable rows rather than Material3's pill-shaped [androidx.compose.material3.Button]. */
@Composable
private fun CardActionRow(text: String, enabled: Boolean, onClick: () -> Unit) {
    TextButton(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(
            text = text,
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 4.dp),
        )
    }
}

@Composable
private fun RecentSessionsCard(
    sessions: List<SessionMembership>,
    enabled: Boolean,
    onRejoin: (inviteCode: String) -> Unit,
) {
    val clipboard = LocalClipboardManager.current
    GroupedCard(modifier = Modifier.fillMaxWidth()) {
        sessions.forEachIndexed { index, membership ->
            if (index > 0) GroupDivider()
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable(enabled = enabled) { onRejoin(membership.inviteCode) }
                    .padding(horizontal = 16.dp, vertical = 12.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column {
                    Text(text = membership.sessionName, style = MaterialTheme.typography.bodyLarge)
                    Text(
                        text = "Invite ${membership.inviteCode}",
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                IconButton(onClick = { clipboard.setText(AnnotatedString(membership.inviteCode)) }) {
                    Icon(
                        imageVector = Icons.Default.ContentCopy,
                        contentDescription = "Copy invite code",
                        tint = MaterialTheme.colorScheme.primary,
                    )
                }
            }
        }
    }
}

/** Matches iOS's "Create a New Session" sheet (media-files/Simulator Screenshot ...
 *  16.20.27.png): a centered title/subtitle, an 8-box session-name field, a filled action
 *  button, and a plain-text cancel row — reached from [SessionScreen]'s "Create one" link
 *  rather than being a second card on the main screen.
 *
 * A plain [Dialog] rather than [AlertDialog]: Material3's AlertDialog hard-codes a tinted
 * `surfaceContainerHigh` background and generous title/content padding that read as a stock
 * Android sheet rather than iOS's plain white, tightly-spaced card — using Dialog gives full
 * control over both to match. */
@Composable
private fun CreateSessionDialog(
    enabled: Boolean,
    onCreate: (sessionName: String?) -> Unit,
    onCancel: () -> Unit,
) {
    var sessionName by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onCancel) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(20.dp))
                .padding(24.dp),
        ) {
            Text(
                text = "Create a New Session",
                textAlign = TextAlign.Center,
                style = MaterialTheme.typography.titleLarge,
            )
            Spacer(modifier = Modifier.height(12.dp))
            Text(
                text = "Give your session a name. You'll get an invite code to share with others.",
                textAlign = TextAlign.Center,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(modifier = Modifier.height(20.dp))
            CodeBoxField(
                value = sessionName,
                onValueChange = { sessionName = it },
                length = 8,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(modifier = Modifier.height(20.dp))
            Button(
                onClick = { onCreate(sessionName.ifBlank { null }) },
                enabled = enabled && (sessionName.isEmpty() || sessionName.length >= 4),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("Create Session")
            }
            Spacer(modifier = Modifier.height(4.dp))
            TextButton(onClick = onCancel, modifier = Modifier.fillMaxWidth()) {
                Text("Cancel")
            }
        }
    }
}
