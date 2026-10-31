package nz.skelstar.dotwatcher.events

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.location.Fix
import nz.skelstar.dotwatcher.network.NearbyEvent

data class EventsUiState(
    /** Public events near the person, nearest first. Empty until loaded, and for an account (or
     *  server) without the events feature: `/me/events/nearby` is a 404, and the UI shows nothing. */
    val events: List<NearbyEvent> = emptyList(),
    /** True once the server has answered for this account, false after a 404. Gates the Create dialog's
     *  nearby switch. */
    val isAvailable: Boolean = false,
) {
    /** The runs the person can join from where they are, as opposed to the placeholder. */
    val joinable: List<NearbyEvent> get() = events.filter { it.sessionId != null }
}

/** Events near the signed-in account, owned by [nz.skelstar.dotwatcher.AppViewModel]. Same approach
 *  as crew and casual: the server decides who has the feature. With a position the list is the runs
 *  people nearby can join (see .ai/plans/nearby-join.md); without one the server sends its placeholder. */
class EventsModel(
    private val repository: DotWatcherRepository,
    private val scope: CoroutineScope,
    /** One current position, or null when location isn't allowed or none arrives. */
    private val currentFix: suspend () -> Fix? = { null },
) {
    private val _state = MutableStateFlow(EventsUiState())
    val state: StateFlow<EventsUiState> = _state

    /** Reloads the list. Safe to call often: a network failure keeps what is already shown. */
    fun refresh() {
        scope.launch { refreshNow() }
    }

    /** [refresh], but returns when the list has loaded, so a pull-to-refresh spinner can end with it. */
    suspend fun refreshNow() {
        val fix = currentFix()
        val response = runCatching { repository.getNearbyEvents(fix) }.getOrNull() ?: return
        when {
            response.isSuccessful ->
                _state.update { it.copy(events = response.body().orEmpty(), isAvailable = true) }
            // 401 means the session ended, 426 is handled app-wide and 429 means slow down; none says
            // anything about whether the account has events. Anything else (404) hides the sign quietly.
            response.code() == 401 || response.code() == 426 || response.code() == 429 -> Unit
            else -> _state.update { it.copy(events = emptyList(), isAvailable = false) }
        }
    }

    fun clear() {
        _state.value = EventsUiState()
    }
}
