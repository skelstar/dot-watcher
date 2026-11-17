package nz.skelstar.dotwatcher.crew

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.CrewCode
import nz.skelstar.dotwatcher.location.Fix
import nz.skelstar.dotwatcher.network.CrewCodePreview
import nz.skelstar.dotwatcher.network.CrewFindingState
import nz.skelstar.dotwatcher.network.CrewMember
import nz.skelstar.dotwatcher.network.CrewNearbyPerson
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
import nz.skelstar.dotwatcher.network.networkMessage
import retrofit2.Response
import java.time.OffsetDateTime

/** The crew members this phone has already shown the person, so the start screen can say who is new.
 *  Null until the first load, which becomes the baseline. Plain SharedPreferences: not sensitive. */
class CrewPrefs(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var seenMemberIds: Set<String>?
        get() = prefs.getStringSet(KEY_SEEN, null)?.toSet()
        set(value) = prefs.edit().putStringSet(KEY_SEEN, value).apply()

    fun clear() = prefs.edit().clear().apply()

    private companion object {
        const val PREFS_NAME = "dotwatcher_crew"
        const val KEY_SEEN = "seen_member_ids"
    }
}

/** Whether this server has crew for this account. Until the server is updated, or while the account
 *  hasn't been given the crew feature, `/me/crew` is a 404, and the UI then shows nothing rather
 *  than an error. */
enum class CrewAvailability { Unknown, Available, Unavailable }

/** The "Add Priya to your crew?" confirmation, and what follows it. A link or a typed code leads to
 *  [Loading] (previewing the code), then [Ready]; nobody is added until the person taps Add. */
sealed interface CrewConfirm {
    val code: String

    data class Loading(override val code: String) : CrewConfirm
    data class Ready(override val code: String, val owner: CrewCodePreview) : CrewConfirm
    data class Adding(override val code: String, val owner: CrewCodePreview) : CrewConfirm
    data class Done(override val code: String, val member: CrewMember) : CrewConfirm
    data class Failed(override val code: String, val message: String) : CrewConfirm
}

/** "Find new crew": adding people standing next to you, without a code. Lasts two minutes and stops when the
 *  crew sheet closes. See .ai/plans/crew-finding.md. */
data class CrewFindingUi(
    val isOn: Boolean = false,
    val isStarting: Boolean = false,
    /** When the window closes by itself (epoch millis). */
    val expiresAtMillis: Long? = null,
    /** People who have accepted this person come first, so they are easy to spot. */
    val people: List<CrewNearbyPerson> = emptyList(),
    /** A line under the switch: why it couldn't start, or that it turned itself off. */
    val note: String? = null,
    /** Location is off for the app, so the note offers Settings. */
    val needsSettings: Boolean = false,
    val acceptingId: String? = null,
    val acceptError: String? = null,
    /** Someone who just became crew this way, for a short confirmation. */
    val justAdded: CrewMember? = null,
)

data class CrewUiState(
    val availability: CrewAvailability = CrewAvailability.Unknown,
    val members: List<CrewMember> = emptyList(),
    /** The name this account's crew sees; null until chosen (they then see the initials). */
    val nickname: String? = null,
    val nicknameError: String? = null,
    /** The code the person is currently offering, if any. */
    val myCode: CrewCode? = null,
    val myCodeLoading: Boolean = false,
    val myCodeError: String? = null,
    val removeError: String? = null,
    val confirm: CrewConfirm? = null,
    /** People who joined since the crew sheet was last opened (someone redeemed a code of ours). */
    val newMemberIds: Set<String> = emptySet(),
    /** Codes we have sent out that nobody has used yet. */
    val pendingInvites: Int = 0,
    val finding: CrewFindingUi = CrewFindingUi(),
) {
    val isAvailable: Boolean get() = availability == CrewAvailability.Available
}

/**
 * Crew state and actions for the signed-in account, owned by [nz.skelstar.dotwatcher.AppViewModel]
 * (which supplies the scope and repository) so the screens share one network stack. A crew is mutual
 * and per account; people join it only by redeeming a one-time code the other person sent them. See
 * .ai/plans/crew-management.md.
 */
