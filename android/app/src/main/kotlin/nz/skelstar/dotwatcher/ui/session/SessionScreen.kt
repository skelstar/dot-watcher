package nz.skelstar.dotwatcher.ui.session

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.HelpOutline
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.ContentPaste
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.AccountInfo
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.SessionUiState
import nz.skelstar.dotwatcher.casual.CasualModel
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.events.EventsModel
import nz.skelstar.dotwatcher.ui.casual.BeforeYouGoSheet
import nz.skelstar.dotwatcher.ui.casual.crewOutLine
import nz.skelstar.dotwatcher.ui.casual.WhoCanSeeMeSheet
import java.time.Duration
import nz.skelstar.dotwatcher.ui.CodeBoxField
import nz.skelstar.dotwatcher.ui.crew.CrewFlow
import nz.skelstar.dotwatcher.ui.crew.CrewRow
import nz.skelstar.dotwatcher.ui.crew.CrewStage
import nz.skelstar.dotwatcher.ui.landing.EventSignButton
import nz.skelstar.dotwatcher.ui.landing.HoldSignButton
import nz.skelstar.dotwatcher.ui.landing.SignButton
import nz.skelstar.dotwatcher.nearby.ActiveSessionConflict
import nz.skelstar.dotwatcher.nearby.NEARBY_MAX_TITLE_LENGTH
import nz.skelstar.dotwatcher.nearby.NearbyDraft
import nz.skelstar.dotwatcher.network.NearbyEvent
import nz.skelstar.dotwatcher.ui.nearby.ActiveSessionDialog
import nz.skelstar.dotwatcher.ui.nearby.NearbyCreateSection
import nz.skelstar.dotwatcher.ui.nearby.NearbyJoinSheet

/** How often the nearby list refreshes by itself while the start screen is showing, matching iOS. Pulling the
 *  screen down refreshes at once; this only stops a screen left open from going stale, and each refresh takes a
 *  GPS fix, so it is slow. */
private const val EVENTS_REFRESH_MS = 3 * 60_000L

