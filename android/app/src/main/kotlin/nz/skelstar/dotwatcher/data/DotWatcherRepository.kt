package nz.skelstar.dotwatcher.data

import nz.skelstar.dotwatcher.network.CreateSessionRequest
import nz.skelstar.dotwatcher.network.DotWatcherApi
import nz.skelstar.dotwatcher.network.JoinSessionRequest
import nz.skelstar.dotwatcher.network.LocationPostResponse
import nz.skelstar.dotwatcher.network.LocationUpdate
import nz.skelstar.dotwatcher.network.LoginRequest
import nz.skelstar.dotwatcher.network.RegisterRequest
import nz.skelstar.dotwatcher.network.RunnerPosition
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.network.bearer
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.ResponseBody.Companion.toResponseBody
import retrofit2.Response

/**
 * Thin wrapper around [DotWatcherApi] that fills in the bearer token from [AuthTokenStore] on
 * every authenticated call, so screens/ViewModels never handle the header directly.
 */
class DotWatcherRepository(
    private val api: DotWatcherApi,
    private val tokenStore: AuthTokenStore,
) {
    suspend fun register(username: String, password: String, displayName: String): Response<Unit> {
        val response = api.register(RegisterRequest(username, password, displayName))
        response.body()?.let { auth ->
            tokenStore.accessToken = auth.accessToken
            tokenStore.userId = auth.user.userId
            tokenStore.displayName = auth.user.displayName
        }
        return stripBody(response)
    }

    suspend fun login(username: String, password: String): Response<Unit> {
        val response = api.login(LoginRequest(username, password))
        response.body()?.let { auth ->
            tokenStore.accessToken = auth.accessToken
            tokenStore.userId = auth.user.userId
            tokenStore.displayName = auth.user.displayName
        }
        return stripBody(response)
    }

    suspend fun logout() {
        val token = tokenStore.accessToken
        if (token != null) {
            runCatching { api.logout(bearer(token)) }
        }
        tokenStore.clear()
    }

    suspend fun createSession(sessionName: String?, displayName: String?): Response<SessionMembership> {
        val token = requireToken()
        return api.createSession(bearer(token), CreateSessionRequest(sessionName, displayName))
    }

    suspend fun joinSession(
        inviteCode: String,
        displayName: String?,
        role: String = "runner",
    ): Response<SessionMembership> {
        val token = requireToken()
        return api.joinSession(inviteCode, bearer(token), JoinSessionRequest(displayName, role))
    }

    suspend fun postLocation(
        sessionId: String,
        latitude: Double,
        longitude: Double,
        heading: Double?,
        timestamp: String,
    ): Response<LocationPostResponse> {
        val token = requireToken()
        val update = LocationUpdate(
            sessionId = sessionId,
            latitude = latitude,
            longitude = longitude,
            heading = heading,
            timestamp = timestamp,
        )
        return api.postLocation(bearer(token), update)
    }

    suspend fun getLatestPositions(sessionId: String): Response<List<List<RunnerPosition>>> {
        val token = requireToken()
        return api.getLatestPositions(sessionId, bearer(token))
    }

    private fun requireToken(): String =
        tokenStore.accessToken ?: error("No access token — caller must be signed in.")

    /** Auth responses carry the token/user payload we've already consumed into [tokenStore]. */
    private fun <T> stripBody(response: Response<T>): Response<Unit> {
        if (response.isSuccessful) return Response.success(Unit)
        val errorBody = response.errorBody()
            ?: "".toResponseBody("text/plain".toMediaType())
        return Response.error(errorBody, response.raw())
    }
}
