package nz.skelstar.dotwatcher.ui.map

import android.content.Context
import android.content.Intent
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
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
import java.time.Duration
import java.time.Instant

/** How often the map polls for other runners' positions, matching the web client's cadence
 *  (repo root README.md, "client" section). This is the read side only; posting this device's
 *  own position is [LocationTrackingService]'s job (Milestone 2), independent of this screen's
 *  lifecycle so it survives the screen being locked or the app being backgrounded. */
private const val POLL_INTERVAL_MS = 10_000L

/**
 * Hosts the live map for [membership]'s session, laid out per the "In-Session Map Redesign v2"
 * artifact: the map fills the screen with floating controls over it — wordmark and auto-stop
 * countdown top-left, a right rail (share, invite code, overflow with Leave) and a round Stop pill
 * bottom-right while [isSharing]. Polls everyone's latest positions to feed [MapScreen] and
 * surfaces [LocationTrackingService]'s state without owning the tracking loop itself.
 */
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
    var showInviteCode by remember { mutableStateOf(false) }
    var showMenu by remember { mutableStateOf(false) }
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

    if (showInviteCode) {
        InviteCodeDialog(
            code = membership.inviteCode,
            onShare = { context.shareInviteLink(membership) },
            onDismiss = { showInviteCode = false },
        )
    }

    Box(modifier = Modifier.fillMaxSize()) {
        MapScreen(
            positions = positions,
            linzApiKey = BuildConfig.LINZ_API_KEY,
            currentUserName = membership.displayName,
        )

        // Top-left: wordmark, then the auto-stop countdown while this device is sharing.
        Row(
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding()
                .padding(start = 14.dp, top = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Wordmark(initials = membership.displayName.initials())
            (trackingState as? TrackingState.Tracking)?.let { AutoStopTimer(it.expiresAt) }
        }

        // Right rail: share, invite code, and an overflow menu holding Leave.
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(end = 14.dp, top = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            RailButton("↗", "Share invite link") { context.shareInviteLink(membership) }
            RailButton("#", "Invite code") { showInviteCode = true }
            Box {
                RailButton("⋯", "More") { showMenu = true }
                DropdownMenu(expanded = showMenu, onDismissRequest = { showMenu = false }) {
                    DropdownMenuItem(
                        text = { Text("Leave session") },
                        onClick = {
                            showMenu = false
                            showLeaveConfirm = true
                        },
                    )
                }
            }
        }

        if (isSharing) {
            StopPill(
                onClick = onStopSharing,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .navigationBarsPadding()
                    .padding(end = 14.dp, bottom = 26.dp),
            )
        }

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
                    .align(Alignment.BottomStart)
                    .navigationBarsPadding()
                    .padding(16.dp),
            )
        }
    }
}

private val GlassColor = Color.White.copy(alpha = 0.82f)
private val StopRed = Color(0xFFFF3B30)
private val AccentBlue = Color(0xFF0A84FF)

/** The display name is the runner's own 2-letter initials (see the auth/name-entry screens), so it
 *  is shown as entered; only a blank name falls back to "?". */
private fun String.initials(): String = trim().ifEmpty { "?" }

/** The dot-watchr wordmark with the runner's initials as the "o", drawn straight on the map. */
@Composable
private fun Wordmark(initials: String) {
    val halo = Shadow(color = Color.White, blurRadius = 8f)
    val textStyle = TextStyle(
        color = Color.Black,
        fontSize = 19.sp,
        fontWeight = FontWeight.ExtraBold,
        letterSpacing = (-0.6).sp,
        shadow = halo,
    )
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text("d", style = textStyle)
        Box(
            modifier = Modifier.padding(horizontal = 1.dp).size(25.dp).clip(CircleShape).background(AccentBlue),
            contentAlignment = Alignment.Center,
        ) {
            Text(initials, color = Color.White, fontSize = 9.5.sp, fontWeight = FontWeight.Bold)
        }
        Text("t-watchr", style = textStyle)
    }
}

