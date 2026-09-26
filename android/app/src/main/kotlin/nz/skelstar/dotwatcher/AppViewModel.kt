package nz.skelstar.dotwatcher

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.AuthTokenStore
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import nz.skelstar.dotwatcher.network.SessionMembership
import retrofit2.Response

/** Where the nav graph should route to based on sign-in/session state. */
sealed interface Destination {
    data object Auth : Destination
    data object Session : Destination
    data class Map(val membership: SessionMembership) : Destination
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
 * App-scoped state holder for Milestone 1's single linear flow (auth -> session -> map). Owns
 * the repository so screens don't construct their own network stack. Kept as one ViewModel
 * rather than one per screen while the flow is this small; revisit if Milestone 3's fuller
 * session-list UI needs more independent state.
 */
class AppViewModel(application: Application) : AndroidViewModel(application) {
    private val tokenStore = AuthTokenStore(application)
    private val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL)
    val repository = DotWatcherRepository(api, tokenStore)

    private val _destination = MutableStateFlow<Destination>(
        if (tokenStore.isSignedIn) Destination.Session else Destination.Auth
    )
    val destination: StateFlow<Destination> = _destination

    private val _authState = MutableStateFlow<AuthUiState>(AuthUiState.Idle)
    val authState: StateFlow<AuthUiState> = _authState

    private val _sessionState = MutableStateFlow<SessionUiState>(SessionUiState.Idle)
    val sessionState: StateFlow<SessionUiState> = _sessionState

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
            AuthUiState.Idle
        } else {
            AuthUiState.Error("Sign-in failed (${response.code()}).")
        }
    }

    fun createSession(sessionName: String?, displayName: String?) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.createSession(sessionName?.ifBlank { null }, displayName?.ifBlank { null }) }
            _sessionState.value = handleSessionResult(result)
        }
    }

    fun joinSession(inviteCode: String, displayName: String?) {
        viewModelScope.launch {
            _sessionState.value = SessionUiState.Loading
            val result = runCatching { repository.joinSession(inviteCode, displayName?.ifBlank { null }) }
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
            _destination.value = Destination.Map(membership)
            SessionUiState.Idle
        } else {
            SessionUiState.Error("Could not join/create session (${response.code()}).")
        }
    }

    /** Returns to the create/join screen without signing out — stopping tracking for this
     *  session, not leaving the app. There's no server-side "leave session" call from Milestone 1
     *  yet (that's Milestone 3's session-management UI), so this is purely local navigation. */
    fun returnToSessionPicker() {
        _sessionState.value = SessionUiState.Idle
        _destination.value = Destination.Session
    }

    fun signOut() {
        viewModelScope.launch {
            repository.logout()
            _destination.value = Destination.Auth
        }
    }
}