/**
 * Lets a signed-in runner either start a new session ([POST /sessions]) or join an existing one
 * by invite code ([POST /session-invites/{inviteCode}/join] with role "runner" — repo root
 * README.md, "Auth model"). Rejoining a session is only by typing its invite code again: iOS
 * removed its "Recent Sessions" card, and so has Android. iOS shows this screen only when there's
 * no active session; Android's single linear flow reaches it the same way (via [leaveSession][
 * nz.skelstar.dotwatcher.AppViewModel.leaveSession] or first sign-in).
 *
 * Laid out to match the real iOS screen (media-files/Simulator Screenshot ... 16.19.49 / 16.23.23
 * / 16.20.27.png): an app header, a signed-in status row, a single "Join a Session" card (session
 * creation lives behind a "Create one" link that opens a dialog, not a second inline card).
 *
 * No display-name field here or in the create dialog: iOS doesn't ask per-session either — it
 * auto-fills from the account's own registered display name on every login
 * (`LocationManager.swift`'s `authenticate(path:body:)` sets `runnerName =
 * session.user.displayName.uppercased()`), only falling back to a one-time 2-letter-initials
 * entry sheet in the rare case that's empty. Android leaves `displayName` unset on
 * create/join, which the server already defaults to the account's own display name
 * (`server/Controllers/SessionsController.cs`) — the same outcome as iOS's common path,
 * without needing to port that rarely-hit fallback sheet.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SessionScreen(
    state: SessionUiState,
    onCreateSession: (sessionName: String?, nearby: NearbyDraft?) -> Unit,
    onJoinSession: (inviteCode: String) -> Unit,
    onSignOut: () -> Unit,
    account: AccountInfo,
    onDeleteAccount: (onError: (String) -> Unit) -> Unit,
    crew: CrewModel,
    casual: CasualModel,
    events: EventsModel,
    onStartCasual: (Duration) -> Unit,
    onJoinNearby: (sessionId: String) -> Unit,
    activeSessionConflict: ActiveSessionConflict?,
    onEndActiveSessionAndCreate: () -> Unit,
    onDismissActiveSessionConflict: () -> Unit,
    hasPreciseLocation: Boolean,
) {
    var showJoinDialog by remember { mutableStateOf(false) }
    // Pull-to-refresh: the spinner shows until the nearby list has loaded.
    var refreshing by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    // A nearby run the person tapped, showing its join sheet; null when none is up.
    var nearbyCandidate by remember { mutableStateOf<NearbyEvent?>(null) }
    var joinAttempted by remember { mutableStateOf(false) }
    var showBeforeYouGo by remember { mutableStateOf(false) }
    var showWhoCanSeeMe by remember { mutableStateOf(false) }
    val casualState by casual.state.collectAsState()
    val eventsState by events.state.collectAsState()
    var showCreateDialog by remember { mutableStateOf(false) }
    var showAccount by remember { mutableStateOf(false) }
    var crewStage by remember { mutableStateOf(CrewStage.None) }
    val crewState by crew.state.collectAsState()

    // Crew changes on other people's phones (a code redeemed, a removal), and there are no push
    // notifications, so pick it up whenever this screen comes back to the front.
    LifecycleResumeEffect(Unit) {
        crew.refresh()
        casual.refresh()
        events.refresh()
        onPauseOrDispose { }
    }

    // Runs near the person change as people arrive and the organiser moves the join location. Pulling the screen
    // down refreshes at once; this slower refresh only keeps a screen that is left open from going stale. It
    // runs while the app is visible (screen on, app in front) and not while tracking: the map replaces this
    // screen then. Each refresh takes a one-off GPS fix, hence the long interval.
    val lifecycleOwner = LocalLifecycleOwner.current
    LaunchedEffect(lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (isActive) {
                delay(EVENTS_REFRESH_MS)
                events.refresh()
            }
        }
    }

    // Header, crew and any error scroll; the three sign buttons stay pinned to the bottom where a
    // thumb reaches them. Kept inside the system bars: with three-button navigation the bottom button
    // would otherwise run under the nav bar (the map screens allow for the bars themselves).
    Column(modifier = Modifier.fillMaxSize().safeDrawingPadding()) {
        PullToRefreshBox(
            isRefreshing = refreshing,
            onRefresh = {
                scope.launch {
                    refreshing = true
                    crew.refresh()
                    casual.refresh()
                    events.refreshNow()
                    refreshing = false
                }
            },
            modifier = Modifier.weight(1f),
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .verticalScroll(rememberScrollState())
                    .padding(start = 24.dp, top = 8.dp, end = 24.dp),
            ) {
                AppHeader(onAccountClick = { showAccount = true })
                Spacer(modifier = Modifier.height(24.dp))

                // Hidden entirely until the server says it has crew (see CrewRow).
                if (crewState.isAvailable) {
                    CrewRow(state = crewState, onClick = { crewStage = CrewStage.List })
                    Spacer(modifier = Modifier.height(12.dp))
                }

                // Hidden until the server has casual for this account, and when there is no crew to be out.
                if (casualState.isAvailable && crewState.members.isNotEmpty()) {
                    crewOutLine(casualState.crewOut)?.let {
                        Text(
                            text = it,
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }

                when (state) {
                    is SessionUiState.Loading -> {
                        Spacer(modifier = Modifier.height(16.dp))
                        CircularProgressIndicator()
                    }
                    is SessionUiState.Error -> {
                        Spacer(modifier = Modifier.height(16.dp))
                        Text(
                            text = state.message,
                            color = MaterialTheme.colorScheme.error,
                        )
                    }
                    SessionUiState.Idle -> Unit
                }
            }
        }

        Column(
            modifier = Modifier.padding(start = 24.dp, top = 16.dp, end = 24.dp, bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            if (casualState.isAvailable) {
                HoldSignButton(
                    title = "Go",
                    subtitle = "Hold to start",
                    onComplete = { showBeforeYouGo = true },
                )
            }
            // Only when the server has events for this account (the "events" feature): the runs people
            // nearby can join, or the placeholder while there's no position to look near.
            eventsState.joinable.forEach { event ->
                EventSignButton(
                    eyebrow = event.eyebrow,
                    title = event.title,
                    subtitle = event.subtitle,
                    onClick = {
                        joinAttempted = false
                        nearbyCandidate = event
                    },
                )
            }
            if (eventsState.joinable.isEmpty()) {
                eventsState.events.firstOrNull()?.let { event ->
                    EventSignButton(
                        eyebrow = event.eyebrow,
                        title = event.title,
                        subtitle = event.subtitle,
                        onClick = {},
                    )
                }
            }
            SignButton(
                title = "Join",
                subtitle = "Enter an invite code",
                enabled = state !is SessionUiState.Loading,
                onClick = { showJoinDialog = true },
            )
            SignButton(
                title = "Create",
                subtitle = "Set up a new run for others",
                enabled = state !is SessionUiState.Loading,
                onClick = { showCreateDialog = true },
            )
        }
    }

    if (showBeforeYouGo) {
        BeforeYouGoSheet(
            onGo = { duration ->
                showBeforeYouGo = false
                onStartCasual(duration)
            },
            onChooseWhoCanSeeMe = {
                showBeforeYouGo = false
                showWhoCanSeeMe = true
            },
            onDismiss = { showBeforeYouGo = false },
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

    if (showAccount) {
        AccountSheet(
            account = account,
            onSignOut = {
                showAccount = false
                onSignOut()
            },
            onDeleteAccount = onDeleteAccount,
            onDismiss = { showAccount = false },
            showNickname = crewState.isAvailable,
            nickname = crewState.nickname,
            nicknameError = crewState.nicknameError,
            onSaveNickname = { crew.setNickname(it) },
            onEditNickname = crew::clearNicknameError,
        )
    }

    CrewFlow(crew = crew, state = crewState, stage = crewStage, onStage = { crewStage = it })

    nearbyCandidate?.let { event ->
        NearbyJoinSheet(
            event = event,
            state = if (joinAttempted) state else SessionUiState.Idle,
            onJoin = {
                event.sessionId?.let {
                    joinAttempted = true
                    onJoinNearby(it)
                }
            },
            onDismiss = { nearbyCandidate = null },
        )
    }

    if (activeSessionConflict != null) {
        ActiveSessionDialog(
            onEndAndCreate = onEndActiveSessionAndCreate,
            onDismiss = onDismissActiveSessionConflict,
        )
    }

    if (showJoinDialog) {
        JoinSessionDialog(
            enabled = state !is SessionUiState.Loading,
            onJoin = { code ->
                showJoinDialog = false
                onJoinSession(code)
            },
            onCancel = { showJoinDialog = false },
        )
    }

    if (showCreateDialog) {
        CreateSessionDialog(
            enabled = state !is SessionUiState.Loading,
            nearbyAvailable = eventsState.isAvailable,
            hasPreciseLocation = hasPreciseLocation,
            onCreate = { sessionName, nearby ->
                showCreateDialog = false
                onCreateSession(sessionName, nearby)
            },
            onCancel = { showCreateDialog = false },
        )
    }
}

private val HeaderIconSize = 34.dp
private val HeaderButtonSize = 44.dp

/** App wordmark + badge + build info, with account/help icon buttons — matches the iOS header
 *  shown atop every no-session-screen state (media-files/Simulator Screenshot ... 16.19.49.png).
 *  No git-commit short-SHA (iOS's "1105bf8"): that needs a dedicated build-time step this app
 *  doesn't have yet, so this only shows [BuildConfig.VERSION_NAME]/[BuildConfig.VERSION_CODE],
 *  which are already available. */
