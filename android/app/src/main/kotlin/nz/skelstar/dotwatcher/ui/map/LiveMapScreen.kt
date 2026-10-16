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
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.scale
import androidx.compose.ui.graphics.vector.PathParser
import androidx.core.net.toUri
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
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.location.LocationTrackingService
import nz.skelstar.dotwatcher.location.POST_INTERVAL_SECONDS
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
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    var showLeaveConfirm by remember { mutableStateOf(false) }
    var showInviteCode by remember { mutableStateOf(false) }
    var showMenu by remember { mutableStateOf(false) }
    var showExtendSheet by remember { mutableStateOf(false) }
    // Fit-all is lit by default; following a runner or panning away turns it off.
    var fitAll by remember { mutableStateOf(true) }
    var followedRunner by remember { mutableStateOf<String?>(null) }
    var fitAllTick by remember { mutableStateOf(0) }
    val trackingState by LocationTrackingService.state.collectAsState()
    val pausedForBattery = isSharing && (trackingState as? TrackingState.Tracking)?.pausedForBattery == true

    // Polls everyone's latest positions, including this device's own once the server has it.
    // Only while the app is visible (STARTED): with the screen locked or the app backgrounded
    // nobody is looking at the map, so the radio and CPU are left alone — the foreground service
    // keeps sending this device's own position regardless. Resuming restarts the loop, which
    // fetches immediately.
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(membership.sessionId, lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
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

    (trackingState as? TrackingState.Tracking)?.takeIf { showExtendSheet }?.let { tracking ->
        ExtendSessionSheet(
            expiresAt = tracking.expiresAt,
            onExtend = { LocationTrackingService.extend(context, it) },
            onStopNow = {
                showExtendSheet = false
                onStopSharing()
            },
            onDismiss = { showExtendSheet = false },
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
            followedRunner = followedRunner,
            fitAll = fitAll,
            fitAllTick = fitAllTick,
            sendIntervalSeconds = if (isSharing) POST_INTERVAL_SECONDS else null,
            ownPaused = pausedForBattery,
            onRunnerTapped = { name ->
                // Tapping the runner you're following lets go again.
                followedRunner = if (name == followedRunner) null else name
                fitAll = false
            },
            onUserMovedCamera = {
                followedRunner = null
                fitAll = false
            },
        )

        // Top-left: wordmark, then the auto-stop countdown while this device is sharing; in the
        // last 30 minutes a pill underneath spells out the time left.
        Column(
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding()
                .padding(start = 14.dp, top = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Wordmark(initials = membership.displayName.initials())
                (trackingState as? TrackingState.Tracking)?.let { AutoStopTimer(it) { showExtendSheet = true } }
            }
            (trackingState as? TrackingState.Tracking)?.let { EndingSoonPill(it.expiresAt) }
        }

        // Right rail: share, fit all, web map, invite code, and an overflow menu holding Leave.
        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(end = 14.dp, top = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            RailButton(RailIcon.Share, "Share invite link") { context.shareInviteLink(membership) }
            RailButton(RailIcon.Fit, "Fit all runners", active = fitAll) {
                followedRunner = null
                fitAll = true
                fitAllTick++
            }
            RailButton(RailIcon.Globe, "Open web map") { context.openWebMap(membership) }
            RailButton(RailIcon.Hash, "Invite code") { showInviteCode = true }
            Box {
                RailButton(RailIcon.More, "More") { showMenu = true }
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

        // While paused for battery, the card below carries its own Stop and takes the bottom edge.
        if (isSharing && !pausedForBattery) {
            StopPill(
                onClick = onStopSharing,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .navigationBarsPadding()
                    .padding(end = 14.dp, bottom = 26.dp),
            )
        }

        val lastError = (trackingState as? TrackingState.Tracking)?.lastError
        if (pausedForBattery) {
            BatteryPausedCard(
                lastError = lastError,
                onSendNow = { LocationTrackingService.sendNow(context) },
                onAuto = { LocationTrackingService.resumeAutomatic(context) },
                onStop = onStopSharing,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .navigationBarsPadding()
                    .padding(start = 12.dp, end = 12.dp, bottom = 16.dp),
            )
        } else if (isSharing && lastError != null) {
            Text(
                text = "Couldn't send your position: $lastError",
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

private fun String.initials(): String = initialsFor(this)

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

/** Circular countdown to the sharing cap: arc is remaining ÷ the session's total (original
 *  choice plus extensions); turns amber and flashes in the last 30 minutes. Tap to extend. */
@Composable
private fun AutoStopTimer(tracking: TrackingState.Tracking, onClick: () -> Unit) {
    val expiresAt = tracking.expiresAt
    val now by rememberSecondClock()
    val total = tracking.totalDuration.coerceAtLeast(Duration.ofSeconds(1))
    val remaining = Duration.between(now, expiresAt).coerceAtLeast(Duration.ZERO)
    val fraction = (remaining.seconds.toFloat() / total.seconds.toFloat()).coerceIn(0f, 1f)
    val low = remaining <= ENDING_SOON
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
            .clickable(onClick = onClick)
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

/** Rail glyphs: the 24×24 stroke paths from the redesign mock, so they match it exactly. */
private enum class RailIcon(val paths: List<String>) {
    Share(listOf("M12 15V3M8 7l4-4 4 4M6 11H5a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-8a1 1 0 0 0-1-1h-1")),
    Fit(listOf("M4 9V5a1 1 0 0 1 1-1h4M15 4h4a1 1 0 0 1 1 1v4M20 15v4a1 1 0 0 1-1 1h-4M9 20H5a1 1 0 0 1-1-1v-4")),
    Globe(listOf("M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18M3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18")),
    Hash(listOf("M5 9h14M5 15h14M10 4L8 20M16 4l-2 16")),
    More(listOf("M5 12h.01M12 12h.01M19 12h.01")),
    Battery(listOf("M4 7h13a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2zM22 11v2M6 10v4")),
}

/** Placeholder durations from the redesign mock — to be confirmed. */
private val EXTEND_OPTIONS = listOf(
    "+30 min" to Duration.ofMinutes(30),
    "+1 hr" to Duration.ofHours(1),
    "+2 hr" to Duration.ofHours(2),
)

/** Opened by tapping the countdown: add time to the current auto-stop (the sheet stays open and
 *  its "Auto-stops in" line updates), or stop now. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ExtendSessionSheet(
    expiresAt: Instant,
    onExtend: (Duration) -> Unit,
    onStopNow: () -> Unit,
    onDismiss: () -> Unit,
) {
    val now by rememberSecondClock()
    val remaining = Duration.between(now, expiresAt).coerceAtLeast(Duration.ZERO)
    val label = if (remaining.toHours() >= 1) {
        "Auto-stops in ${remaining.toHours()} hr ${remaining.toMinutesPart()} min"
    } else {
        "Auto-stops in ${(remaining.seconds + 59) / 60} min"
    }
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier.padding(start = 18.dp, end = 18.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(label, style = MaterialTheme.typography.labelLarge)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                EXTEND_OPTIONS.forEach { (text, duration) ->
                    OutlinedButton(onClick = { onExtend(duration) }, modifier = Modifier.weight(1f)) {
                        Text(text)
                    }
                }
            }
            Row(
                modifier = Modifier.fillMaxWidth().height(44.dp).clip(RoundedCornerShape(22.dp))
                    .background(StopRed).clickable(onClick = onStopNow),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center,
            ) {
                Box(Modifier.size(10.dp).clip(RoundedCornerShape(3.dp)).background(Color.White))
                Spacer(Modifier.width(8.dp))
                Text("Stop now", color = Color.White, fontSize = 14.sp, fontWeight = FontWeight.Bold)
            }
        }
    }
}

/** How long before the auto-stop the countdown starts flashing and [EndingSoonPill] appears. */
private val ENDING_SOON = Duration.ofMinutes(30)

/** "Auto-stops in 8 min" — shown only inside the last [ENDING_SOON] of a sharing session, next to
 *  the flashing countdown, so the amber circle has words behind it. */
@Composable
private fun EndingSoonPill(expiresAt: Instant) {
    val now by rememberSecondClock()
    val remaining = Duration.between(now, expiresAt)
    if (remaining > ENDING_SOON || remaining.isNegative || remaining.isZero) return

    val minutes = (remaining.seconds + 59) / 60 // round up: "1 min" until it actually stops
    Box(
        modifier = Modifier.height(36.dp).clip(CircleShape).background(Color(0xF0FFF4D6))
            .padding(horizontal = 13.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = "Auto-stops in $minutes min",
            color = Color(0xFF7A4D00),
            fontSize = 12.5.sp,
            fontWeight = FontWeight.SemiBold,
        )
    }
}

/** [icon] drawn as strokes in [tint] on a 24-unit grid, at [size]. */
@Composable
private fun PathIcon(icon: RailIcon, tint: Color, size: androidx.compose.ui.unit.Dp) {
    val paths = remember(icon) { icon.paths.map { PathParser().parsePathString(it).toPath() } }
    Canvas(Modifier.size(size)) {
        scale(this.size.width / 24f, pivot = androidx.compose.ui.geometry.Offset.Zero) {
            // "More" is three dots: a zero-length round-capped segment draws as a dot, wider stroke.
            val width = if (icon == RailIcon.More) 3.2f else 2f
            paths.forEach {
                drawPath(it, tint, style = Stroke(width = width, cap = StrokeCap.Round, join = StrokeJoin.Round))
            }
        }
    }
}

@Composable
private fun RailButton(icon: RailIcon, description: String, active: Boolean = false, onClick: () -> Unit) {
    Box(
        modifier = Modifier.size(44.dp).clip(CircleShape).background(if (active) AccentBlue else GlassColor)
            .clickable(onClick = onClick)
            .semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        PathIcon(icon, if (active) Color.White else Color(0xFF14181F), 22.dp)
    }
}

/** Shown while automatic tracking is paused for a critical battery. Needs a decision, so it takes
 *  the bottom edge with its own Stop (the redesign's rule for anything covering the Stop pill).
 *  "Send now" posts one position without resuming; "Auto" overrides the pause. */
@Composable
private fun BatteryPausedCard(
    lastError: String?,
    onSendNow: () -> Unit,
    onAuto: () -> Unit,
    onStop: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val danger = Color(0xFFC4271D)
    Column(
        modifier = modifier.fillMaxWidth().clip(RoundedCornerShape(22.dp)).background(Color(0xF5FFF0EE))
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(7.dp)) {
            PathIcon(RailIcon.Battery, danger, 18.dp)
            Text(
                "Battery critical, updates paused",
                color = danger,
                fontSize = 13.5.sp,
                fontWeight = FontWeight.Bold,
            )
        }
        Text(
            "Others can't see you move until you send manually, plug in, or turn automatic updates back on.",
            color = Color(0xFF5D6675),
            fontSize = 11.5.sp,
        )
        if (lastError != null) {
            Text("Couldn't send: $lastError", color = danger, fontSize = 11.5.sp)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = onSendNow, modifier = Modifier.weight(1f)) { Text("Send now") }
            OutlinedButton(onClick = onAuto, modifier = Modifier.weight(1f)) { Text("Auto") }
        }
        Row(
            modifier = Modifier.fillMaxWidth().height(40.dp).clip(RoundedCornerShape(20.dp))
                .background(StopRed).clickable(onClick = onStop),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center,
        ) {
            Box(Modifier.size(10.dp).clip(RoundedCornerShape(3.dp)).background(Color.White))
            Spacer(Modifier.width(8.dp))
            Text("Stop", color = Color.White, fontSize = 14.sp, fontWeight = FontWeight.Bold)
        }
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

/** Opens the session's web viewer (`/code/{inviteCode}`) in the browser. */
private fun Context.openWebMap(membership: SessionMembership) {
    val url = "${BuildConfig.WEB_BASE_URL}/code/${membership.inviteCode}"
    startActivity(Intent(Intent.ACTION_VIEW, url.toUri()))
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
