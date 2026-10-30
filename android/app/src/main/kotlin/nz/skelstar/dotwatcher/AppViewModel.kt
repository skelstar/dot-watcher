package nz.skelstar.dotwatcher

import android.Manifest
import android.app.Application
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.casual.CasualModel
import nz.skelstar.dotwatcher.casual.CasualPrefs
import nz.skelstar.dotwatcher.events.EventsModel
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.crew.CrewPrefs
import nz.skelstar.dotwatcher.data.AuthTokenStore
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.data.ShareChoiceStore
import nz.skelstar.dotwatcher.location.CASUAL_SESSION_ID
import nz.skelstar.dotwatcher.location.Fix
import nz.skelstar.dotwatcher.location.LocationTrackingService
import nz.skelstar.dotwatcher.location.NearbyFixSource
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.nearby.ActiveSessionConflict
import nz.skelstar.dotwatcher.nearby.NEARBY_DEFAULT_RADIUS_METRES
import nz.skelstar.dotwatcher.nearby.NearbyCreate
import nz.skelstar.dotwatcher.nearby.NearbyDraft
import nz.skelstar.dotwatcher.nearby.isUsableForJoinLocation
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import nz.skelstar.dotwatcher.network.NearbyStatus
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
import nz.skelstar.dotwatcher.network.networkMessage
import nz.skelstar.dotwatcher.network.parsedError
import retrofit2.Response
import java.time.Duration
import java.time.Instant

/** Where the nav graph should route to based on sign-in/session state. */
sealed interface Destination {
    data object Auth : Destination
    /** Signed in, checking the server for a session still active from before the app was
     *  relaunched — shown briefly so the create/join screen doesn't flash first. */
    data object Restoring : Destination
    data object Session : Destination
    /** Asks how long to share this device's own position for, before starting the background tracking
     *  service. Declining leaves the session: there is no view-only map. */
    data class Consent(val membership: SessionMembership) : Destination
    data class Map(val membership: SessionMembership) : Destination

    /** Out casually: the live map with no session behind it. See .ai/plans/casual-tracking.md. */
    data object Casual : Destination
}

sealed interface AuthUiState {
    data object Idle : AuthUiState
    data object Loading : AuthUiState
    data class Error(val message: String) : AuthUiState
}

sealed interface SessionUiState {
    data object Idle : SessionUiState
    data object Loading : SessionUiState
    data class Error(val message: String) : SessionUiState
}

/**
 * App-scoped state holder for the app's single linear flow (auth -> session -> consent -> map).
 * Owns the repository so screens don't construct their own network stack. Kept as one ViewModel
 * rather than one per screen while the flow is this small; revisit if Milestone 3's fuller
 * session-list UI needs more independent state.
 */
