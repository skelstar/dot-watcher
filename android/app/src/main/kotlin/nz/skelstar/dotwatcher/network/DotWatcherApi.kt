package nz.skelstar.dotwatcher.network

import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.Path

/**
 * Every request from the app must carry X-Api-Version, matching iOS and web (see
 * repo root README.md, "Client compatibility"). The server's current floor is 1
 * (server/appsettings.json's MinimumApiVersion) — bump this only when the app adopts
 * a change that could break against an older server.
 */
private const val API_VERSION = "1"

// --- Wire models -------------------------------------------------------------------
// Field names below are load-bearing: they must match the server's camelCase JSON exactly
// (see tests/DotWatcher.Server.Tests/ContractTests.cs), not just the Kotlin naming convention.

@Serializable
data class RegisterRequest(
    val username: String,
    val password: String,
    val displayName: String,
)

@Serializable
data class LoginRequest(
    val username: String,
    val password: String,
)

@Serializable
data class AuthenticatedUser(
    val userId: String,
    val username: String,
    val displayName: String,
)

@Serializable
data class AuthResponse(
    val accessToken: String,
    val expiresAt: String,
    val user: AuthenticatedUser,
)

@Serializable
data class CreateSessionRequest(
    val sessionName: String? = null,
    val displayName: String? = null,
    val maxLengthHours: Int? = null,
)

@Serializable
data class JoinSessionRequest(
    val displayName: String? = null,
    val role: String? = null,
)

@Serializable
data class SessionMembership(
    val sessionId: String,
    val sessionName: String,
    val inviteCode: String,
    val role: String,
    val displayName: String,
    val ownerDisplayName: String? = null,
)

@Serializable
data class LocationUpdate(
    val runnerName: String? = null,
    val sessionId: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val timestamp: String,
)

@Serializable
data class RunnerPosition(
    val runnerName: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val timestamp: String,
)

@Serializable
data class LocationPostResponse(
    val participants: List<String> = emptyList(),
    val positions: List<List<RunnerPosition>> = emptyList(),
)

/** Shape of every error response body across the API — a bare `{ "error": "..." }`, per every
 *  BadRequest/Conflict/etc. call in server/Controllers/*.cs. Not a server-declared contract type
 *  (those controllers return anonymous objects), just this client's model of the convention. */
@Serializable
data class ApiErrorResponse(val error: String? = null)

/**
 * Best-effort extraction of the server's own error message from a failed response, falling back
 * to the HTTP status code alone if the body isn't the expected shape (e.g. a proxy's HTML error
 * page, or a genuinely empty body) — better UX than always showing a bare status code, without
 * assuming the body is well-formed.
 */
fun Response<*>.errorMessageOrStatus(): String {
    val bodyText = errorBody()?.string()
    val parsedMessage = bodyText?.let {
        runCatching { Json { ignoreUnknownKeys = true }.decodeFromString<ApiErrorResponse>(it) }
            .getOrNull()
            ?.error
    }
    return parsedMessage ?: "Request failed (${code()})."
}

// Paths below are deliberately relative (no leading "/"): Retrofit resolves them against
// baseUrl with normal URL-relative semantics, and a leading "/" would resolve against the
// server root, silently dropping any path prefix in baseUrl (production's is ".../api" — see
// app/build.gradle.kts's API_BASE_URL). See DotWatcherApiClient.create for the matching
// trailing-"/" requirement on baseUrl itself.
interface DotWatcherApi {
    @POST("auth/register")
    suspend fun register(
        @Body request: RegisterRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<AuthResponse>

    @POST("auth/login")
    suspend fun login(
        @Body request: LoginRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<AuthResponse>

    @POST("auth/logout")
    suspend fun logout(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    @POST("sessions")
    suspend fun createSession(
        @Header("Authorization") bearerToken: String,
        @Body request: CreateSessionRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<SessionMembership>

    @POST("session-invites/{inviteCode}/join")
    suspend fun joinSession(
        @Path("inviteCode") inviteCode: String,
        @Header("Authorization") bearerToken: String,
        @Body request: JoinSessionRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<SessionMembership>

    @GET("me/sessions")
    suspend fun getMySessions(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<List<SessionMembership>>

    @POST("location")
    suspend fun postLocation(
        @Header("Authorization") bearerToken: String,
        @Body update: LocationUpdate,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<LocationPostResponse>

    @GET("locations/{sessionId}")
    suspend fun getLatestPositions(
        @Path("sessionId") sessionId: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<List<List<RunnerPosition>>>
}

object DotWatcherApiClient {
    fun create(baseUrl: String): DotWatcherApi {
        val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BASIC
        }
        val okHttpClient = OkHttpClient.Builder()
            .addInterceptor(logging)
            .build()

        val json = Json { ignoreUnknownKeys = true }

        // Retrofit requires a trailing "/" on baseUrl to resolve relative @GET/@POST paths
        // correctly (see interface note above) — normalize here so callers can pass either form.
        val normalizedBaseUrl = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"

        val retrofit = Retrofit.Builder()
            .baseUrl(normalizedBaseUrl)
            .client(okHttpClient)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()

        return retrofit.create(DotWatcherApi::class.java)
    }
}

/** Formats a bearer token the way every `Authorization` header on protected calls expects it. */
fun bearer(token: String): String = "Bearer $token"
