package nz.skelstar.dotwatcher.groups

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.Flow
import nz.skelstar.dotwatcher.location.Fix
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.nearby.NEARBY_DEFAULT_RADIUS_METRES
import nz.skelstar.dotwatcher.nearby.EntryCodeActions
import nz.skelstar.dotwatcher.network.SessionMembership

// Groups (see .ai/plans/go-and-groups.md, step 2): for accounts with the "groups" feature, joining a nearby
// session no longer starts tracking in it. You join, stay on the start screen, and press Go as usual; everyone
// in the group then sees you by name on the Go map (the server does that, step 1). Until then nobody does.

/** The server's name for the feature. */
const val GROUPS_FEATURE = "groups"

/** Who may organise a group: with groups, creating a session makes a group, and that needs this too. */
const val CREATE_GROUPS_FEATURE = "create-groups"

/** The line above "Go" on the start screen while the person is in any groups: the newest by name, and how many
 *  more. Null when they're in none. Matches iOS's `goSignEyebrow`. */
fun goSignEyebrow(groups: List<SessionMembership>): String? {
    val newest = groups.firstOrNull() ?: return null
    return if (groups.size > 1) "With ${newest.sessionName} + ${groups.size - 1} more" else "With ${newest.sessionName}"
}

/** What the organiser can do from their own group's card on the start screen (the session map isn't used for a
 *  group). Each returns words for the person, or null on success for [end]. */
class GroupOrganiserActions(
    /** Moves the join location to here; null when the group isn't shown nearby. */
    val ping: (suspend () -> String)?,
    /** The entry code; null when the group isn't shown nearby (codes only apply to nearby joins). */
    val entryCode: EntryCodeActions?,
    /** Ends the group for everyone. */
    val end: suspend () -> String?,
    /** Moves the join location to a point chosen on the map (the crosshair), not where the phone is; null when
     *  the group isn't shown nearby. Null when it's set, otherwise words for what went wrong. */
    val setJoinLocationAt: (suspend (latitude: Double, longitude: Double) -> String?)? = null,
    /** The join location as this phone knows it (set from here, or created here); null when it doesn't. Where
     *  "Choose on map" opens. */
    val joinLocation: Fix? = null,
    /** The group's join range, for the ring around the crosshair. */
    val radiusMeters: Int = NEARBY_DEFAULT_RADIUS_METRES,
    /** The phone's position now, for "Choose on map" when [joinLocation] is unknown. */
    val currentPosition: suspend () -> Fix? = { null },
    /** The join area's size; null when the group isn't shown nearby. */
    val area: GroupAreaActions? = null,
)

/** The organiser changes how big their group's join area is. The join location stays where it is. */
class GroupAreaActions(
    /** The size now, in metres, as the server last said. */
    val radiusMeters: Flow<Int>,
    /** Where the join location is, when this phone set it (by creating the group or Ping here); null otherwise. */
    val joinLocation: Flow<Fix?>,
    /** Sets a new size. Returns words for the person. */
    val change: suspend (radiusMeters: Int) -> String,
)

data class GroupsUiState(
    /** True when the account has the "groups" feature; everything else here is empty without it. */
    val isAvailable: Boolean = false,
    /** The groups the person is in, newest first, as the server lists them. */
    val joined: List<SessionMembership> = emptyList(),
    /** Whether the start screen offers Create. With groups it makes a group, which needs "create-groups" too
     *  (the server refuses otherwise); without groups anyone can create a session, as before. */
    val canCreate: Boolean = true,
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

    /** Whether this account may create a group (see [GroupsUiState.canCreate]). Cheap: no network. */
    val canCreate: Boolean get() = !isAvailable || repository.hasFeature(CREATE_GROUPS_FEATURE)

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
            _state.update { it.copy(isAvailable = true, canCreate = canCreate, joined = response.body().orEmpty()) }
        } else {
            _state.update { it.copy(isAvailable = true, canCreate = canCreate) }
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
