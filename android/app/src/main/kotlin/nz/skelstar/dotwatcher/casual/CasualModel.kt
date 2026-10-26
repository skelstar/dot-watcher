package nz.skelstar.dotwatcher.casual

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
import nz.skelstar.dotwatcher.network.networkMessage

/** The full address of a watch link: the web site's base URL joined to the path the server gives. */
fun watchLinkUrl(webBaseUrl: String, path: String): String = webBaseUrl.trimEnd('/') + path

/** Whether this server has casual tracking for this account. Until the server is updated, or while
 *  the account hasn't been given the feature, `/me/casual/settings` is a 404, and the UI then shows
 *  nothing rather than an error. Same approach as crew. */
enum class CasualAvailability { Unknown, Available, Unavailable }

data class CasualUiState(
    val availability: CasualAvailability = CasualAvailability.Unknown,
    /** "Show me to people nearby". Off until the person turns it on, then remembered. */
    val visibleToNonCrew: Boolean = false,
    /** How many of the person's crew are out right now; null until first loaded. */
    val crewOut: Int? = null,
    val error: String? = null,
) {
    val isAvailable: Boolean get() = availability == CasualAvailability.Available
}

/**
 * Casual tracking state for the signed-in account, owned by [nz.skelstar.dotwatcher.AppViewModel]
 * (which supplies the scope and repository). Starting and stopping the tracking service is the
 * ViewModel's job; this holds what the screens show and the calls to the server. See
 * .ai/plans/casual-tracking.md.
 */
class CasualModel(
    private val repository: DotWatcherRepository,
    private val scope: CoroutineScope,
    private val prefs: CasualPrefs,
) {
    private val _state = MutableStateFlow(CasualUiState(visibleToNonCrew = prefs.visibleToNonCrew))
    val state: StateFlow<CasualUiState> = _state

    /** Reloads availability, the setting and the crew-out count. Safe to call often: a network
     *  failure keeps what is already shown. */
    fun refresh() {
        scope.launch { load() }
    }

    private suspend fun load() {
        val settings = runCatching { repository.getCasualSettings() }.getOrNull() ?: return
        when {
            settings.isSuccessful -> {
                val visible = settings.body()?.visibleToNonCrew ?: false
                prefs.visibleToNonCrew = visible
                _state.update { it.copy(availability = CasualAvailability.Available, visibleToNonCrew = visible) }
            }
            // 401 means the session ended and 426 is handled app-wide; neither says anything about
            // whether the server has casual. Anything else (404) hides the feature quietly.
            settings.code() == 401 || settings.code() == 426 -> return
            else -> {
                _state.update { it.copy(availability = CasualAvailability.Unavailable, crewOut = null) }
                return
            }
        }
        val live = runCatching { repository.casualLive() }.getOrNull()
        if (live != null && live.isSuccessful) {
            val crewOut = live.body()?.positions.orEmpty().count { group -> group.firstOrNull()?.isSelf == false }
            _state.update { it.copy(crewOut = crewOut) }
        }
    }

    /** Turns "Show me to people nearby" on or off. Shown at once and remembered on this phone; put
     *  back if the server refuses it. */
    fun setVisibleToNonCrew(visible: Boolean) {
        val before = _state.value.visibleToNonCrew
        prefs.visibleToNonCrew = visible
        _state.update { it.copy(visibleToNonCrew = visible, error = null) }
        scope.launch {
            val result = runCatching { repository.setCasualVisibleToNonCrew(visible) }
            val response = result.getOrNull()
            if (response == null || !response.isSuccessful) {
                prefs.visibleToNonCrew = before
                _state.update {
                    it.copy(
                        visibleToNonCrew = before,
                        error = response?.errorMessageOrStatus()
                            ?: (result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again."),
                    )
                }
            }
        }
    }

    /**
     * Makes a secret link to the current run that anyone can open in a browser, no account or app
     * needed, and returns the full address ([webBaseUrl] plus the path the server gives). Each call
     * makes a new link; every link stops working when the run ends.
     */
    suspend fun createWatchLink(webBaseUrl: String): Result<String> {
        val result = runCatching { repository.createCasualWatchLink() }
        val response = result.getOrNull()
        val link = response?.body()
        return when {
            response != null && response.isSuccessful && link != null ->
                Result.success(watchLinkUrl(webBaseUrl, link.path))
            response != null -> Result.failure(IllegalStateException(response.errorMessageOrStatus()))
            else -> Result.failure(IllegalStateException(result.exceptionOrNull()?.networkMessage() ?: "Network error."))
        }
    }

    /** Ends every watch link for the current run now. True when the server confirmed. */
    suspend fun turnOffWatchLinks(): Boolean =
        runCatching { repository.revokeCasualWatchLinks() }.getOrNull()?.isSuccessful == true

    fun clearError() = _state.update { it.copy(error = null) }

    /** Signing out or deleting the account must not leave this person's choice behind for whoever
     *  signs in next. */
    fun clear() {
        prefs.clear()
        _state.value = CasualUiState()
    }
}

/** The one thing kept on this phone: the "show me to people nearby" choice, so the switch is right
 *  the moment the screen opens. The server holds the real value. Plain SharedPreferences: not
 *  sensitive. */
class CasualPrefs(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var visibleToNonCrew: Boolean
        get() = prefs.getBoolean(KEY_VISIBLE, false)
        set(value) = prefs.edit().putBoolean(KEY_VISIBLE, value).apply()

    fun clear() = prefs.edit().clear().apply()

    private companion object {
        const val PREFS_NAME = "dotwatcher_casual"
        const val KEY_VISIBLE = "visible_to_non_crew"
    }
}
