package nz.skelstar.dotwatcher.ui.map

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SmallFloatingActionButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.platform.LocalContext
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
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/** How often the map polls for other runners' positions, matching the web client's cadence
 *  (repo root README.md, "client" section). This is the read side only; posting this device's
 *  own position is [LocationTrackingService]'s job (Milestone 2), independent of this screen's
 *  lifecycle so it survives the screen being locked or the app being backgrounded. */
private const val POLL_INTERVAL_MS = 10_000L

private val EXPIRY_TIME_FORMAT = DateTimeFormatter.ofPattern("h:mm a")

/**
 * Hosts the live map for [membership]'s session: polls everyone's latest positions to feed
 * [MapScreen], and — when [isSharing] — surfaces [LocationTrackingService]'s state (a "Sharing ·
 * expires HH:MM" chip, plus a "Stop sharing" action distinct from leaving the session entirely)
 * without owning the tracking loop itself. Also offers sharing the session's invite link via the
 * system share sheet and leaving the session (with confirmation) — matches iOS's header share
 * button and leave-confirmation alert (ContentView.swift's `headerSection`/`leaveOrDeleteSession`).
 */
@OptIn(ExperimentalMaterial3Api::class) // TopAppBar is experimental in the pinned Material3 version.
@Composable
fun LiveMapScreen(
    membership: SessionMembership,
    repository: DotWatcherRepository,
    isSharing: Boolean,
    onStopSharing: () -> Unit,
    onLeaveSession: () -> Unit,
) {
    val context = LocalContext.current
    val coroutineScope = rememberCoroutineScope()
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    var showLeaveConfirm by remember { mutableStateOf(false) }
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

    if (showLeaveConfirm) {
        LeaveSessionDialog(
            isSharing = isSharing,
            onConfirm = {
                showLeaveConfirm = false
                onLeaveSession()
            },
            onDismiss = { showLeaveConfirm = false },
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(membership.sessionName)
                        if (isSharing) {
                            SharingStatusLine(trackingState)
                        }
                    }
                },
                actions = {
                    TextButton(onClick = { context.shareInviteLink(membership) }) {
                        Text("Share")
                    }
                    if (isSharing) {
                        TextButton(onClick = onStopSharing) {
                            Text("Stop sharing")
                        }
                    }
                },
            )
        },
        floatingActionButton = {
            SmallFloatingActionButton(onClick = { showLeaveConfirm = true }) {
                Text("Leave")
            }
        },
    ) { padding ->
        Box(modifier = Modifier.fillMaxSize().padding(padding)) {
            MapScreen(positions = positions, linzApiKey = BuildConfig.LINZ_API_KEY)

            val errorMessage = if (!isSharing) {
                null
            } else {
                (trackingState as? TrackingState.Tracking)?.lastError?.let {
                    "Couldn't send your position: $it"
                }
            }

            if (errorMessage != null) {
                Text(
                    text = errorMessage,
                    color = MaterialTheme.colorScheme.error,
                    modifier = Modifier
                        .align(Alignment.BottomCenter)
                        .padding(16.dp),
                )
            }
        }
    }
}

/** "Sharing · expires 5:31 PM" while [LocationTrackingService] is actually running, or "Sharing
 *  stopped" if it ended (duration cap reached, or stopped elsewhere) while this screen still
 *  thinks [isSharing][LiveMapScreen] is true — e.g. right after the cap expires, before the
 *  runner has dismissed/left. */
@Composable
private fun SharingStatusLine(trackingState: TrackingState) {
    val text = when (trackingState) {
        is TrackingState.Tracking -> {
            val expiryTime = trackingState.expiresAt.atZone(ZoneId.systemDefault()).format(EXPIRY_TIME_FORMAT)
            "Sharing · expires $expiryTime"
        }
        TrackingState.Stopped -> "Sharing stopped"
    }
    Text(text = text, style = MaterialTheme.typography.labelSmall)
}

/** Matches iOS's leave-confirmation alert (ContentView.swift), including its wording branching
 *  on whether tracking is currently active. */
@Composable
private fun LeaveSessionDialog(
    isSharing: Boolean,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (isSharing) "Stop sharing and leave session?" else "Leave session?") },
        text = { Text("Are you sure? You can rejoin later using the invite code.") },
        confirmButton = {
            TextButton(onClick = onConfirm) {
                Text(if (isSharing) "Stop & Leave" else "Leave")
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text("Cancel")
            }
        },
    )
}

/** Opens the system share sheet with the session's invite link — matches iOS's header
 *  `ShareLink` (ContentView.swift's `headerSection`), which shares the same message format:
 *  invite code plus a `{webBaseURL}/code/{inviteCode}` link the recipient can open directly. */
private fun Context.shareInviteLink(membership: SessionMembership) {
    val sessionUrl = "${BuildConfig.WEB_BASE_URL}/code/${membership.inviteCode}"
    val message = "Join my DotWatcher session!\n\n" +
        "Invite code: ${membership.inviteCode}\n\n" +
        sessionUrl
    val sendIntent = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, message)
    }
    startActivity(Intent.createChooser(sendIntent, "Share session"))
}
