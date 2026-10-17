package nz.skelstar.dotwatcher.crash

import android.content.Context
import android.os.Build
import android.util.Log
import kotlinx.serialization.encodeToString
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.json.Json
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.network.ClientErrorReport
import nz.skelstar.dotwatcher.network.DotWatcherApi
import java.io.File
import java.time.Duration
import java.time.Instant

/**
 * A minimal crash reporter, so a tester's crash reaches Seq as a stack trace instead of as a
 * screenshot of "keeps stopping". Play Console's Android vitals stays empty for a small
 * internal-testing group, which is why this exists.
 *
 * When something on any thread throws and nothing catches it, [install]'s handler writes the trace
 * to a small file and then lets Android carry on killing the app as it normally would. On the next
 * launch [uploadPending] posts that file to the server's `POST /client-errors` and deletes it once
 * the server has it. Only the latest trace is kept, with a count of how many crashes happened since
 * the last upload (a crash loop would otherwise write the same trace over and over).
 *
 * Limits: this only sees JVM exceptions. A native crash (in MapLibre's C++ code, say) or an ANR
 * kills the process without ever reaching this handler.
 */
object CrashReporter {
    private const val FILE_NAME = "pending_crash.json"
    private const val MAX_STACK_TRACE_LENGTH = 8_000
    private val MAX_AGE: Duration = Duration.ofDays(7)

    private val json = Json { ignoreUnknownKeys = true }

    fun install(context: Context) {
        val appContext = context.applicationContext
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            // Writing the report must never get in the way of the crash itself.
            runCatching { save(appContext, thread, throwable) }
            previous?.uncaughtException(thread, throwable)
        }
    }

    private fun save(context: Context, thread: Thread, throwable: Throwable) {
        val file = pendingFile(context)
        val earlier = read(file)
        val report = ClientErrorReport(
            stackTrace = Log.getStackTraceString(throwable).take(MAX_STACK_TRACE_LENGTH),
            threadName = thread.name,
            occurredAt = Instant.now().toString(),
            occurrences = (earlier?.occurrences ?: 0) + 1,
            appVersion = "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})",
            osVersion = osVersion(),
            deviceModel = "${Build.MANUFACTURER} ${Build.MODEL}",
        )
        file.writeText(json.encodeToString(report))
    }

    /**
     * Sends the saved report, if there is one. The file is deleted once the server has it (any 2xx),
     * or when it can never be accepted (a 4xx other than 429) or has gone stale; a network failure,
     * 429 or 5xx leaves it for the next launch.
     */
    suspend fun uploadPending(context: Context, api: DotWatcherApi) {
        val file = pendingFile(context)
        val report = read(file)
        if (report == null) {
            file.delete() // unreadable or absent
            return
        }
        val age = runCatching { Duration.between(Instant.parse(report.occurredAt), Instant.now()) }.getOrNull()
        if (age != null && age > MAX_AGE) {
            file.delete()
            return
        }

        val response = runCatching { api.postClientError(report) }.getOrNull() ?: return
        val permanent = response.code() in 400..499 && response.code() != 429
        if (response.isSuccessful || permanent) file.delete()
    }

    fun osVersion(): String = "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"

    private fun pendingFile(context: Context) = File(context.filesDir, FILE_NAME)

    private fun read(file: File): ClientErrorReport? {
        if (!file.exists()) return null
        return runCatching { json.decodeFromString<ClientErrorReport>(file.readText()) }.getOrNull()
    }
}
