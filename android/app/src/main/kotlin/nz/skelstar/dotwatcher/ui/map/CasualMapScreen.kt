package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
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
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
import nz.skelstar.dotwatcher.network.networkMessage
import nz.skelstar.dotwatcher.route.CasualRouteCache
import nz.skelstar.dotwatcher.route.RoutePoint
import nz.skelstar.dotwatcher.ui.casual.StopPill
import nz.skelstar.dotwatcher.groups.GroupsModel
import nz.skelstar.dotwatcher.groups.GroupOrganiserActions
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.ui.groups.GoMapGroupBar
import nz.skelstar.dotwatcher.ui.groups.GroupsOnMapSheet
import java.time.Instant
import nz.skelstar.dotwatcher.ui.casual.ShareRunSheet
import nz.skelstar.dotwatcher.ui.casual.WatchLinkState
import nz.skelstar.dotwatcher.ui.casual.WhoCanSeeMeSheet
import nz.skelstar.dotwatcher.ui.crew.CrewFlow
import nz.skelstar.dotwatcher.ui.crew.CrewStage
import nz.skelstar.dotwatcher.ui.session.SettingsSheet
import nz.skelstar.dotwatcher.ui.theme.AppearanceMode

private const val POLL_INTERVAL_MS = 10_000L

/**
 * The map while out casually, laid out like [LiveMapScreen]: wordmark and auto-stop countdown
 * top-left, a right rail, a Stop bottom-right. There is no session behind it, so no invite or web
 * map buttons; instead an eye opens "Who can see me" and a Crew button opens the crew sheet. Bottom left is
 * the same dot chip as the session map: you and your crew who are out, tap one to follow them. Polls `GET /casual/live`, which returns this person, their crew by name, and (only
 * when both have chosen to be seen) anonymous dots within 5 km. Stopping is a press-and-hold.
 */
