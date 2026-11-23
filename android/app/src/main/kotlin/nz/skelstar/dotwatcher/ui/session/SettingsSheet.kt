package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.location.CrewAlertSettings
import nz.skelstar.dotwatcher.ui.theme.AppearanceMode

internal const val PRIVACY_URL = "https://dot-watcher.skelstar.io/privacy"
internal const val TERMS_URL = "https://dot-watcher.skelstar.io/terms"
internal const val LOCATION_DISCLAIMER = "Location data can be delayed, inaccurate, or unavailable."

/**
 * App preferences, opened from the gear in the start screen's header and the gear on the map rail.
 * Who you are (initials, crew name, blocked users, sign out) lives in [AccountSheet]. Mirrors iOS's
 * `SettingsSheet`, which also has dot size and satellite alerts; Android gains those here when it
 * gets them. Crew alerts are here already.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsSheet(
    /** Light only for now, as on iOS: the Appearance picker is hidden until dark mode is ready. */
    @Suppress("UNUSED_PARAMETER") appearance: AppearanceMode,
    @Suppress("UNUSED_PARAMETER") onAppearanceChange: (AppearanceMode) -> Unit,
    onDismiss: () -> Unit,
) {
    val uriHandler = LocalUriHandler.current
    val context = LocalContext.current
    var crewAlerts by remember { mutableStateOf(CrewAlertSettings.isEnabled(context)) }

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier
                .fillMaxWidth()
                .verticalScroll(rememberScrollState())
                .padding(start = 24.dp, end = 24.dp, bottom = 32.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Text("Settings", style = MaterialTheme.typography.titleLarge)

            Text(
                "Notifications",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .toggleable(
                        value = crewAlerts,
                        role = Role.Switch,
                        onValueChange = {
                            crewAlerts = it
                            CrewAlertSettings.setEnabled(context, it)
                        },
                    ),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("Crew alerts", style = MaterialTheme.typography.bodyLarge)
                // The row is the toggle, so the switch itself takes no clicks (one target for TalkBack).
                Switch(checked = crewAlerts, onCheckedChange = null)
            }
            Text(
                "Tells you when someone in your crew starts a Go run while you're on one.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )

            Text(
                "About",
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                TextButton(onClick = { uriHandler.openUri(PRIVACY_URL) }) { Text("Privacy Policy") }
                TextButton(onClick = { uriHandler.openUri(TERMS_URL) }) { Text("Terms of Use") }
            }
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("Version", style = MaterialTheme.typography.bodyLarge)
                Text(
                    "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})",
                    style = MaterialTheme.typography.bodyLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Text(
                LOCATION_DISCLAIMER,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
