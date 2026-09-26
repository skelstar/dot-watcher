package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.location.LocationTrackingService
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.RunnerPosition
import nz.skelstar.dotwatcher.network.SessionMembership

/** How often the map polls for other runners' positions, matching the web client's cadence
 *  (repo root README.md, "client" section). This is the read side only; posting this device's
 *  own position is [LocationTrackingService]'s job (Milestone 2), independent of this screen's
 *  lifecycle so it survives the screen being locked or the app being backgrounded. */
private const val POLL_INTERVAL_MS = 10_000L

/**
 * Hosts the live map for [membership]'s session: polls everyone's latest positions to feed
 * [MapScreen], and — when [isSharing] — surfaces [LocationTrackingService]'s state (next
 * expiry, last post error) without owning the tracking loop itself.
 */
@OptIn(ExperimentalMaterial3Api::class) // TopAppBar is experimental in the pinned Material3 version.
@Composable
fun LiveMapScreen(
    membership: SessionMembership,
    repository: DotWatcherRepository,
    isSharing: Boolean,
    onLeaveSession: () -> Unit,
) {
    val coroutineScope = rememberCoroutineScope()
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    val trackingState by LocationTrackingService.state.collectAsState()

    // Polls everyone's latest positions, including this device's own once the server has it.
    DisposableEffect(membership.sessionId) {
        val job = coroutineScope.launch {
            while (isActive) {
                runCatching { repository.getLatestPositions(membership.sessionId) }
                    .onSuccess { response ->
                        response.body()?.let { groups ->
                            positions = groups.mapNotNull { it.firstOrNull() }
                        }
                    }
                delay(POLL_INTERVAL_MS)
            }
        }
        onDispose { job.cancel() }
    }

    Scaffold(
        topBar = {
            TopAppBar(title = { Text(membership.sessionName) })
        },
        floatingActionButton = {
            SmallFloatingActionButton(onClick = onLeaveSession) {
                Text("Leave")
            }
        },
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize().padding(padding)) {
            MapScreen(positions = positions, linzApiKey = BuildConfig.LINZ_API_KEY)

            val statusMessage = if (!isSharing) {
                null
            } else {
                when (val current = trackingState) {
                    is TrackingState.Tracking -> current.lastError?.let { "Couldn't send your position: $it" }
                    TrackingState.Stopped -> "Sharing has stopped."
                }
            }

            if (statusMessage != null) {
                Text(
                    text = statusMessage,
                    color = MaterialTheme.colorScheme.error,
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(16.dp),
                )
            }
        }
    }
}
