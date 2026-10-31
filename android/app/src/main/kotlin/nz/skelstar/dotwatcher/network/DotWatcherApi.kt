package nz.skelstar.dotwatcher.network

import android.annotation.SuppressLint
import android.content.Context
import android.os.Build
import android.provider.Settings
import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.BuildConfig
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query

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
    // Nearby join (see .ai/plans/nearby-join.md): with [nearby] the name is a public title and a position is
    // required, which becomes the first join location. All absent for an ordinary session.
    val nearby: Boolean? = null,
    val nearbyRadiusMeters: Int? = null,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val accuracy: Double? = null,
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
    /** ISO-8601 UTC time the session was created; null from older servers. */
    val createdAt: String? = null,
)

// --- Crew (server: Controllers/CrewController.cs) -----------------------------------
// A crew is mutual and tied to the account. Members are added only by redeeming a one-time code the
// other person sent. The server never returns usernames here: people are shown by nickname, falling
// back to their account initials (displayName).

@Serializable
data class CrewMember(
    val userId: String,
    val nickname: String? = null,
    val displayName: String,
    /** ISO-8601 UTC time the two became crew. */
    val since: String,
)

@Serializable
data class CrewOverview(val crew: List<CrewMember> = emptyList())

/** A one-time code for the caller to send to one person; usable once, for 24 hours. */
@Serializable
data class CrewCode(val code: String, val expiresAt: String)

/** Whose code this is, for the confirm screen. Previewing adds no one. */
@Serializable
data class CrewCodePreview(
    val userId: String,
    val nickname: String? = null,
    val displayName: String,
    val alreadyCrew: Boolean = false,
)

@Serializable
data class ProfileResponse(val nickname: String? = null)

/** A null (or blank) nickname clears it. */
@Serializable
data class UpdateProfileRequest(val nickname: String?)

// --- Casual tracking (server: Controllers/CasualController.cs) ------------------------
// Start tracking without creating or joining a session. See .ai/plans/casual-tracking.md.

@Serializable
data class CasualStartResponse(val sessionId: String, val runId: String)

/** Whether people outside the caller's crew can see them, as an anonymous dot. Off by default. */
@Serializable
data class CasualSettings(
    val visibleToNonCrew: Boolean = false,
    /** People who aren't in the crew don't see the caller until 300 m from where the run began. On by default. */
    val hideStartArea: Boolean = true,
)

/** Fields left null are left alone by the server. */
@Serializable
data class UpdateCasualSettingsRequest(
    val visibleToNonCrew: Boolean? = null,
    val hideStartArea: Boolean? = null,
)

// --- Events near you (server: Controllers/EventsController.cs) --------------------------

/** A public event shown on the start screen. [eyebrow] is the small line above the title. [sessionId] is set
 *  when this is a session the person can join from where they are; null for the placeholder. */
@Serializable
data class NearbyEvent(
    val id: String,
    val eyebrow: String,
    val title: String,
    val subtitle: String,
    val sessionId: String? = null,
)

// --- Nearby join (server: Controllers/SessionsController.cs) ---------------------------

/** Joining a session from the nearby list; the server checks the position against the session's radius. */
@Serializable
data class JoinNearbyRequest(
    val latitude: Double,
    val longitude: Double,
    val accuracy: Double? = null,
    val displayName: String? = null,
)

/** The organiser's Ping: a fresh fix sets the join location; [enabled] and [radiusMeters] change the setting.
 *  Switching off needs no fix. */
@Serializable
data class UpdateNearbyRequest(
    val latitude: Double? = null,
    val longitude: Double? = null,
    val accuracy: Double? = null,
    val enabled: Boolean? = null,
    val radiusMeters: Int? = null,
)

/** A session's nearby setting as the server reports it. No coordinates: they never leave the server. */
@Serializable
data class NearbyStatus(val nearby: Boolean, val radiusMeters: Int)

/** Yourself or someone in your crew, out casually. The same fields as [RunnerPosition] plus
 *  [inSession] (they are in a named session rather than out casually: the lock) and [isSelf]. */
@Serializable
data class CasualPosition(
    val runnerName: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val timestamp: String,
    val isUltraConstrained: Boolean = false,
    val inSession: Boolean = false,
    val isSelf: Boolean = false,
    /** The account's display name (its initials), for the letters on the dot. [runnerName] can be a crew
     *  nickname, which would be cut down to its first two letters. Absent from older servers. */
    val displayName: String? = null,
)

/** Someone who is not in your crew: a position and nothing else, so there is no name or id to show. */
@Serializable
data class NearbyDot(
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val inSession: Boolean = false,
)

/** A secret link that lets someone with no account or app watch the caller's current run on the web.
 *  [path] is relative to the web site (`/watch/{token}`); the full link is the web base URL plus it. */