class CrewModel(
    private val repository: DotWatcherRepository,
    private val scope: CoroutineScope,
    private val prefs: CrewPrefs,
    /** One current position for "Find new crew", or null without permission or when none arrives. */
    private val currentFix: suspend () -> Fix? = { null },
    private val hasLocationPermission: () -> Boolean = { false },
) {
    private val _state = MutableStateFlow(CrewUiState())
    val state: StateFlow<CrewUiState> = _state

    /** A code that arrived (as a link) before anyone was signed in; handled once they are. */
    private var pendingLinkCode: String? = null

    /** Loads the crew and the person's own nickname. Safe to call often (on resume, on the refresh
     *  button): a network failure keeps what is already shown. */
    fun refresh() {
        scope.launch { load() }
    }

    private suspend fun load() {
        val crew = runCatching { repository.getCrew() }.getOrNull() ?: return
        when {
            crew.isSuccessful -> {
                val members = crew.body()?.crew.orEmpty()
                // The first load on this phone is the baseline: everyone already there counts as seen.
                val seen = prefs.seenMemberIds ?: members.map { it.userId }.toSet().also { prefs.seenMemberIds = it }
                val newIds = members.map { it.userId }.filterNot { it in seen }.toSet()
                _state.update {
                    it.copy(availability = CrewAvailability.Available, members = members, newMemberIds = newIds)
                }
                val codes = runCatching { repository.getCrewCodes() }.getOrNull()
                    ?.takeIf { it.isSuccessful }?.body()
                if (codes != null) _state.update { it.copy(pendingInvites = codes.size) }
            }
            // 401 means the session ended and 426 is handled app-wide; neither says anything
            // about whether the server has crew. Anything else (404 from a server without it, or
            // from an account that hasn't been given the crew feature) hides the feature quietly.
            crew.code() == 401 || crew.code() == 426 -> Unit
            else -> _state.update { it.copy(availability = CrewAvailability.Unavailable, members = emptyList()) }
        }
        if (crew.isSuccessful) {
            val profile = runCatching { repository.getProfile() }.getOrNull()
            if (profile != null && profile.isSuccessful) {
                _state.update { it.copy(nickname = profile.body()?.nickname) }
            }
        }
    }

    /** Forgets everything: signing out or deleting the account must not leave the last person's
     *  crew behind for whoever signs in next. */
    fun clear() {
        stopFinding()
        pendingLinkCode = null
        prefs.clear()
        _state.value = CrewUiState()
    }

    /** The crew sheet was opened: whoever joined has now been seen. */
    fun markMembersSeen() {
        val ids = _state.value.members.map { it.userId }
        prefs.seenMemberIds = (prefs.seenMemberIds.orEmpty()) + ids
        _state.update { it.copy(newMemberIds = emptySet()) }
    }

    fun setNickname(raw: String, onSaved: () -> Unit = {}) {
        if (!isValidNickname(raw)) {
            _state.update { it.copy(nicknameError = "Use up to $MAX_NICKNAME_LENGTH characters, with no line breaks.") }
            return
        }
        val nickname = cleanNickname(raw)
        scope.launch {
            val result = runCatching { repository.updateNickname(nickname) }
            val response = result.getOrNull()
            when {
                response != null && response.isSuccessful -> {
                    _state.update { it.copy(nickname = response.body()?.nickname, nicknameError = null) }
                    onSaved()
                }
                response != null -> _state.update { it.copy(nicknameError = response.errorMessageOrStatus()) }
                else -> _state.update {
                    it.copy(nicknameError = result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again.")
                }
            }
        }
    }

    fun clearNicknameError() = _state.update { it.copy(nicknameError = null) }

    // --- My code ------------------------------------------------------------------------

    /** Shows the code already on offer, or makes one, so reopening the sheet doesn't burn a new
     *  code each time. */
    fun loadMyCode() {
        if (_state.value.myCodeLoading) return
        _state.update { it.copy(myCodeLoading = true, myCodeError = null) }
        scope.launch {
            val existing = runCatching { repository.getCrewCodes() }.getOrNull()
                ?.takeIf { it.isSuccessful }?.body()?.firstOrNull()
            if (existing != null) {
                _state.update { it.copy(myCode = existing, myCodeLoading = false) }
            } else {
                makeCode()
            }
        }
    }

    /** "Make a new code": the old one stops working, so whoever has it can no longer use it. */
    fun newMyCode() {
        if (_state.value.myCodeLoading) return
        val old = _state.value.myCode
        _state.update { it.copy(myCodeLoading = true, myCodeError = null) }
        scope.launch {
            if (old != null) runCatching { repository.revokeCrewCode(old.code) }
            makeCode()
        }
    }

    private suspend fun makeCode() {
        val result = runCatching { repository.createCrewCode() }
        val response = result.getOrNull()
        _state.update {
            when {
                response != null && response.isSuccessful ->
                    it.copy(myCode = response.body(), myCodeLoading = false, myCodeError = null)
                response != null ->
                    it.copy(myCodeLoading = false, myCodeError = response.errorMessageOrStatus())
                else -> it.copy(
                    myCodeLoading = false,
                    myCodeError = result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again.",
                )
            }
        }
        // A new code is a new pending invite for the start screen.
        if (response != null && response.isSuccessful) load()
    }

    // --- Adding someone with a code -----------------------------------------------------

    /** A crew link was opened. If nobody is signed in yet it is kept and handled after sign-in, as
     *  session invite links are. */
    fun onLink(code: String, signedIn: Boolean) {
        if (signedIn) beginAdd(code) else pendingLinkCode = code
    }

    /** Call once the person is signed in (after sign-in or register) to handle a link opened earlier. */
    fun consumePendingLink() {
        pendingLinkCode?.let { code ->
            pendingLinkCode = null
            beginAdd(code)
        }
    }

    /** Looks up whose code this is and asks the person to confirm. Adds no one. */
    fun beginAdd(rawCode: String) {
        val code = CrewLink.normalize(rawCode)
        if (code == null) {
            _state.update {
                it.copy(confirm = CrewConfirm.Failed(rawCode, "That doesn't look like a crew code. It has 8 letters and numbers."))
            }
            return
        }
        _state.update { it.copy(confirm = CrewConfirm.Loading(code)) }
        scope.launch {
            // Crew is switched on per account, and a lookup for an account without it 404s just like
            // a dead code. A link can arrive before the first refresh has answered, so find out first.
            if (_state.value.availability == CrewAvailability.Unknown) load()
            if (_state.value.availability == CrewAvailability.Unavailable) {
                _state.update { it.copy(confirm = CrewConfirm.Failed(code, CREW_NOT_AVAILABLE_MESSAGE)) }
                return@launch
            }
            val result = runCatching { repository.previewCrewCode(code) }
            _state.update { it.copy(confirm = previewOutcome(code, result)) }
        }
    }

    private fun previewOutcome(code: String, result: Result<Response<CrewCodePreview>>): CrewConfirm {
        val response = result.getOrNull()
            ?: return CrewConfirm.Failed(code, result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again.")
        val owner = response.body()
        return if (response.isSuccessful && owner != null) {
            CrewConfirm.Ready(code, owner)
        } else {
            CrewConfirm.Failed(code, crewCodeFailureMessage(response.code()))
        }
    }

    /** The person tapped Add on the confirmation. */
    fun confirmAdd() {
        val ready = _state.value.confirm as? CrewConfirm.Ready ?: return
        _state.update { it.copy(confirm = CrewConfirm.Adding(ready.code, ready.owner)) }
        scope.launch {
            val result = runCatching { repository.redeemCrewCode(ready.code) }
            val response = result.getOrNull()
            val member = response?.body()
            if (response != null && response.isSuccessful && member != null) {
                // Someone we added ourselves isn't news.
                prefs.seenMemberIds = prefs.seenMemberIds.orEmpty() + member.userId
                _state.update {
                    it.copy(
                        confirm = CrewConfirm.Done(ready.code, member),
                        members = listOf(member) + it.members.filterNot { m -> m.userId == member.userId },
                    )
                }
                refresh()
            } else {
                val message = response?.let { crewCodeFailureMessage(it.code()) }
                    ?: (result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again.")
                _state.update { it.copy(confirm = CrewConfirm.Failed(ready.code, message)) }
            }
        }
    }

    fun dismissConfirm() = _state.update { it.copy(confirm = null) }

    // --- Removing someone ---------------------------------------------------------------

    /** Takes effect straight away for both people; the other person isn't told. */
    fun remove(userId: String) {
        scope.launch {
            val result = runCatching { repository.removeCrewMember(userId) }
            val response = result.getOrNull()
            if (response != null && (response.isSuccessful || response.code() == 404)) {
                _state.update { it.copy(members = it.members.filterNot { m -> m.userId == userId }, removeError = null) }
            } else {
                _state.update {
                    it.copy(
                        removeError = response?.errorMessageOrStatus()
                            ?: (result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again."),
                    )
                }
            }
        }
    }

    fun clearRemoveError() = _state.update { it.copy(removeError = null) }

    // --- Find new crew ------------------------------------------------------------------

    private var findingJob: Job? = null
    /** Bumped by every stop, so a start that answers after the person switched off is undone. */
    private var findingGeneration = 0
    private val reportedAdded = mutableSetOf<String>()

    /** The switch. */
    fun setFinding(on: Boolean) = if (on) startFinding() else stopFinding()

    fun startFinding() {
        val current = _state.value.finding
        if (current.isOn || current.isStarting) return
        if (!hasLocationPermission()) {
            updateFinding {
                CrewFindingUi(
                    note = "Location is off for DotWatcher, so people next to you can't find you.",
                    needsSettings = true,
                )
            }
            return
        }
        val generation = ++findingGeneration
        updateFinding { CrewFindingUi(isStarting = true) }
        scope.launch {
            val fix = currentFix()
            if (generation != findingGeneration) return@launch
            if (fix == null) {
                updateFinding { CrewFindingUi(note = "Couldn't get your position. Try again in a moment.") }
                return@launch
            }
            val result = runCatching { repository.startCrewFinding(fix) }
            val response = result.getOrNull()
            val body = response?.body()
            if (generation != findingGeneration) {
                // Switched off while this was on its way: make sure the server agrees.
                if (response?.isSuccessful == true) runCatching { repository.stopCrewFinding() }
                return@launch
            }
            if (response != null && response.isSuccessful && body != null) {
                reportedAdded.clear()
                updateFinding { CrewFindingUi(isOn = true) }
                applyFinding(body)
                findingJob?.cancel()
                findingJob = scope.launch { refreshFindingLoop() }
            } else {
                val note = when (response?.code()) {
                    400 -> "Your position isn't accurate enough yet. Check precise location is on, then try again."
                    429 -> "Too many tries. Wait a few minutes and try again."
                    null -> result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again."
                    else -> "Couldn't turn this on. Try again."
                }
                updateFinding { CrewFindingUi(note = note) }
            }
        }
    }

    /** Off: from the switch, when the crew sheet closes, or when the two minutes are up ([expired]). The
     *  server drops unanswered accepts. */
    fun stopFinding(expired: Boolean = false) {
        findingGeneration++
        findingJob?.cancel()
        findingJob = null
        val current = _state.value.finding
        val wasOn = current.isOn || current.isStarting
        updateFinding {
            CrewFindingUi(note = if (expired) "Turned off after 2 minutes. Turn on again to keep looking." else null)
        }
        if (wasOn) scope.launch { runCatching { repository.stopCrewFinding() } }
    }

    fun acceptNearby(person: CrewNearbyPerson) {
        val current = _state.value.finding
        if (!current.isOn || current.acceptingId != null) return
        updateFinding { it.copy(acceptingId = person.id, acceptError = null) }
        scope.launch {
            val result = runCatching { repository.acceptCrewFinding(person.id) }
            val response = result.getOrNull()
            val body = response?.body()
            updateFinding { it.copy(acceptingId = null) }
            when {
                response != null && response.isSuccessful && body != null -> applyFinding(body)
                response?.code() == 410 -> stopFinding(expired = true)
                response?.code() == 409 -> updateFinding { it.copy(acceptError = "Crew is full, so ${person.label()} can't be added.") }
                response != null -> updateFinding { it.copy(acceptError = "${person.label()} isn't nearby any more.") }
                else -> updateFinding {
                    it.copy(acceptError = result.exceptionOrNull()?.networkMessage() ?: "Network error. Try again.")
                }
            }
        }
    }

    fun clearJustAdded() = updateFinding { it.copy(justAdded = null) }

    private suspend fun refreshFindingLoop() {
        while (true) {
            delay(FINDING_REFRESH_MS)
            val current = _state.value.finding
            if (!current.isOn) return
            val expiresAt = current.expiresAtMillis
            if (expiresAt != null && System.currentTimeMillis() >= expiresAt) {
                stopFinding(expired = true)
                return
            }
            // A missed refresh is fine: the next one follows in a few seconds, until the window ends.
            val fix = currentFix() ?: continue
            val response = runCatching { repository.refreshCrewFinding(fix) }.getOrNull() ?: continue
            if (!_state.value.finding.isOn) return
            when {
                response.code() == 410 -> {
                    stopFinding(expired = true)
                    return
                }
                response.isSuccessful -> response.body()?.let { applyFinding(it) }
            }
        }
    }

    private fun applyFinding(state: CrewFindingState) {
        val expires = runCatching { OffsetDateTime.parse(state.expiresAt).toInstant().toEpochMilli() }.getOrNull()
        val people = state.people.filter { it.acceptedYou } + state.people.filterNot { it.acceptedYou }
        // Oldest first, so the newest ends up as the confirmation.
        val newlyAdded = state.added.reversed().filter { reportedAdded.add(it.userId) }
        if (newlyAdded.isNotEmpty()) {
            // Someone we added ourselves isn't news.
            prefs.seenMemberIds = prefs.seenMemberIds.orEmpty() + newlyAdded.map { it.userId }
        }
        _state.update { s ->
            val members = newlyAdded.fold(s.members) { acc, m -> listOf(m) + acc.filterNot { it.userId == m.userId } }
            s.copy(
                members = members,
                finding = s.finding.copy(
                    expiresAtMillis = expires ?: s.finding.expiresAtMillis
                        ?: (System.currentTimeMillis() + FINDING_WINDOW_MS),
                    people = people,
                    justAdded = newlyAdded.lastOrNull() ?: s.finding.justAdded,
                ),
            )
        }
    }

    private fun updateFinding(change: (CrewFindingUi) -> CrewFindingUi) =
        _state.update { it.copy(finding = change(it.finding)) }

    private companion object {
        /** The server lists someone only while their last fix is under 30 s old. */
        const val FINDING_REFRESH_MS = 4_000L
        const val FINDING_WINDOW_MS = 120_000L
    }
}