@Composable
private fun AppHeader(onAccountClick: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.Top,
    ) {
        Column {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(text = "dot", style = MaterialTheme.typography.headlineLarge)
                Box(
                    modifier = Modifier
                        .padding(horizontal = 4.dp)
                        .size(32.dp)
                        .background(MaterialTheme.colorScheme.primary, CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(
                        text = "DW",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onPrimary,
                    )
                }
                Text(text = "watchr", style = MaterialTheme.typography.headlineLarge)
            }
            Text(
                text = "v${BuildConfig.VERSION_NAME} · build ${BuildConfig.VERSION_CODE}",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        // iOS draws these as ~28pt (.title) primary-colored glyphs. Material's circle glyphs only fill
        // about 20/24 of their box, so the box must be ~34dp for the visible circle to be ~28dp.
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            IconButton(onClick = onAccountClick, modifier = Modifier.size(HeaderButtonSize)) {
                Icon(
                    imageVector = Icons.Default.AccountCircle,
                    contentDescription = "Account",
                    modifier = Modifier.size(HeaderIconSize),
                    tint = MaterialTheme.colorScheme.onSurface,
                )
            }
            IconButton(onClick = {}, modifier = Modifier.size(HeaderButtonSize)) {
                Icon(
                    imageVector = Icons.AutoMirrored.Filled.HelpOutline,
                    contentDescription = "Help",
                    modifier = Modifier.size(HeaderIconSize),
                    tint = MaterialTheme.colorScheme.onSurface,
                )
            }
        }
    }
}

/** The invite-code entry that used to be the inline "Join a Session" card: paste or type the six
 *  characters, then join. Opened from the Join sign. */