@Composable
fun CasualMapScreen(
    repository: DotWatcherRepository,
    casual: CasualModel,
    crew: CrewModel,
    appearance: AppearanceMode,
    onAppearanceChange: (AppearanceMode) -> Unit,
    onStop: () -> Unit,
    /** Groups the person is in (see .ai/plans/go-and-groups.md); null without the "groups" feature. */
    groups: GroupsModel? = null,
    /** "‹ Home": the start screen with tracking carrying on; null hides it (no groups). */
    onHome: (() -> Unit)? = null,
    /** The organiser's controls for groups the person organised. */
    organiserActions: ((SessionMembership) -> GroupOrganiserActions)? = null,
) {
    val context = LocalContext.current
    var positions by remember { mutableStateOf<List<RunnerPosition>>(emptyList()) }
    var locked by remember { mutableStateOf<Set<String>>(emptySet()) }
    var nearby by remember { mutableStateOf<List<NearbyDot>>(emptyList()) }
    var showPeople by remember { mutableStateOf(false) }
    /** This person's runner name as the server sends it (their own row), for the chip's "you". */
    var ownName by remember { mutableStateOf<String?>(null) }
    var showExtendSheet by remember { mutableStateOf(false) }
    var showWhoCanSeeMe by remember { mutableStateOf(false) }
    var showShare by remember { mutableStateOf(false) }
    var showSettings by remember { mutableStateOf(false) }
    var crewStage by remember { mutableStateOf(CrewStage.None) }
    val crewState by crew.state.collectAsState()
    var showGroups by remember { mutableStateOf(false) }
    val groupsState = groups?.state?.collectAsState()?.value
    // The chip's time left counts down; once a minute is enough.
    var now by remember { mutableStateOf(Instant.now()) }
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

    // GPX routes: this person's own and those of crew who are out, kept in step with the poll so a
    // route someone adds or removes shows for everyone within one poll (as on iOS).
    val routeCache = remember { CasualRouteCache() }
    var routeLines by remember { mutableStateOf<List<List<RoutePoint>>>(emptyList()) }
    var hasOwnRoute by remember { mutableStateOf(false) }
    var routeFocus by remember { mutableStateOf<List<RoutePoint>?>(null) }
    var routeFocusTick by remember { mutableStateOf(0) }

    suspend fun loadLive() {
        val live = runCatching { repository.casualLive() }.getOrNull()?.body() ?: return
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
        nearby = live.nearby

        val refs = live.routes.orEmpty()
        val selfName = everyone.firstOrNull { it.isSelf }?.runnerName
        ownName = selfName
        hasOwnRoute = selfName != null && refs.any { it.runnerName == selfName }
        routeCache.sync(refs) { runId ->
            runCatching { repository.getCasualRoute(runId) }.getOrNull()
                ?.takeIf { it.isSuccessful }
                ?.let { response -> runCatching { response.body()?.bytes() }.getOrNull() }
        }
        routeLines = routeCache.lines
    }

    // Only while the app is visible: the foreground service keeps sending this device's position
    // regardless, and nobody is looking at the map with the screen locked.
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (isActive) {
                loadLive()
                delay(POLL_INTERVAL_MS)
            }
        }
    }
    // The groups list changes rarely (join, leave, end), so it is fetched when the map comes to the front, and
    // the clock for the chip ticks once a minute.
    if (groups != null) {
        LaunchedEffect(lifecycleOwner) {
            lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
                groups.refreshNow()
                while (isActive) {
                    now = Instant.now()
                    delay(60_000)
                }
            }
        }
    }

    if (showGroups && groups != null && groupsState != null) {
        GroupsOnMapSheet(
            groups = groupsState.joined,
            now = now,
            onLeave = groups::leave,
            onDismiss = {
                showGroups = false
                groups.clearError()
            },
            error = groupsState.error,
            organiserActions = organiserActions,
        )
    }

    // This person's own route, shown to their crew and to anyone with their watch link.
    val routeActions = RouteActions(
        hasRoute = hasOwnRoute,
        removeMessage = "This removes your route for you, your crew and anyone watching your link.",
        upload = { gpx ->
            val result = runCatching { repository.setCasualRoute(gpx) }
            val response = result.getOrNull()
            when {
                response == null -> result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again."
                !response.isSuccessful -> response.errorMessageOrStatus()
                else -> {
                    loadLive()
                    null
                }
            }
        },
        remove = {
            val result = runCatching { repository.deleteCasualRoute() }
            val response = result.getOrNull()
            when {
                response == null -> result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again."
                !response.isSuccessful -> response.errorMessageOrStatus()
                else -> {
                    loadLive()
                    null
                }
            }
        },
    )

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

    if (showSettings) {
        SettingsSheet(
            appearance = appearance,
            onAppearanceChange = onAppearanceChange,
            onDismiss = { showSettings = false },
        )
    }

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

    // You first, then crew who are out. Their runner names can be crew nicknames, so the dots take their
    // letters from the account's initials (displayName), as the pins do.
    val me = ownName ?: displayName
    val peopleNames = sessionNames(me, positions)
    val peopleLabels = positions.mapNotNull { p -> p.displayName?.let { p.runnerName to it } }.toMap()
    fun toggleFollow(name: String) {
        followedRunner = if (name == followedRunner) null else name
        fitAll = followedRunner == null
        if (fitAll) fitAllTick++
    }

    if (showPeople) {
        MapPeopleSheet(
            title = "Out now",
            names = peopleNames,
            me = me,
            positions = positions,
            followed = followedRunner,
            labels = peopleLabels,
            onSelect = { name ->
                showPeople = false
                followedRunner = name
                fitAll = false
            },
            onDismiss = { showPeople = false },
        )
    }

    if (showWhoCanSeeMe) {
        WhoCanSeeMeSheet(
            me = displayName,
            crew = crewState.members,
            visibleToCrew = casualState.visibleToCrew,
            visibleToNonCrew = casualState.visibleToNonCrew,
            hideStartArea = casualState.hideStartArea,
            error = casualState.error,
            onCrewVisibleChange = casual::setVisibleToCrew,
            onVisibleChange = casual::setVisibleToNonCrew,
            onHideStartAreaChange = casual::setHideStartArea,
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
            routes = routeLines,
            routeFocus = routeFocus,
            routeFocusTick = routeFocusTick,
            onRunnerTapped = { name ->
                followedRunner = if (name == followedRunner) null else name
                fitAll = false
            },
            onUserMovedCamera = {
                followedRunner = null
                fitAll = false
            },
        )

        Box(
            modifier = Modifier
                .align(Alignment.TopStart)
                .statusBarsPadding()
                .padding(start = 14.dp, top = 8.dp),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (tracking != null) {
                    WordmarkTimerPill(displayName.initials(), tracking) { showExtendSheet = true }
                } else {
                    Wordmark(initials = displayName.initials())
                }
                if (onHome != null) {
                    GoMapGroupBar(
                        groups = groupsState?.joined.orEmpty(),
                        now = now,
                        onHome = onHome,
                        onOpenGroups = { showGroups = true },
                    )
                }
            }
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
            // The crew sheet, where crew can be added mid-run (the bottom-left chip used to open it).
            if (crewState.isAvailable) {
                RailButton(RailIcon.Crew, "Crew") { crewStage = CrewStage.List }
            }
            RouteRailButton(routeActions) { points ->
                // Show the new route whole, and stop fitting so the next poll doesn't pull away from it.
                followedRunner = null
                fitAll = false
                routeFocus = points
                routeFocusTick++
            }
            RailButton(RailIcon.Settings, "Settings") { showSettings = true }
        }

        // While paused for battery the card below takes the bottom edge with its own Stop.
        if (!pausedForBattery) {
            MapPeopleChip(
                names = peopleNames,
                me = me,
                positions = positions,
                followed = followedRunner,
                countLabel = "You and ${peopleNames.size - 1} crew out",
                labels = peopleLabels,
                onFollow = ::toggleFollow,
                onShowAll = { showPeople = true },
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .navigationBarsPadding()
                    .padding(start = 14.dp, bottom = 26.dp),
            )
            StopPill(
                onStop = onStop,
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
