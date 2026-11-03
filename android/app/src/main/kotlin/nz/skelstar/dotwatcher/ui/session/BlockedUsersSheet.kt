package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.blocks.BlocksModel
import nz.skelstar.dotwatcher.network.BlockedUser
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

/**
 * Who this person has blocked, with Unblock. Mirrors iOS's `BlockedUsersView`. Unblocking doesn't
 * restore any session the block ended, so they would need a new invite to rejoin.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BlockedUsersSheet(blocks: BlocksModel, onDismiss: () -> Unit) {
    var users by remember { mutableStateOf<List<BlockedUser>?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var unblockCandidate by remember { mutableStateOf<BlockedUser?>(null) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        blocks.load().fold(
            onSuccess = { users = it },
            onFailure = { error = it.message },
        )
    }

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("Blocked users", style = MaterialTheme.typography.titleLarge)

            val list = users
            when {
                list == null && error == null -> CircularProgressIndicator()
                list != null && list.isEmpty() -> Text(
                    "You haven't blocked anyone.",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                list != null -> list.forEach { blocked ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(modifier = Modifier.weight(1f)) {
                            Text(blocked.displayName, style = MaterialTheme.typography.titleMedium)
                            Text(
                                "Blocked ${formatDate(blocked.blockedAt)}",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        TextButton(onClick = { unblockCandidate = blocked }) { Text("Unblock") }
                    }
                }
            }

            error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            }

            Text(
                "Blocked users can't see your location, join sessions you own, or appear in sessions you're both a member of.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }

    unblockCandidate?.let { blocked ->
        AlertDialog(
            onDismissRequest = { unblockCandidate = null },
            title = { Text("Unblock ${blocked.displayName}?") },
            text = {
                Text("This doesn't restore any session they were removed from — they'd need a new invite to rejoin.")
            },
            confirmButton = {
                TextButton(onClick = {
                    unblockCandidate = null
                    error = null
                    scope.launch {
                        blocks.unblock(blocked.userId).fold(
                            onSuccess = { users = users?.filterNot { it.userId == blocked.userId } },
                            onFailure = { error = it.message },
                        )
                    }
                }) { Text("Unblock") }
            },
            dismissButton = { TextButton(onClick = { unblockCandidate = null }) { Text("Cancel") } },
        )
    }
}

private fun formatDate(iso: String): String =
    runCatching {
        DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).format(OffsetDateTime.parse(iso))
    }.getOrDefault(iso)
