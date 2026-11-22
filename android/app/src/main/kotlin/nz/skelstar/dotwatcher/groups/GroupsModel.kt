package nz.skelstar.dotwatcher.groups

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.SessionMembership

// Groups (see .ai/plans/go-and-groups.md, step 2): for accounts with the "groups" feature, joining a nearby
// session no longer starts tracking in it. You join, stay on the start screen, and press Go as usual; everyone
// in the group then sees you by name on the Go map (the server does that, step 1). Until then nobody does.

/** The server's name for the feature. */
const val GROUPS_FEATURE = "groups"

data class GroupsUiState(
    /** True when the account has the "groups" feature; everything else here is empty without it. */
    val isAvailable: Boolean = false,
    /** The groups the person is in, newest first, as the server lists them. */
    val joined: List<SessionMembership> = emptyList(),
    /** Words for the person when leaving went wrong; null otherwise. */
    val error: String? = null,
)

/** Groups for the signed-in account, owned by [nz.skelstar.dotwatcher.AppViewModel], like crew and events. */
class GroupsModel(
    private val repository: DotWatcherRepository,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow(GroupsUiState())
    val state: StateFlow<GroupsUiState> = _state

    /** Whether this account has groups, from what the server last said. Cheap: no network. */
    val isAvailable: Boolean get() = repository.hasFeature(GROUPS_FEATURE)

    fun refresh() {
        scope.launch { refreshNow() }
    }

    /** Reloads the list. A network failure keeps what is already shown. */
    suspend fun refreshNow() {
        if (!isAvailable) {
            _state.value = GroupsUiState()
            return
        }
        val response = runCatching { repository.getMySessions() }.getOrNull()
        if (response?.isSuccessful == true) {
            _state.update { it.copy(isAvailable = true, joined = response.body().orEmpty()) }
        } else {
            _state.update { it.copy(isAvailable = true) }
        }
    }

    /** Leaves a group. Tracking (if any) carries on: the group just stops seeing the person by name. */
    fun leave(sessionId: String) {
        scope.launch {
            val response = runCatching { repository.leaveSession(sessionId) }.getOrNull()
            if (response?.isSuccessful == true) {
                _state.update { state -> state.copy(joined = state.joined.filterNot { it.sessionId == sessionId }, error = null) }
            } else {
                _state.update { it.copy(error = "Couldn't leave the group. Check your connection and try again.") }
            }
        }
    }

    fun clearError() {
        _state.update { it.copy(error = null) }
    }

    fun clear() {
        _state.value = GroupsUiState()
    }
}
