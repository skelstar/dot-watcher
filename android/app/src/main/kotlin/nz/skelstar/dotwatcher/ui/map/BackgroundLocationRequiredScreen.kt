package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/**
 * Shown in place of [ShareLocationConsentScreen] when location isn't set to "Allow all the time".
 * Sharing needs that level because the runner's screen is off or the app is backgrounded for most
 * of a run — "only while using the app" would silently stop posting positions. Android won't
 * re-show its permission dialog after a denial (and on 11+ never offers "all the time" in-dialog),
 * so the only route is the app's settings page; MainActivity re-checks on resume so the real
 * consent screen appears as soon as the runner returns.
 */
@Composable
fun BackgroundLocationRequiredScreen(
    sessionName: String,
    onOpenSettings: () -> Unit,
    onViewOnly: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(
            text = "Allow location \"all the time\"",
            style = MaterialTheme.typography.headlineSmall,
            textAlign = TextAlign.Center,
        )
        Spacer(modifier = Modifier.height(8.dp))
        Text(
            text = "To share your position with \"$sessionName\", Dot Watcher needs location " +
                "access set to \"Allow all the time\". Without it, your position stops being " +
                "shared as soon as your screen locks or you switch apps — which is most of a run.",
            style = MaterialTheme.typography.bodyMedium,
            textAlign = TextAlign.Center,
        )
        Spacer(modifier = Modifier.height(8.dp))
        Text(
            text = "In Settings, tap Permissions → Location, then choose \"Allow all the time\".",
            style = MaterialTheme.typography.bodyMedium,
            textAlign = TextAlign.Center,
        )

        Spacer(modifier = Modifier.height(24.dp))

        Button(onClick = onOpenSettings, modifier = Modifier.fillMaxWidth()) {
            Text("Open settings")
        }
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedButton(onClick = onViewOnly, modifier = Modifier.fillMaxWidth()) {
            Text("Just watch, don't share")
        }
    }
}