@Serializable
data class CasualWatchLinkResponse(val token: String, val path: String)

@Serializable
data class CasualLiveResponse(
    val participants: List<String> = emptyList(),
    val positions: List<List<CasualPosition>> = emptyList(),
    val nearby: List<NearbyDot> = emptyList(),
)

@Serializable
data class LocationUpdate(
    val runnerName: String? = null,
    val sessionId: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val timestamp: String,
    /** 0-100 battery percentage at capture time; null when unknown. Server persists it. */
    val batteryLevel: Int? = null,
)

@Serializable
data class RunnerPosition(
    val runnerName: String,
    val latitude: Double,
    val longitude: Double,
    val heading: Double? = null,
    val timestamp: String,
    /** Self-reported by the runner's device; drives the satellite badge on their marker. Absent
     *  from older servers/clients, hence the default. */
    val isUltraConstrained: Boolean = false,
    /** The account's display name, for the letters on the dot when [runnerName] is a crew nickname.
     *  Only the casual map sets it; null means draw the letters from [runnerName]. */
    val displayName: String? = null,
)

/** A crash saved by [nz.skelstar.dotwatcher.crash.CrashReporter] and uploaded on the next launch.
 *  Matches the server's `ClientErrorReport`. */
@Serializable
data class ClientErrorReport(
    val stackTrace: String,
    val threadName: String? = null,
    val occurredAt: String? = null,
    val occurrences: Int? = null,
    val appVersion: String? = null,
    val osVersion: String? = null,
    val deviceModel: String? = null,
)

@Serializable
data class LocationPostResponse(
    val participants: List<String> = emptyList(),
    val positions: List<List<RunnerPosition>> = emptyList(),
)

/** Shape of every error response body across the API — a bare `{ "error": "..." }`, per every
 *  BadRequest/Conflict/etc. call in the server's controllers (server/Controllers). Not a
 *  server-declared contract type (those controllers return anonymous objects), just this
 *  client's model of the convention. */
@Serializable
data class ApiErrorResponse(
    val error: String? = null,
    /** Only on the 409 for "you already have an active session": that session's id. */
    val sessionId: String? = null,
)

/**
 * The failed response's body as [ApiErrorResponse], or null if it isn't that shape (e.g. a proxy's HTML
 * error page, or a genuinely empty body). A response body can be read once, so a caller that needs more
 * than the message reads it here and keeps the result.
 */
fun Response<*>.parsedError(): ApiErrorResponse? =
    errorBody()?.string()?.let {
        runCatching { Json { ignoreUnknownKeys = true }.decodeFromString<ApiErrorResponse>(it) }.getOrNull()
    }

/**
 * Best-effort extraction of the server's own error message from a failed response, falling back
 * to the HTTP status code alone if the body isn't the expected shape — better UX than always
 * showing a bare status code, without assuming the body is well-formed.
 */
