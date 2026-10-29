package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.casual.CasualModel
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.location.LocationTrackingService
import nz.skelstar.dotwatcher.location.POST_INTERVAL_SECONDS
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.NearbyDot
import nz.skelstar.dotwatcher.network.RunnerPosition
import nz.skelstar.dotwatcher.ui.casual.HoldToStopPill
import nz.skelstar.dotwatcher.ui.casual.ShareRunSheet
import nz.skelstar.dotwatcher.ui.casual.WatchLinkState
import nz.skelstar.dotwatcher.ui.casual.WhoCanSeeMeSheet
import nz.skelstar.dotwatcher.ui.crew.CrewFlow
import nz.skelstar.dotwatcher.ui.crew.CrewStage

private const val POLL_INTERVAL_MS = 10_000L

/**
 * The map while out casually, laid out like [LiveMapScreen]: wordmark and auto-stop countdown
 * top-left, a right rail, a Stop bottom-right. There is no session behind it, so no invite or web
 * map buttons; instead an eye opens "Who can see me", and a chip at the bottom left counts crew and
 * people nearby. Polls `GET /casual/live`, which returns this person, their crew by name, and (only
 * when both have chosen to be seen) anonymous dots within 5 km. Stopping is a press-and-hold.
 */
@Composable
fun CasualMapScreen(
    repository: DotWatcherRepository,
    casual: CasualModel,
    crew: CrewModel,
    onStop: () -> Unit,
) {
    val context = LocalContext.current
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    var locked by remember { mutableStateOf<Set<String>>(emptySet()) }
    var nearby by remember { mutableStateOf<List<NearbyDot>>(emptyList()) }
    var crewOut by remember { mutableStateOf(0) }
    var showExtendSheet by remember { mutableStateOf(false) }
    var showWhoCanSeeMe by remember { mutableStateOf(false) }
    var showShare by remember { mutableStateOf(false) }
    var crewStage by remember { mutableStateOf(CrewStage.None) }
    val crewState by crew.state.collectAsState()
    // The link made for this run, kept while the screen is up so reopening the sheet doesn't make another.
    var watchLink by remember { mutableStateOf<WatchLinkState>(WatchLinkState.Creating) }
    val scope = rememberCoroutineScope()
    var fitAll by remember { mutableStateOf(true) }
    var followedRunner by remember { mutableStateOf<String?>(null) }
    var fitAllTick by remember { mutableStateOf(0) }
    val trackingState by LocationTrackingService.state.collectAsState()
    val casualState by casual.state.collectAsState()
    val tracking = trackingState as? TrackingState.Tracking
    val pausedForBattery = tracking?.pausedForBattery == true
    val displayName = repository.displayName.orEmpty()

    // Only while the app is visible: the foreground service keeps sending this device's position
    // regardless, and nobody is looking at the map with the screen locked.
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (isActive) {
                runCatching { repository.casualLive() }
                    .onSuccess { response ->
                        response.body()?.let { live ->
                            val everyone = live.positions.mapNotNull { it.firstOrNull() }
                            positions = everyone.map {
                                RunnerPosition(
                                    runnerName = it.runnerName,
                                    latitude = it.latitude,
                                    longitude = it.longitude,
                                    heading = it.heading,
                                    timestamp = it.timestamp,
                                    isUltraConstrained = it.isUltraConstrained,
                                    displayName = it.displayName,
                                )
                            }
                            locked = everyone.filter { it.inSession }.map { it.runnerName }.toSet()
                            crewOut = everyone.count { !it.isSelf }
                            nearby = live.nearby
                        }
                    }
                delay(POLL_INTERVAL_MS)
            }
        }
    }

    fun makeWatchLink() {
        watchLink = WatchLinkState.Creating
        scope.launch {
            watchLink = casual.createWatchLink(BuildConfig.WEB_BASE_URL).fold(
                onSuccess = { WatchLinkState.Ready(it) },
                onFailure = { WatchLinkState.Failed(it.message ?: "Couldn't make a link.") },
            )
        }
    }

    if (showShare) {
        ShareRunSheet(
            state = watchLink,
            onRetry = ::makeWatchLink,
            onTurnOff = {
                scope.launch {
                    casual.turnOffWatchLinks()
                    // A fresh link is made the next time the sheet opens.
                    watchLink = WatchLinkState.Creating
                    showShare = false
                }
            },
            onDismiss = { showShare = false },
        )
    }

    CrewFlow(crew = crew, state = crewState, stage = crewStage, onStage = { crewStage = it })

    tracking?.takeIf { showExtendSheet }?.let { running ->
        ExtendSessionSheet(
            expiresAt = running.expiresAt,
            onExtend = { LocationTrackingService.extend(context, it) },
            onStopNow = {
                showExtendSheet = false
                onStop()
            },
            onDismiss = { showExtendSheet = false },
        )
    }

    if (showWhoCanSeeMe) {
        WhoCanSeeMeSheet(
            visibleToNonCrew = casualState.visibleToNonCrew,
            error = casualState.error,
            onVisibleChange = casual::setVisibleToNonCrew,
            onDismiss = {
                showWhoCanSeeMe = false
                casual.clearError()
            },
        )
    }

    Box(modifier = Modifier.fillMaxSize()) {
        MapScreen(
            positions = positions,
            linzApiKey = BuildConfig.LINZ_API_KEY,
            currentUserName = displayName,
            followedRunner = followedRunner,
            fitAll = fitAll,
            fitAllTick = fitAllTick,
            sendIntervalSeconds = POST_INTERVAL_SECONDS,
            ownPaused = pausedForBattery,
            nearby = nearby,
            lockedRunners = locked,
            onRunnerTapped = { name ->
                followedRunner = if (name == followedRunner) null else name
                fitAll = false
            },
            onUserMovedCamera = {
                followedRunner = null
                fitAll = false
            },
        )

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
                Wordmark(initials = displayName.initials())
                tracking?.let { AutoStopTimer(it) { showExtendSheet = true } }
            }
            tracking?.let { EndingSoonPill(it.expiresAt) }
        }

        Column(
            modifier = Modifier
                .align(Alignment.TopEnd)
                .statusBarsPadding()
                .padding(end = 14.dp, top = 8.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            RailButton(RailIcon.Share, "Share a link to watch me") {
                if (watchLink !is WatchLinkState.Ready) makeWatchLink()
                showShare = true
            }
            RailButton(RailIcon.Fit, "Fit all runners", active = fitAll) {
                followedRunner = null
                fitAll = true
                fitAllTick++
            }
            RailButton(RailIcon.Eye, "Who can see me") { showWhoCanSeeMe = true }
        }

        // While paused for battery the card below takes the bottom edge with its own Stop.
        if (!pausedForBattery) {
            CountChip(
                crewOut = crewOut,
                nearby = nearby.size.takeIf { casualState.visibleToNonCrew },
                hasNoCrew = crewState.isAvailable && crewState.members.isEmpty(),
                onClick = if (crewState.isAvailable) ({ crewStage = CrewStage.List }) else null,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .navigationBarsPadding()
                    .padding(start = 14.dp, bottom = 26.dp),
            )
            HoldToStopPill(
                onComplete = onStop,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .navigationBarsPadding()
                    .padding(end = 14.dp, bottom = 26.dp),
            )
        } else {
            BatteryPausedCard(
                lastError = tracking?.lastError,
                onSendNow = { LocationTrackingService.sendNow(context) },
                onAuto = { LocationTrackingService.resumeAutomatic(context) },
                onStop = onStop,
                stopLabel = "Stop",
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .navigationBarsPadding()
                    .padding(start = 12.dp, end = 12.dp, bottom = 16.dp),
            )
        }
    }
}