class AppViewModel(application: Application) : AndroidViewModel(application) {
    private val tokenStore = AuthTokenStore(application)
    private val shareChoices = ShareChoiceStore(application)
    private val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL, application)
    val repository = DotWatcherRepository(api, tokenStore)

    /** The signed-in account's crew. Shares this ViewModel's repository and scope rather than being a
     *  second ViewModel, so there is one network stack (see the class note above). */
    val crew = CrewModel(repository, viewModelScope, CrewPrefs(application))

    /** One-off positions for nearby join; never starts tracking. */
    private val nearbyFix = NearbyFixSource(application)

    /** Public events near the signed-in account: the runs people nearby can join. */
    val events = EventsModel(repository, viewModelScope, nearbyFix::current)

    /** Whether the Create dialog can place a nearby run: location allowed, and precise. */
    val hasPreciseLocation: Boolean get() = nearbyFix.hasPreciseLocation

    /** Nearby settings of sessions this device made nearby in this run, as last reported by the server. Only
     *  the creator can Ping or End, and the app leaves old sessions on relaunch, so remembering them here is
     *  enough. */
    private val _nearbyStatuses = MutableStateFlow<Map<String, NearbyStatus>>(emptyMap())
    val nearbyStatuses: StateFlow<Map<String, NearbyStatus>> = _nearbyStatuses

    /** Set when creating was refused because the person already has a session running (409); the start
     *  screen asks whether to end it. */
    private val _activeSessionConflict = MutableStateFlow<ActiveSessionConflict?>(null)
    val activeSessionConflict: StateFlow<ActiveSessionConflict?> = _activeSessionConflict

    /** Casual tracking: start tracking without creating or joining a session. */
    val casual = CasualModel(repository, viewModelScope, CasualPrefs(application))

    private val _destination = MutableStateFlow<Destination>(
        if (tokenStore.isSignedIn) Destination.Restoring else Destination.Auth
    )
    val destination: StateFlow<Destination> = _destination

    private val _authState = MutableStateFlow<AuthUiState>(AuthUiState.Idle)
    val authState: StateFlow<AuthUiState> = _authState

    private val _sessionState = MutableStateFlow<SessionUiState>(SessionUiState.Idle)
    val sessionState: StateFlow<SessionUiState> = _sessionState

    init {
        if (tokenStore.isSignedIn) {
            restoreActiveSession()
        }
    }

    /**
     * [_destination] is in-memory only, so a relaunch (Back out of the app, or the OS killing the
     * process) would otherwise land a runner on create/join while still a member server-side — and,
     * if sharing was running in the foreground service, with no Stop button. Mirrors iOS's
     * `loadSessions()` picking the first active membership. A failed load falls back to the
     * create/join screen.
     */
    /** Sharing in the background needs foreground *and* background location; without them the
     *  consent screen (which explains how to grant it) is the right place to land. */
    private fun hasLocationPermissionsForSharing(): Boolean {
        val app = getApplication<Application>()
        fun granted(permission: String) =
            ContextCompat.checkSelfPermission(app, permission) == PackageManager.PERMISSION_GRANTED
        val foreground = granted(Manifest.permission.ACCESS_FINE_LOCATION) ||
            granted(Manifest.permission.ACCESS_COARSE_LOCATION)
        val background = Build.VERSION.SDK_INT < Build.VERSION_CODES.Q ||
            granted(Manifest.permission.ACCESS_BACKGROUND_LOCATION)
        return foreground && background
    }

    private fun restoreActiveSession() {
        viewModelScope.launch {
            // Out casually, with the service still running or a share that has not run out: carry
            // on, don't ask. (Casual has no membership, so the session restore below can't see it.)
            if (restoreCasual()) return@launch

            val memberships = runCatching { repository.getMySessions() }
                .getOrNull()
                ?.takeIf { it.isSuccessful }
                ?.body()
                .orEmpty()

            // Don't clobber a navigation the user made while this was in flight.
            if (_destination.value != Destination.Restoring) return@launch

            val decision = decideRestore(
                memberships = memberships,
                tracking = LocationTrackingService.state.value as? TrackingState.Tracking,
                choice = shareChoices.read(),
                now = Instant.now(),
                canResumeSharing = hasLocationPermissionsForSharing(),
            )
            if (decision is RestoreDecision.LeaveStale) {
                // A session doesn't carry over a relaunch: leave it, so the others stop seeing someone who
                // isn't sharing, and start from the create/join screen.
                shareChoices.clear()
                decision.memberships.forEach { runCatching { repository.leaveSession(it.sessionId) } }
                if (_destination.value == Destination.Restoring) _destination.value = Destination.Session
                return@launch
            }
            _destination.value = when (decision) {
                RestoreDecision.CreateOrJoin, is RestoreDecision.LeaveStale -> Destination.Session
                is RestoreDecision.Map -> Destination.Map(decision.membership)
                is RestoreDecision.ResumeSharing -> {
                    // They already chose how long to share and it hasn't run out: carry on, don't ask.
                    LocationTrackingService.start(
                        getApplication(),
                        decision.membership.sessionId,
                        decision.remaining,
                        decision.total,
                    )
                    Destination.Map(decision.membership)
                }
            }
        }
    }

    /** True if the app landed on the casual map. */
    private suspend fun restoreCasual(): Boolean {
        val tracking = LocationTrackingService.state.value as? TrackingState.Tracking
        if (tracking?.sessionId == CASUAL_SESSION_ID) {
            if (_destination.value == Destination.Restoring) _destination.value = Destination.Casual
            return true
        }
        val choice = shareChoices.read() as? ShareChoiceStore.Choice.Sharing ?: return false
        val now = Instant.now()
        if (choice.sessionId != CASUAL_SESSION_ID || !choice.expiresAt.isAfter(now) || !hasLocationPermissionsForSharing()) {
            return false
        }
        // Starting again is idempotent on the server: it continues this run if it is still open.
        val started = runCatching { repository.casualStart() }.getOrNull()
        if (started == null || !started.isSuccessful || _destination.value != Destination.Restoring) return false
        LocationTrackingService.start(
            getApplication(),
            CASUAL_SESSION_ID,
            Duration.between(now, choice.expiresAt),
            maxOf(choice.totalDuration, Duration.between(now, choice.expiresAt)),
        )
        _destination.value = Destination.Casual
        return true
    }

    /** Holding Go, then Go on "Before you go": start the casual run on the server, then the
     *  tracking service, then show the map. A failure stays on the start screen with the reason. */
    fun startCasual(duration: Duration) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.casualStart() }
            val response = result.getOrNull()
            val started = response?.body()
            if (response != null && response.isSuccessful && started != null) {
                LocationTrackingService.start(getApplication(), started.sessionId, duration)
                _sessionState.value = SessionUiState.Idle
                _destination.value = Destination.Casual
            } else {
                _sessionState.value = SessionUiState.Error(
                    response?.errorMessageOrStatus() ?: (result.exceptionOrNull()?.networkMessage() ?: "Network error.")
                )
            }
        }
    }

    /** Ends the casual run: stops the service at once, tells the server (which takes the person off
     *  everyone's map), and returns to the start screen. */
    fun stopCasual() {
        LocationTrackingService.stop(getApplication())
        shareChoices.clear()
        _sessionState.value = SessionUiState.Idle
        _destination.value = Destination.Session
        viewModelScope.launch {
            runCatching { repository.casualStop() }
            casual.refresh()
        }
    }

    fun register(username: String, password: String, displayName: String) {
        viewModelScope.launch {
            _authState.value = AuthUiState.Loading
            val response = runCatching { repository.register(username, password, displayName) }
            _authState.value = handleAuthResult(response)
        }
    }

    fun login(username: String, password: String) {
        viewModelScope.launch {
            _authState.value = AuthUiState.Loading
            val response = runCatching { repository.login(username, password) }
            _authState.value = handleAuthResult(response, wrongCredentialsMessage = "Wrong username or password.")
        }
    }

    /** [wrongCredentialsMessage] replaces the server's reply to a failed sign-in: it answers a bad login with a
     *  bare 401 on purpose (it never says whether the username or the password was wrong). */
    private fun handleAuthResult(result: Result<Response<Unit>>, wrongCredentialsMessage: String? = null): AuthUiState {
        val response = result.getOrElse {
            return AuthUiState.Error(it.networkMessage())
        }
        return if (response.isSuccessful) {
            _destination.value = Destination.Session
            // A crew link opened before sign-in is handled now, as session invite links are.
            crew.consumePendingLink()
            AuthUiState.Idle
        } else {
            AuthUiState.Error(
                if (response.code() == 401 && wrongCredentialsMessage != null) wrongCredentialsMessage
                else response.errorMessageOrStatus()
            )
        }
    }

    /** The create/join screen's refresh button: re-checks for an active membership (e.g. one made
     *  from another device) and moves into it, otherwise lands back on create/join. */
    fun refresh() {
        _destination.value = Destination.Restoring
        restoreActiveSession()
        crew.refresh()
    }

    /** A crew link (https://…/crew/{CODE}) was opened: from a tap, or from the camera app scanning a
     *  QR code. Never adds anyone by itself; it only leads to the "Add X to your crew?" confirmation. */
    fun onCrewLink(code: String) = crew.onLink(code, signedIn = tokenStore.isSignedIn)

    /** No displayName param: matches iOS's common path, which auto-fills the per-session
     *  display name from the account's own registered display name rather than asking again
     *  (see SessionScreen.kt's kdoc) — the server already defaults to that when none is sent. */
    fun createSession(sessionName: String?, nearby: NearbyDraft? = null) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading

            // With nearby on, the name is a public title and the organiser's position becomes the first
            // join location, so a usable fix comes first.
            val draft = nearby?.takeIf { it.isOn }
            var nearbyCreate: NearbyCreate? = null
            if (draft != null) {
                val fix = nearbyFix.current()
                if (fix == null || !fix.isUsableForJoinLocation()) {
                    _sessionState.value = SessionUiState.Error(
                        if (nearbyFix.hasPreciseLocation) {
                            "Couldn't get an accurate location, so the run can't be shown nearby. " +
                                "Try again with a clear view of the sky."
                        } else {
                            "Turn on precise location for Dot Watcher in Settings to show a run nearby."
                        }
                    )
                    return@launch
                }
                nearbyCreate = NearbyCreate(draft.radiusMeters, fix)
            }

            val name = if (draft != null) draft.trimmedTitle else sessionName?.ifBlank { null }
            val result = runCatching { repository.createSession(name, displayName = null, nearby = nearbyCreate) }

            val response = result.getOrNull()
            if (response != null && response.code() == 409) {
                val error = response.parsedError()
                val activeId = error?.sessionId
                if (activeId == null) {
                    _sessionState.value = SessionUiState.Error(error?.error ?: "Request failed (409).")
                } else {
                    // What was entered is kept, so ending the old session can carry straight on.
                    _activeSessionConflict.value = ActiveSessionConflict(activeId, sessionName, nearby)
                    _sessionState.value = SessionUiState.Idle
                }
                return@launch
            }

            val created = nearbyCreate
            _sessionState.value = handleSessionResult(result) { membership ->
                if (created != null) {
                    setNearbyStatus(membership.sessionId, NearbyStatus(nearby = true, radiusMeters = created.radiusMeters))
                }
            }
        }
    }

    /** The person chose to end the session that was in the way, then create the new one with what they entered. */
    fun endActiveSessionAndCreate() {
        val conflict = _activeSessionConflict.value ?: return
        _activeSessionConflict.value = null
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.endSession(conflict.sessionId) }
            val response = result.getOrNull()
            if (response == null || !response.isSuccessful) {
                _sessionState.value = SessionUiState.Error(
                    response?.errorMessageOrStatus() ?: (result.exceptionOrNull()?.networkMessage() ?: "Network error.")
                )
                return@launch
            }
            createSession(conflict.sessionName, conflict.nearby)
        }
    }

    fun dismissActiveSessionConflict() {
        _activeSessionConflict.value = null
    }

    /** Joins a run from the nearby list. The server re-checks the position, and every refusal is the same 404. */
    fun joinNearby(sessionId: String) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val fix = nearbyFix.current()
            if (fix == null) {
                _sessionState.value = SessionUiState.Error(
                    "Couldn't get your location. Check that Location is allowed for Dot Watcher and try again."
                )
                return@launch
            }
            val result = runCatching { repository.joinNearbySession(sessionId, fix, displayName = null) }
            _sessionState.value = when (result.getOrNull()?.code()) {
                // One answer for every refusal, by design: too far, ended, switched off or gone.
                404 -> SessionUiState.Error("You can't join this run from here. Move closer to the start and try again.")
                429 -> SessionUiState.Error("Too many attempts. Try again in a little while.")
                else -> handleSessionResult(result)
            }
        }
    }

    private fun setNearbyStatus(sessionId: String, status: NearbyStatus) {
        _nearbyStatuses.update { it + (sessionId to status) }
    }

    /** The organiser's Ping: takes a fresh fix and makes it the session's join location. [radiusMeters] null
     *  keeps the session's current range. Returns words for the person, whether it worked or not. */
    suspend fun pingJoinLocation(sessionId: String, radiusMeters: Int? = null): String {
        val fix = nearbyFix.current()
        if (fix == null || !fix.isUsableForJoinLocation()) {
            return "Couldn't get an accurate location. Try again with a clear view of the sky."
        }
        val radius = radiusMeters ?: _nearbyStatuses.value[sessionId]?.radiusMeters ?: NEARBY_DEFAULT_RADIUS_METRES
        return updateNearby(sessionId, fix, enabled = true, radiusMeters = radius, done = "Join location set")
    }

    /** Stops the session being shown nearby; members stay. */
    suspend fun stopShowingNearby(sessionId: String): String =
        updateNearby(sessionId, fix = null, enabled = false, radiusMeters = null, done = "No longer shown nearby")

    private suspend fun updateNearby(
        sessionId: String,
        fix: Fix?,
        enabled: Boolean?,
        radiusMeters: Int?,
        done: String,
    ): String {
        val result = runCatching { repository.updateNearby(sessionId, fix, enabled, radiusMeters) }
        val response = result.getOrElse { return it.networkMessage() }
        val status = response.body()
        return when {
            response.isSuccessful && status != null -> {
                setNearbyStatus(sessionId, status)
                done
            }
            response.code() == 410 -> "This session has ended."
            else -> response.errorMessageOrStatus()
        }
    }

    /** Ends the session for everyone (owner only), then returns to the start screen. Null on success;
     *  otherwise words for what went wrong, and the person stays on the map. */
    suspend fun endSession(membership: SessionMembership): String? {
        val result = runCatching { repository.endSession(membership.sessionId) }
        val response = result.getOrElse { return it.networkMessage() }
        if (!response.isSuccessful) return response.errorMessageOrStatus()
        LocationTrackingService.stop(getApplication())
        shareChoices.clear()
        _nearbyStatuses.update { it - membership.sessionId }
        _sessionState.value = SessionUiState.Idle
        _destination.value = Destination.Session
        return null
    }

    fun joinSession(inviteCode: String) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.joinSession(inviteCode, displayName = null) }
            _sessionState.value = handleSessionResult(result)
        }
    }

    private fun handleSessionResult(
        result: Result<Response<SessionMembership>>,
        onSuccess: (SessionMembership) -> Unit = {},
    ): SessionUiState {
        val response = result.getOrElse {
            return SessionUiState.Error(it.networkMessage())
        }
        val membership = response.body()
        return if (response.isSuccessful && membership != null) {
            onSuccess(membership)
            _destination.value = Destination.Consent(membership)
            SessionUiState.Idle
        } else {
            SessionUiState.Error(response.errorMessageOrStatus())
        }
    }

    fun startSharing(membership: SessionMembership, duration: Duration) {
        // The service tracks one thing at a time. Starting a session replaces a casual run, so end
        // that one properly rather than leaving it open on the server.
        if ((LocationTrackingService.state.value as? TrackingState.Tracking)?.sessionId == CASUAL_SESSION_ID) {
            viewModelScope.launch { runCatching { repository.casualStop() } }
        }
        LocationTrackingService.start(getApplication(), membership.sessionId, duration)
        _destination.value = Destination.Map(membership)
    }

    /** "Don't share" leaves the session and returns to the create/join screen: there is no view-only map. */
    fun declineSharing(membership: SessionMembership) = leaveSession(membership)

    /** Leaves [membership]'s session for real ([DELETE /me/sessions/{sessionId}/membership] —
     *  matches iOS's `leaveSession(sessionId:)`) and returns to the create/join screen. Stops
     *  tracking first if active, mirroring iOS's `leaveOrDeleteSession` calling `location.stop()`
     *  before the network call. The caller (UI) is responsible for confirming with the runner
     *  first — this performs the leave unconditionally once called. */
    fun leaveSession(membership: SessionMembership) {
        LocationTrackingService.stop(getApplication())
        shareChoices.clear()
        viewModelScope.launch {
            runCatching { repository.leaveSession(membership.sessionId) }
            _nearbyStatuses.update { it - membership.sessionId }
            _sessionState.value = SessionUiState.Idle
            _destination.value = Destination.Session
        }
    }

    /** Who is signed in, for the account sheet. [username] is null for a sign-in that predates it
     *  being stored. */
    val account: AccountInfo
        get() = AccountInfo(repository.username, repository.displayName.orEmpty())

    /** Deletes the account for good (after the UI has confirmed). On success this signs out and
     *  lands on the auth screen; on failure the runner stays signed in and [onError] says why. */
    fun deleteAccount(onError: (String) -> Unit) {
        viewModelScope.launch {
            val result = runCatching { repository.deleteAccount() }
            val response = result.getOrNull()
            when {
                response != null && response.isSuccessful -> {
                    LocationTrackingService.stop(getApplication())
                    shareChoices.clear()
                    crew.clear()
                    casual.clear()
                    events.clear()
                    _nearbyStatuses.value = emptyMap()
                    _destination.value = Destination.Auth
                }
                response != null -> onError(response.errorMessageOrStatus())
                else -> onError(result.exceptionOrNull()?.networkMessage() ?: "Network error.")
            }
        }
    }

    fun signOut() {
        val wasCasual = (LocationTrackingService.state.value as? TrackingState.Tracking)?.sessionId == CASUAL_SESSION_ID
        LocationTrackingService.stop(getApplication())
        shareChoices.clear()
        crew.clear()
        casual.clear()
        events.clear()
        _nearbyStatuses.value = emptyMap()
        viewModelScope.launch {
            // Take the person off the map before the token goes; the server would only drop them
            // after its stale timeout otherwise.
            if (wasCasual) runCatching { repository.casualStop() }
            repository.logout()
            _destination.value = Destination.Auth
        }
    }
}

/** The signed-in account as shown in the account sheet. */
data class AccountInfo(val username: String?, val displayName: String)
