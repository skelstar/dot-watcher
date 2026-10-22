package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.AccountInfo
import nz.skelstar.dotwatcher.crew.MAX_NICKNAME_LENGTH
import nz.skelstar.dotwatcher.ui.InitialsDot

/**
 * What the account icon opens when you're signed in: who you are and how your dot looks, with Sign
 * out and Delete account. Tapping the icon used to sign you out on the spot, which dropped you on
 * the sign-in screen with no way back. Mirrors the signed-in half of iOS's `AuthSheet`.
 *
 * Deleting the account is permanent (it also deletes the sessions you own, server-side), so it
 * takes a confirmation, and a failure leaves you signed in with the reason shown.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountSheet(
    account: AccountInfo,
    onSignOut: () -> Unit,
    onDeleteAccount: (onError: (String) -> Unit) -> Unit,
    onDismiss: () -> Unit,
    /** Crew: the name the person's crew sees. Only offered once the server has crew. */
    showNickname: Boolean = false,
    nickname: String? = null,
    nicknameError: String? = null,
    onSaveNickname: (String) -> Unit = {},
    onEditNickname: () -> Unit = {},
) {
    var nicknameDraft by remember(nickname) { mutableStateOf(nickname.orEmpty()) }
    var confirmDelete by remember { mutableStateOf(false) }
    var deleting by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    ModalBottomSheet(onDismissRequest = { if (!deleting) onDismiss() }) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text("Account", style = MaterialTheme.typography.titleLarge)

            Row(verticalAlignment = Alignment.CenterVertically) {
                InitialsDot(initials = account.displayName)
                Spacer(modifier = Modifier.width(16.dp))
                Column {
                    Text(
                        text = account.username ?: "Signed in",
                        style = MaterialTheme.typography.titleMedium,
                    )
                    Text(
                        text = "Other people see these initials on your dot.",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }

            if (showNickname) {
                OutlinedTextField(
                    value = nicknameDraft,
                    onValueChange = {
                        if (it.length <= MAX_NICKNAME_LENGTH) {
                            nicknameDraft = it.replace("\n", "")
                            onEditNickname()
                        }
                    },
                    label = { Text("Name your crew sees") },
                    singleLine = true,
                    isError = nicknameError != null,
                    supportingText = {
                        Text(nicknameError ?: "Shown to people in your crew. Leave blank to show your initials.")
                    },
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedButton(
                    onClick = { onSaveNickname(nicknameDraft) },
                    enabled = !deleting && nicknameDraft.trim() != nickname.orEmpty(),
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Save name") }
            }

            OutlinedButton(
                onClick = onSignOut,
                enabled = !deleting,
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Sign out") }

            if (deleting) {
                CircularProgressIndicator()
            } else {
                TextButton(
                    onClick = { confirmDelete = true },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text("Delete account", color = MaterialTheme.colorScheme.error)
                }
            }

            error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }

    if (confirmDelete) {
        AlertDialog(
            onDismissRequest = { confirmDelete = false },
            title = { Text("Delete your account?") },
            text = {
                Text("This permanently deletes your account and any sessions you own. It can't be undone.")
            },
            confirmButton = {
                TextButton(onClick = {
                    confirmDelete = false
                    deleting = true
                    error = null
                    onDeleteAccount { message ->
                        deleting = false
                        error = "Couldn't delete your account: $message"
                    }
                }) { Text("Delete", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { confirmDelete = false }) { Text("Cancel") } },
        )
    }
}