@Composable
private fun JoinSessionDialog(
    enabled: Boolean,
    onJoin: (inviteCode: String) -> Unit,
    onCancel: () -> Unit,
) {
    var inviteCode by remember { mutableStateOf("") }
    val clipboard = LocalClipboardManager.current

    Dialog(onDismissRequest = onCancel) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(20.dp))
                .padding(24.dp),
        ) {
            Text(
                text = "Enter your invite code",
                textAlign = TextAlign.Center,
                style = MaterialTheme.typography.titleLarge,
            )
            Spacer(modifier = Modifier.height(12.dp))
            TextButton(onClick = {
                clipboard.getText()?.text?.let { pasted -> inviteCode = pasted }
            }) {
                Icon(
                    imageVector = Icons.Default.ContentPaste,
                    contentDescription = null,
                    modifier = Modifier.size(18.dp),
                )
                Spacer(modifier = Modifier.width(4.dp))
                Text("Paste")
            }
            Spacer(modifier = Modifier.height(8.dp))
            CodeBoxField(
                value = inviteCode,
                onValueChange = { inviteCode = it },
                length = 6,
            )
            Spacer(modifier = Modifier.height(20.dp))
            Button(
                onClick = { onJoin(inviteCode) },
                // Requires the full 6 characters, matching iOS's noSessionJoinCard
                // (`disabled(... || noSessionInviteCode.count < 6)`).
                enabled = enabled && inviteCode.length == 6,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("Join Session")
            }
            Spacer(modifier = Modifier.height(4.dp))
            TextButton(onClick = onCancel, modifier = Modifier.fillMaxWidth()) {
                Text("Cancel")
            }
        }
    }
}

/** Matches iOS's "Create a New Session" sheet (media-files/Simulator Screenshot ...
 *  16.20.27.png): a centered title/subtitle, an 8-box session-name field, a filled action
 *  button, and a plain-text cancel row — reached from [SessionScreen]'s "Create one" link
 *  rather than being a second card on the main screen.
 *
 * A plain [Dialog] rather than [AlertDialog]: Material3's AlertDialog hard-codes a tinted
 * `surfaceContainerHigh` background and generous title/content padding that read as a stock
 * Android sheet rather than iOS's plain white, tightly-spaced card — using Dialog gives full
 * control over both to match. */
@Composable
private fun CreateSessionDialog(
    enabled: Boolean,
    nearbyAvailable: Boolean,
    hasPreciseLocation: Boolean,
    onCreate: (sessionName: String?, nearby: NearbyDraft?) -> Unit,
    onCancel: () -> Unit,
) {
    var sessionName by remember { mutableStateOf("") }
    var nearby by remember { mutableStateOf(NearbyDraft()) }
    // With nearby on the name becomes a public title and the code-style box goes away.
    val nearbyIsOn = nearbyAvailable && nearby.isOn

    Dialog(onDismissRequest = onCancel) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            modifier = Modifier
                .fillMaxWidth()
                .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(20.dp))
                .verticalScroll(rememberScrollState())
                .padding(24.dp),
        ) {
            Text(
                text = "Create a New Session",
                textAlign = TextAlign.Center,
                style = MaterialTheme.typography.titleLarge,
            )
            Spacer(modifier = Modifier.height(12.dp))
            if (nearbyIsOn) {
                Text(
                    text = "Give your run a title. People nearby will see it. You'll still get an invite code to share.",
                    textAlign = TextAlign.Center,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(modifier = Modifier.height(20.dp))
                OutlinedTextField(
                    value = nearby.title,
                    onValueChange = { if (it.length <= NEARBY_MAX_TITLE_LENGTH) nearby = nearby.copy(title = it) },
                    label = { Text("Title") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
            } else {
                Text(
                    text = "Give your session a name. You'll get an invite code to share with others.",
                    textAlign = TextAlign.Center,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(modifier = Modifier.height(20.dp))
                CodeBoxField(
                    value = sessionName,
                    onValueChange = { sessionName = it },
                    length = 8,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            if (nearbyAvailable) {
                Spacer(modifier = Modifier.height(20.dp))
                NearbyCreateSection(
                    draft = nearby,
                    onChange = { nearby = it },
                    hasPreciseLocation = hasPreciseLocation,
                )
            }
            Spacer(modifier = Modifier.height(20.dp))
            Button(
                onClick = { onCreate(sessionName.ifBlank { null }, nearby.takeIf { nearbyIsOn }) },
                enabled = enabled && if (nearbyIsOn) {
                    nearby.trimmedTitle.isNotEmpty() && hasPreciseLocation
                } else {
                    sessionName.isEmpty() || sessionName.length >= 4
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("Create Session")
            }
            Spacer(modifier = Modifier.height(4.dp))
            TextButton(onClick = onCancel, modifier = Modifier.fillMaxWidth()) {
                Text("Cancel")
            }
        }
    }
}
