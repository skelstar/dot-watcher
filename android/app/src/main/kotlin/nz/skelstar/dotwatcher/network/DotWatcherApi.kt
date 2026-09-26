package nz.skelstar.dotwatcher.network

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.kotlinserialization.asConverterFactory
import retrofit2.http.GET
import retrofit2.http.Header

/**
 * Every request from the app must carry X-Api-Version, matching iOS and web (see
 * README.md, "Client compatibility"). The server's current floor is 1
 * (server/appsettings.json's MinimumApiVersion) — bump this only when the app adopts
 * a change that could break against an older server.
 */
private const val API_VERSION = "1"

@Serializable
data class SessionMembership(
    val sessionId: String,
    val sessionName: String,
    val inviteCode: String,
    val role: String,
)

interface DotWatcherApi {
    /**
     * Milestone 0 smoke check only: this is called with no Authorization header, so a
     * 401 response is the *expected*, successful outcome here — it proves the request
     * reached the server, was parsed, and rejected for the right reason (no token yet),
     * rather than proving auth works. Milestone 1 adds real login and reuses this same
     * endpoint authenticated.
     */
    @GET("/me/sessions")
    suspend fun getMySessions(
        @Header("X-Api-Version") apiVersion: String = API_VERSION,
    ): Response<List<SessionMembership>>
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

        val retrofit = Retrofit.Builder()
            .baseUrl(baseUrl)
            .client(okHttpClient)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()

        return retrofit.create(DotWatcherApi::class.java)
    }
}
