package nz.skelstar.dotwatcher.ui.casual

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.net.toUri

/** What the share sheet is showing. */
sealed interface WatchLinkState {
    data object Creating : WatchLinkState
    data class Ready(val url: String) : WatchLinkState
    data class Failed(val message: String) : WatchLinkState
}

/**
 * "Share your run": a secret link to a web map of just this person, for someone with no app or account.
 * It can be sent through the system share sheet, opened in the browser, or copied. The link stops
 * working when the run ends, and "Turn off links" ends it sooner.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShareRunSheet(
    state: WatchLinkState,
    onRetry: () -> Unit,
    onTurnOff: () -> Unit,
    onDismiss: () -> Unit,
) {
    val context = LocalContext.current

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Share your run", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Text(
                text = "Send this link to someone who isn't in the app. They watch your position on a web map. " +
                    "No app and no account.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            when (state) {
                WatchLinkState.Creating -> Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.Center,
                ) { CircularProgressIndicator(modifier = Modifier.size(28.dp)) }

                is WatchLinkState.Failed -> {
                    Text(state.message, color = MaterialTheme.colorScheme.error)
                    OutlinedButton(onClick = onRetry, modifier = Modifier.fillMaxWidth()) { Text("Try again") }
                }

                is WatchLinkState.Ready -> {
                    Text(
                        text = state.url,
                        fontFamily = FontFamily.Monospace,
                        style = MaterialTheme.typography.bodyMedium,
                        modifier = Modifier.fillMaxWidth(),
                    )
                    Button(
                        onClick = { context.shareWatchLink(state.url) },
                        colors = androidx.compose.material3.ButtonDefaults.buttonColors(containerColor = GoGreen),
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Share link", fontWeight = FontWeight.Bold) }
                    OutlinedButton(
                        onClick = { context.openInBrowser(state.url) },
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Open in browser") }
                    OutlinedButton(
                        onClick = { context.copyToClipboard(state.url) },
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text("Copy link") }
                }
            }
            Point("Works until you stop", "The link goes dead when your run ends. A new run gets a new link.")
            Point("Shows only you", "Not your crew, and not anyone nearby.")
            if (state is WatchLinkState.Ready) {
                TextButton(onClick = onTurnOff, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                    Text("Turn off links", color = MaterialTheme.colorScheme.error)
                }
            }
        }
    }
}

@Composable
private fun Point(title: String, body: String) {
    Column {
        Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
        Text(body, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

private fun Context.shareWatchLink(url: String) {
    val send = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, "Watch my run on Dot Watcher: $url")
    }
    startActivity(Intent.createChooser(send, "Share your run"))
}

private fun Context.openInBrowser(url: String) {
    startActivity(Intent(Intent.ACTION_VIEW, url.toUri()))
}

private fun Context.copyToClipboard(url: String) {
    val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
    clipboard.setPrimaryClip(ClipData.newPlainText("Dot Watcher link", url))
}
