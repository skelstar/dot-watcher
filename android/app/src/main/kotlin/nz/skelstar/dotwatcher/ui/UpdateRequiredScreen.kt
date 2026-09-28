package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/**
 * Blocking full-screen replacement shown once the server rejects this build's `X-Api-Version`
 * with `426 Upgrade Required` (see network/UpdateRequiredState.kt). Ported from
 * ios/DotWatcher/DotWatcher/UpdateRequiredView.swift; Android has no TestFlight equivalent to
 * deep-link to yet since distribution (Play Console internal testing) is Milestone 4, so this
 * points at the Play Store listing placeholder instead — update the link once that listing
 * exists.
 */
@Composable
fun UpdateRequiredScreen(onOpenPlayStore: () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(text = "Update Required", style = MaterialTheme.typography.headlineMedium)
        Text(
            text = "This version of Dot Watcher is no longer supported. Update the app to continue.",
            style = MaterialTheme.typography.bodyMedium,
            textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = 8.dp, bottom = 24.dp),
        )
        Button(onClick = onOpenPlayStore) {
            Text("Open Play Store")
        }
    }
}
