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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.location.LocationTracker
import nz.skelstar.dotwatcher.network.RunnerPosition
import nz.skelstar.dotwatcher.network.SessionMembership
import java.time.Instant

/** How often the foreground-only tracker captures and posts a position (Milestone 1). Clock-
 *  aligned cadence (see repo root README.md, "Position synchronisation") is Milestone 2's job —
 *  this is a plain fixed-delay timer, which is fine while only one client instance is testing. */
private const val POST_INTERVAL_MS = 15_000L

/** How often the map polls for other runners' positions, matching the web client's cadence
 *  (repo root README.md, "client" section). */
private const val POLL_INTERVAL_MS = 10_000L

/**
 * Hosts the live map for [membership]'s session: posts this device's own position on a timer
 * (foreground only — see LocationTracker's kdoc) and polls everyone's latest positions to feed
 * [MapScreen]. This is Milestone 1's tracking loop; background operation is Milestone 2.
 */
@OptIn(ExperimentalMaterial3Api::class) // TopAppBar is experimental in the pinned Material3 version.
@Composable
fun LiveMapScreen(
    membership: SessionMembership,
    repository: DotWatcherRepository,
    hasLocationPermission: Boolean,
    onLeaveSession: () -> Unit,
) {
    val context = LocalContext.current
    val coroutineScope = rememberCoroutineScope()
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    var trackingError by remember { mutableStateOf<String?>(null) }

    val locationTracker = remember { LocationTracker(context) }

    DisposableEffect(Unit) {
        locationTracker.startHeadingUpdates()
        onDispose { locationTracker.stopHeadingUpdates() }
    }

    // Posts this device's own position on a timer.
    DisposableEffect(membership.sessionId, hasLocationPermission) {
        val job = coroutineScope.launch {
            if (!hasLocationPermission) return@launch
            while (isActive) {
                runCatching {
                    val capture = locationTracker.captureOnce()
                    repository.postLocation(
                        sessionId = membership.sessionId,
                        latitude = capture.latitude,
                        longitude = capture.longitude,
                        heading = capture.heading,
                        timestamp = Instant.now().toString(),
                    )
                }.onFailure { trackingError = it.message }
                delay(POST_INTERVAL_MS)
            }
        }
        onDispose { job.cancel() }
    }

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

            if (!hasLocationPermission) {
                Text(
                    text = "Location permission not granted — your position won't be shared.",
                    color = MaterialTheme.colorScheme.error,
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(16.dp),
                )
            } else if (trackingError != null) {
                Text(
                    text = "Couldn't send your position: $trackingError",
                    color = MaterialTheme.colorScheme.error,
                    modifier = Modifier.align(Alignment.BottomCenter),
                )
            }
        }
    }
}
