package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.AccountInfo
import nz.skelstar.dotwatcher.blocks.BlocksModel
import nz.skelstar.dotwatcher.crew.MAX_NICKNAME_LENGTH
import nz.skelstar.dotwatcher.ui.InitialsDot
import nz.skelstar.dotwatcher.ui.theme.AppearanceMode

private const val INITIALS_LENGTH = 2
private const val PRIVACY_URL = "https://dot-watcher.skelstar.io/privacy"
private const val TERMS_URL = "https://dot-watcher.skelstar.io/terms"

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
    onSaveInitials: (String) -> Unit,
    blocks: BlocksModel,
    appearance: AppearanceMode,
    onAppearanceChange: (AppearanceMode) -> Unit,
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
    var showBlocked by remember { mutableStateOf(false) }
    val uriHandler = LocalUriHandler.current
    var savedInitials by remember(account.displayName) { mutableStateOf(account.displayName) }
    var initialsDraft by remember(account.displayName) { mutableStateOf(account.displayName) }

    ModalBottomSheet(onDismissRequest = { if (!deleting) onDismiss() }) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text("Settings", style = MaterialTheme.typography.titleLarge)

            Row(verticalAlignment = Alignment.CenterVertically) {
                InitialsDot(initials = savedInitials)
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

            OutlinedTextField(
                value = initialsDraft,
                onValueChange = { input ->
                    initialsDraft = input.filter { it.isLetter() }.take(INITIALS_LENGTH).uppercase()
                },
                label = { Text("Initials") },
                singleLine = true,
                supportingText = { Text("Sent when you start or join a session. Signing in again resets them.") },
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedButton(
                onClick = {
                    onSaveInitials(initialsDraft)
                    savedInitials = initialsDraft
                },
                enabled = !deleting && initialsDraft.length == INITIALS_LENGTH && initialsDraft != savedInitials,
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Save initials") }

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

            Text("Appearance", style = MaterialTheme.typography.titleSmall)
            SingleChoiceSegmentedButtonRow(modifier = Modifier.fillMaxWidth()) {
                val modes = AppearanceMode.entries
                modes.forEachIndexed { index, mode ->
                    SegmentedButton(
                        selected = mode == appearance,
                        onClick = { onAppearanceChange(mode) },
                        shape = SegmentedButtonDefaults.itemShape(index, modes.size),
                    ) { Text(mode.label) }
                }
            }

            OutlinedButton(
                onClick = { showBlocked = true },
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Blocked users") }

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

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { uriHandler.openUri(PRIVACY_URL) }) { Text("Privacy Policy") }
                TextButton(onClick = { uriHandler.openUri(TERMS_URL) }) { Text("Terms of Use") }
            }
            Text(
                "Location data can be delayed, inaccurate, or unavailable.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }

    if (showBlocked) {
        BlockedUsersSheet(blocks = blocks, onDismiss = { showBlocked = false })
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
