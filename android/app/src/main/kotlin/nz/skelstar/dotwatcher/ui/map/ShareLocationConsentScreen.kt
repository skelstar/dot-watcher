package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.location.TRACKING_DURATION_OPTIONS
import java.time.Duration

/**
 * Explicit, declinable consent to share location with other members of this session — distinct
 * from the OS location permission prompt, asked once per session start. Ported from
 * ios/DotWatcher/DotWatcher/ShareLocationConsentView.swift, including its duration picker:
 * sharing always auto-stops on its own (see [TRACKING_DURATION_OPTIONS]'s kdoc), the runner just
 * picks how soon, up to 24 hours. Declining still allows viewing the session as a spectator —
 * only automatic sharing of the runner's own position is what's being consented to here.
 */
@Composable
fun ShareLocationConsentScreen(
    sessionName: String,
    onShare: (Duration) -> Unit,
    onDecline: () -> Unit,
) {
    var selectedDuration by remember { mutableStateOf(TRACKING_DURATION_OPTIONS.last()) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(32.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(text = "Share your location?", style = MaterialTheme.typography.headlineSmall)
        Spacer(modifier = Modifier.height(8.dp))
        Text(
            text = "Other members of \"$sessionName\" will be able to see your live position " +
                "on the map while tracking is active. You can stop at any time.",
            style = MaterialTheme.typography.bodyMedium,
            textAlign = TextAlign.Center,
        )

        Spacer(modifier = Modifier.height(24.dp))
        Text(
            text = "SHARE FOR",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(modifier = Modifier.height(8.dp))

        Column(modifier = Modifier.selectableGroup().fillMaxWidth()) {
            TRACKING_DURATION_OPTIONS.forEach { duration ->
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .selectable(
                            selected = duration == selectedDuration,
                            onClick = { selectedDuration = duration },
                            role = Role.RadioButton,
                        )
                        .padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    RadioButton(selected = duration == selectedDuration, onClick = null)
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(text = label(duration))
                }
            }
        }
        Text(
            text = "Sharing always stops on its own — this just picks how soon, up to 24 hours.",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        Spacer(modifier = Modifier.height(24.dp))

        Button(
            onClick = { onShare(selectedDuration) },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Share my location")
        }
        Spacer(modifier = Modifier.height(8.dp))
        OutlinedButton(
            onClick = onDecline,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text("Don't share")
        }

        Spacer(modifier = Modifier.height(16.dp))
        Text(
            text = "You can still view this session as a spectator if you don't share.",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

private fun label(duration: Duration): String = "${duration.toHours()}h"
