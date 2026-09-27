package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.SessionUiState
import nz.skelstar.dotwatcher.network.SessionMembership

/**
 * Lets a signed-in runner either start a new session ([POST /sessions]) or join an existing one
 * by invite code ([POST /session-invites/{inviteCode}/join] with role "runner" — repo root
 * README.md, "Auth model"), or tap to rejoin a session they've previously left
 * ([recentSessions], [GET /me/sessions/recent]) — matches iOS's `noSessionView`'s
 * `recentSessionsCard` (ContentView.swift). iOS shows this screen only when there's no active
 * session; Android's single linear flow reaches it the same way (via [leaveSession][
 * nz.skelstar.dotwatcher.AppViewModel.leaveSession] or first sign-in).
 */
@Composable
fun SessionScreen(
    state: SessionUiState,
    recentSessions: List<SessionMembership>,
    onCreateSession: (sessionName: String?, displayName: String?) -> Unit,
    onJoinSession: (inviteCode: String, displayName: String?) -> Unit,
    onRejoinSession: (inviteCode: String) -> Unit,
    onSignOut: () -> Unit,
) {
    var sessionName by remember { mutableStateOf("") }
    var createDisplayName by remember { mutableStateOf("") }
    var inviteCode by remember { mutableStateOf("") }
    var joinDisplayName by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(text = "Start or join a run", style = MaterialTheme.typography.headlineSmall)
        Spacer(modifier = Modifier.height(24.dp))

        Text(text = "Create a session", style = MaterialTheme.typography.titleMedium)
        // Server-enforced rule (server/Stores/SessionStore.cs's NormalizeSessionName): 4-8 ASCII
        // letters/digits/dashes/underscores if provided at all. Leaving it blank is valid — the
        // server generates a name — so this only applies once the runner's typed something.
        val isSessionNameValid = sessionName.isBlank() ||
            (sessionName.length in 4..8 &&
                sessionName.all { (it.isLetterOrDigit() && it.code < 128) || it == '-' || it == '_' })
        OutlinedTextField(
            value = sessionName,
            onValueChange = { sessionName = it },
            label = { Text("Session name (optional)") },
            supportingText = { Text("4-8 letters, numbers, dashes, or underscores") },
            isError = !isSessionNameValid,
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = createDisplayName,
            onValueChange = { createDisplayName = it },
            label = { Text("Display name (optional)") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        Button(
            onClick = { onCreateSession(sessionName, createDisplayName) },
            enabled = state !is SessionUiState.Loading && isSessionNameValid,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Create session")
        }

        Spacer(modifier = Modifier.height(24.dp))
        HorizontalDivider()
        Spacer(modifier = Modifier.height(24.dp))

        Text(text = "Join with an invite code", style = MaterialTheme.typography.titleMedium)
        OutlinedTextField(
            value = inviteCode,
            onValueChange = { inviteCode = it },
            label = { Text("Invite code") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedTextField(
            value = joinDisplayName,
            onValueChange = { joinDisplayName = it },
            label = { Text("Display name (optional)") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))
        Button(
            onClick = { onJoinSession(inviteCode, joinDisplayName) },
            enabled = state !is SessionUiState.Loading && inviteCode.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Join as runner")
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
            HorizontalDivider()
            Spacer(modifier = Modifier.height(24.dp))
            RecentSessionsList(
                sessions = recentSessions,
                enabled = state !is SessionUiState.Loading,
                onRejoin = onRejoinSession,
            )
        }

        Spacer(modifier = Modifier.height(24.dp))
        OutlinedButton(onClick = onSignOut) {
            Text("Sign out")
        }
    }
}

@Composable
private fun RecentSessionsList(
    sessions: List<SessionMembership>,
    enabled: Boolean,
    onRejoin: (inviteCode: String) -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth()) {
        Text(text = "Recent sessions", style = MaterialTheme.typography.titleMedium)
        Spacer(modifier = Modifier.height(8.dp))
        sessions.forEachIndexed { index, membership ->
            if (index > 0) {
                Spacer(modifier = Modifier.height(4.dp))
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column {
                    Text(text = membership.sessionName, style = MaterialTheme.typography.bodyMedium)
                    Text(
                        text = "Invite ${membership.inviteCode}",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Button(
                    onClick = { onRejoin(membership.inviteCode) },
                    enabled = enabled,
                ) {
                    Text("Rejoin")
                }
            }
        }
    }
}