fun Response<*>.errorMessageOrStatus(): String = parsedError()?.error ?: "Request failed (${code()})."

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

    /** Unauthenticated: a crash can happen before anyone has signed in. */
    @POST("client-errors")
    suspend fun postClientError(
        @Body report: ClientErrorReport,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

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

    /** Permanently deletes the caller's account and the sessions they own — matches iOS's
     *  delete-account action in AuthSheet.swift. */
    @DELETE("me")
    suspend fun deleteAccount(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    /** Removes the caller's own membership from a session — matches iOS's
     *  LocationManager.swift `leaveSession(sessionId:)`. */
    @DELETE("me/sessions/{sessionId}/membership")
    suspend fun leaveSession(
        @Path("sessionId") sessionId: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

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

    @GET("me/crew")
    suspend fun getCrew(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CrewOverview>

    @DELETE("me/crew/{userId}")
    suspend fun removeCrewMember(
        @Path("userId") userId: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    @POST("me/crew/codes")
    suspend fun createCrewCode(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CrewCode>

    @GET("me/crew/codes")
    suspend fun getCrewCodes(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<List<CrewCode>>

    @DELETE("me/crew/codes/{code}")
    suspend fun revokeCrewCode(
        @Path("code") code: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    @GET("me/crew/codes/{code}")
    suspend fun previewCrewCode(
        @Path("code") code: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CrewCodePreview>

    @POST("me/crew/codes/{code}/redeem")
    suspend fun redeemCrewCode(
        @Path("code") code: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CrewMember>

    @POST("casual/start")
    suspend fun casualStart(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CasualStartResponse>

    @POST("casual/stop")
    suspend fun casualStop(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    /** 409 when the caller isn't out on a run; 404 on a server (or account) without casual. */
    @POST("casual/watch-link")
    suspend fun createCasualWatchLink(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CasualWatchLinkResponse>

    @DELETE("casual/watch-link")
    suspend fun revokeCasualWatchLinks(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    @GET("casual/live")
    suspend fun casualLive(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CasualLiveResponse>

    /** 404 on a server without events, or for an account that hasn't been given the feature. With [lat]
     *  and [lon] the server lists the runs the person can join from there; without them it sends its
     *  placeholder. 429 after too many lookups. */
    @GET("me/events/nearby")
    suspend fun getNearbyEvents(
        @Header("Authorization") bearerToken: String,
        @Query("lat") lat: Double? = null,
        @Query("lon") lon: Double? = null,
        @Query("accuracy") accuracy: Double? = null,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<List<NearbyEvent>>

    /** Joins a session from the nearby list as a runner. Every refusal is the same 404. */
    @POST("nearby/sessions/{sessionId}/join")
    suspend fun joinNearbySession(
        @Path("sessionId") sessionId: String,
        @Header("Authorization") bearerToken: String,
        @Body request: JoinNearbyRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<SessionMembership>

    /** Owner only: set the join location and/or change the nearby switch and range. */
    @PUT("sessions/{sessionId}/nearby")
    suspend fun updateNearby(
        @Path("sessionId") sessionId: String,
        @Header("Authorization") bearerToken: String,
        @Body request: UpdateNearbyRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<NearbyStatus>

    /** Owner only: ends the session for everyone. Replay stays. */
    @POST("sessions/{sessionId}/end")
    suspend fun endSession(
        @Path("sessionId") sessionId: String,
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<Unit>

    /** 404 on a server without casual, or for an account that hasn't been given it. */
    @GET("me/casual/settings")
    suspend fun getCasualSettings(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CasualSettings>

    @PUT("me/casual/settings")
    suspend fun updateCasualSettings(
        @Header("Authorization") bearerToken: String,
        @Body request: UpdateCasualSettingsRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<CasualSettings>

    @GET("me/profile")
    suspend fun getProfile(
        @Header("Authorization") bearerToken: String,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<ProfileResponse>

    @PUT("me/profile")
    suspend fun updateProfile(
        @Header("Authorization") bearerToken: String,
        @Body request: UpdateProfileRequest,
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<ProfileResponse>
}

/** Flags [UpdateRequiredState] on any `426 Upgrade Required` response, regardless of which
 *  endpoint triggered it — an application interceptor sees every request/response, mirroring
 *  iOS's single check inside its shared low-level request-sending function. */
private class UpdateRequiredInterceptor : okhttp3.Interceptor {
    override fun intercept(chain: okhttp3.Interceptor.Chain): okhttp3.Response {
        val response = chain.proceed(chain.request())
        if (response.code == 426) UpdateRequiredState.markUpdateRequired()
        return response
    }
}

/** Adds the device-identification headers iOS sends on every request (`X-Device-Name`,
 *  `X-Device-Id`, `X-Client-Id`), which the server's request log turns into `DeviceName` /
 *  `DeviceId` / `ClientId` so Android traffic is as traceable as iOS's. */
private class DeviceHeadersInterceptor(context: Context) : okhttp3.Interceptor {
    private val deviceName: String = run {
        val resolver = context.contentResolver
        // The user-set device name (like an iPhone's name); falls back to the hardware model.
        runCatching { Settings.Global.getString(resolver, Settings.Global.DEVICE_NAME) }.getOrNull()
            ?.takeIf { it.isNotBlank() }
            ?: Build.MODEL
    }

    // Stable per device + app signing key and, like iOS's Keychain id, survives reinstall.
    @SuppressLint("HardwareIds")
    private val deviceId: String = runCatching {
        Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
    }.getOrNull().orEmpty()

    override fun intercept(chain: okhttp3.Interceptor.Chain): okhttp3.Response {
        val request = chain.request().newBuilder()
            .header("X-Device-Name", asciiHeaderValue(deviceName))
            .header("X-Client-Id", "android")
            .header("X-OS-Version", "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
            .header("X-App-Version", "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})")
            .apply { if (deviceId.isNotEmpty()) header("X-Device-Id", deviceId) }
            .build()
        return chain.proceed(request)
    }
}

/** HTTP header values must be plain ASCII: OkHttp throws on anything else, and a phone named
 *  "Sarah’s Galaxy" (curly apostrophe) would otherwise fail every request. Replaces what can't be
 *  sent with "?" and drops control characters. */
private fun asciiHeaderValue(value: String): String =
    value.map { if (it in ' '..'~') it else '?' }.joinToString("").trim()

object DotWatcherApiClient {
    fun create(baseUrl: String, context: Context): DotWatcherApi {
        val logging = HttpLoggingInterceptor().apply {
            level = HttpLoggingInterceptor.Level.BASIC
        }
        val okHttpClient = OkHttpClient.Builder()
            .addInterceptor(DeviceHeadersInterceptor(context.applicationContext))
            .addInterceptor(UpdateRequiredInterceptor())
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