/** "2 crew out · 5 nearby". The nearby part appears only when the person has chosen to be seen,
 *  because that is also when they can see others. Tapping it opens the crew sheet so crew can be
 *  added mid-run; with no crew yet it says so and offers the add. [onClick] is null while crew is
 *  unavailable. */
@Composable
private fun CountChip(
    crewOut: Int,
    nearby: Int?,
    hasNoCrew: Boolean,
    onClick: (() -> Unit)?,
    modifier: Modifier = Modifier,
) {
    val text = buildString {
        append(if (hasNoCrew) "No crew yet" else if (crewOut == 0) "No crew out" else "$crewOut crew out")
        if (nearby != null) append("  ·  $nearby nearby")
    }
    Row(
        modifier = modifier
            .height(36.dp)
            .clip(CircleShape)
            .background(GlassColor)
            .then(if (onClick != null) Modifier.clickable(onClickLabel = "Open your crew", onClick = onClick) else Modifier)
            .padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        val ink = androidx.compose.ui.graphics.Color(0xFF14181F)
        Text(text, color = ink, fontSize = 12.5.sp, fontWeight = FontWeight.SemiBold)
        if (onClick != null) {
            Icon(
                imageVector = if (hasNoCrew) Icons.Default.Add else Icons.AutoMirrored.Filled.KeyboardArrowRight,
                contentDescription = null,
                tint = ink,
                modifier = Modifier.size(16.dp),
            )
        }
    }
}
