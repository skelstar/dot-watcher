package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
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

/**
 * Lets a signed-in runner either start a new session ([POST /sessions]) or join an existing
 * one by invite code ([POST /session-invites/{inviteCode}/join] with role "runner" — repo root
 * README.md, "Auth model"). Milestone 1 keeps this to the single essential path; the fuller
 * "list of my sessions" UI is Milestone 3 (.ai/plans/android-app.md).
 */
@Composable
fun SessionScreen(
    state: SessionUiState,
    onCreateSession: (sessionName: String?, displayName: String?) -> Unit,
    onJoinSession: (inviteCode: String, displayName: String?) -> Unit,
    onSignOut: () -> Unit,
) {
    var sessionName by remember { mutableStateOf("") }
    var createDisplayName by remember { mutableStateOf("") }
    var inviteCode by remember { mutableStateOf("") }
    var joinDisplayName by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(text = "Start or join a run", style = MaterialTheme.typography.headlineSmall)
        Spacer(modifier = Modifier.height(24.dp))

        Text(text = "Create a session", style = MaterialTheme.typography.titleMedium)
        OutlinedTextField(
            value = sessionName,
            onValueChange = { sessionName = it },
            label = { Text("Session name (optional)") },
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
            enabled = state !is SessionUiState.Loading,
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

        Spacer(modifier = Modifier.height(24.dp))
        OutlinedButton(onClick = onSignOut) {
            Text("Sign out")
        }
    }
}
