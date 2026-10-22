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
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.data.AuthTokenStore
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.data.ShareChoiceStore
import nz.skelstar.dotwatcher.location.LocationTrackingService
import nz.skelstar.dotwatcher.location.TrackingState
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
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
    /** Shown only for role "runner" — asks whether/how-long to share this device's own position
     *  before starting the background tracking service. A "viewer" skips straight to Map, same
     *  as iOS only asking runners (ShareLocationConsentView is only shown when
     *  `canTrackSelectedSession`, i.e. role == "runner"). */
    data class Consent(val membership: SessionMembership) : Destination
    data class Map(val membership: SessionMembership, val isSharing: Boolean) : Destination
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
    val crew = CrewModel(repository, viewModelScope)

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
            _destination.value = when (decision) {
                RestoreDecision.CreateOrJoin -> Destination.Session
                is RestoreDecision.Map -> Destination.Map(decision.membership, decision.isSharing)
                is RestoreDecision.Consent -> Destination.Consent(decision.membership)
                is RestoreDecision.ResumeSharing -> {
                    // They already chose how long to share and it hasn't run out: carry on, don't ask.
                    LocationTrackingService.start(
                        getApplication(),
                        decision.membership.sessionId,
                        decision.remaining,
                        decision.total,
                    )
                    Destination.Map(decision.membership, isSharing = true)
                }
            }
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
            _authState.value = handleAuthResult(response)
        }
    }

    private fun handleAuthResult(result: Result<Response<Unit>>): AuthUiState {
        val response = result.getOrElse {
            return AuthUiState.Error(it.message ?: "Network error.")
        }
        return if (response.isSuccessful) {
            _destination.value = Destination.Session
            // A crew link opened before sign-in is handled now, as session invite links are.
            crew.consumePendingLink()
            AuthUiState.Idle
        } else {
            AuthUiState.Error(response.errorMessageOrStatus())
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
    fun createSession(sessionName: String?) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.createSession(sessionName?.ifBlank { null }, displayName = null) }
            _sessionState.value = handleSessionResult(result)
        }
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
    ): SessionUiState {
        val response = result.getOrElse {
            return SessionUiState.Error(it.message ?: "Network error.")
        }
        val membership = response.body()
        return if (response.isSuccessful && membership != null) {
            _destination.value = if (membership.role == "runner") {
                Destination.Consent(membership)
            } else {
                Destination.Map(membership, isSharing = false)
            }
            SessionUiState.Idle
        } else {
            SessionUiState.Error(response.errorMessageOrStatus())
        }
    }

    fun startSharing(membership: SessionMembership, duration: Duration) {
        LocationTrackingService.start(getApplication(), membership.sessionId, duration)
        _destination.value = Destination.Map(membership, isSharing = true)
    }

    fun declineSharing(membership: SessionMembership) {
        shareChoices.recordViewOnly(membership.sessionId)
        _destination.value = Destination.Map(membership, isSharing = false)
    }

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
                    _destination.value = Destination.Auth
                }
                response != null -> onError(response.errorMessageOrStatus())
                else -> onError(result.exceptionOrNull()?.message ?: "Network error.")
            }
        }
    }

    fun signOut() {
        LocationTrackingService.stop(getApplication())
        shareChoices.clear()
        crew.clear()
        viewModelScope.launch {
            repository.logout()
            _destination.value = Destination.Auth
        }
    }
}

/** The signed-in account as shown in the account sheet. */
data class AccountInfo(val username: String?, val displayName: String)
