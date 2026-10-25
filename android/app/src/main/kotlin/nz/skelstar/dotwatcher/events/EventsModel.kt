package nz.skelstar.dotwatcher.events

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.NearbyEvent

data class EventsUiState(
    /** Public events near the person, nearest first. Empty until loaded, and for an account (or
     *  server) without the events feature: `/me/events/nearby` is a 404, and the UI shows nothing. */
    val events: List<NearbyEvent> = emptyList(),
)

/** Events near the signed-in account, owned by [nz.skelstar.dotwatcher.AppViewModel]. Same approach
 *  as crew and casual: the server decides who has the feature. */
class EventsModel(
    private val repository: DotWatcherRepository,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow(EventsUiState())
    val state: StateFlow<EventsUiState> = _state

    /** Reloads the list. Safe to call often: a network failure keeps what is already shown. */
    fun refresh() {
        scope.launch {
            val response = runCatching { repository.getNearbyEvents() }.getOrNull() ?: return@launch
            when {
                response.isSuccessful -> _state.update { it.copy(events = response.body().orEmpty()) }
                // 401 means the session ended and 426 is handled app-wide; neither says anything about
                // whether the account has events. Anything else (404) hides the sign quietly.
                response.code() == 401 || response.code() == 426 -> Unit
                else -> _state.update { it.copy(events = emptyList()) }
            }
        }
    }
}
