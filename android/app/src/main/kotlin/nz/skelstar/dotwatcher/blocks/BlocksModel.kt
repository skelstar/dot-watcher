package nz.skelstar.dotwatcher.blocks

import nz.skelstar.dotwatcher.data.DotWatcherRepository
import nz.skelstar.dotwatcher.network.BlockedUser
import nz.skelstar.dotwatcher.network.errorMessageOrStatus
import nz.skelstar.dotwatcher.network.networkMessage

/**
 * The blocked-users list in Settings: load it, and unblock from it. Blocking itself starts from the
 * session's people list, which has the user ids. The server keeps the list, so nothing is held here and
 * every failure is a message for the screen to show.
 */
class BlocksModel(private val repository: DotWatcherRepository) {
    /** Most recently blocked first. */
    suspend fun load(): Result<List<BlockedUser>> {
        val result = runCatching { repository.getBlockedUsers() }
        val response = result.getOrNull()
            ?: return Result.failure(Exception(result.exceptionOrNull()?.networkMessage() ?: "Network error."))
        if (!response.isSuccessful) return Result.failure(Exception(response.errorMessageOrStatus()))
        return Result.success(response.body().orEmpty().sortedByDescending { it.blockedAt })
    }

    suspend fun unblock(userId: String): Result<Unit> {
        val result = runCatching { repository.unblockUser(userId) }
        val response = result.getOrNull()
            ?: return Result.failure(Exception(result.exceptionOrNull()?.networkMessage() ?: "Network error."))
        return if (response.isSuccessful) Result.success(Unit) else Result.failure(Exception(response.errorMessageOrStatus()))
    }
}