/** Circular countdown to the sharing cap. Arc is remaining ÷ the remaining time when first
 *  shown (the service doesn't expose the originally chosen duration); turns amber and flashes in
 *  the last 30 minutes. Tapping to extend is not implemented yet — needs a service change. */
@Composable
private fun AutoStopTimer(expiresAt: Instant) {
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(expiresAt) {
        while (true) {
            now = Instant.now()
            delay(1_000L)
        }
    }
    val total = remember(expiresAt) { Duration.between(Instant.now(), expiresAt).coerceAtLeast(Duration.ofSeconds(1)) }
    val remaining = Duration.between(now, expiresAt).coerceAtLeast(Duration.ZERO)
    val fraction = (remaining.seconds.toFloat() / total.seconds.toFloat()).coerceIn(0f, 1f)
    val low = remaining <= Duration.ofMinutes(30)
    val arcColor = if (low) Color(0xFFE08A00) else Color(0xFF30A14E)

    val flash by rememberInfiniteTransition(label = "flash").animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(1000), RepeatMode.Reverse),
        label = "flash",
    )
    val background = if (low) lerp(GlassColor, Color(0xF2FFD666), flash) else GlassColor

    Box(
        modifier = Modifier.size(46.dp).clip(CircleShape).background(background)
            .semantics { contentDescription = "Auto-stops in ${remaining.toMinutes()} minutes" },
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.fillMaxSize().padding(2.dp)) {
            val stroke = Stroke(width = 3.dp.toPx(), cap = StrokeCap.Round)
            drawArc(Color.Black.copy(alpha = 0.08f), 0f, 360f, false, style = stroke)
            drawArc(arcColor, -90f, 360f * fraction, false, style = stroke)
        }
        Text(
            text = "%d:%02d".format(remaining.toHours(), remaining.toMinutesPart()),
            fontSize = 11.sp,
            fontWeight = FontWeight.Bold,
            fontFamily = FontFamily.Monospace,
            color = if (low) Color(0xFFA35F00) else Color(0xFF14181F),
        )
    }
}

@Composable
private fun RailButton(glyph: String, description: String, onClick: () -> Unit) {
    Box(
        modifier = Modifier.size(44.dp).clip(CircleShape).background(GlassColor)
            .clickable(onClick = onClick)
            .semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        Text(glyph, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, color = Color(0xFF14181F))
    }
}

@Composable
private fun StopPill(onClick: () -> Unit, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier.height(48.dp).clip(RoundedCornerShape(24.dp)).background(StopRed)
            .clickable(onClick = onClick).padding(horizontal = 20.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Box(Modifier.size(11.dp).clip(RoundedCornerShape(3.dp)).background(Color.White))
        Text("Stop", color = Color.White, fontSize = 15.sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
private fun InviteCodeDialog(code: String, onShare: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Invite code") },
        text = {
            Text(
                text = code,
                fontFamily = FontFamily.Monospace,
                fontSize = 26.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 3.sp,
            )
        },
        confirmButton = { TextButton(onClick = onShare) { Text("Share invite link") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Close") } },
    )
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
 *  `ShareLink` (`InviteLink.shareMessage` in InviteLink.swift), which shares the same message format:
 *  a `{webBaseURL}/join/{inviteCode}` link (opens the iOS app, or a web page with the code and an
 *  App Store link elsewhere), the invite code, and a `/code/{inviteCode}` link for watching on the web. */
private fun Context.shareInviteLink(membership: SessionMembership) {
    val code = membership.inviteCode
    val message = "Join my DotWatcher session!\n\n" +
        "Tap to join in the app:\n${BuildConfig.WEB_BASE_URL}/join/$code\n\n" +
        "Invite code: $code\n\n" +
        "Just want to watch?\n${BuildConfig.WEB_BASE_URL}/code/$code"
    val sendIntent = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, message)
    }
    startActivity(Intent.createChooser(sendIntent, "Share session"))
}
