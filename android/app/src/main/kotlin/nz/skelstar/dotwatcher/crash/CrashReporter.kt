package nz.skelstar.dotwatcher.crash

import android.app.ActivityManager
import android.app.ApplicationExitInfo
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
import java.io.InputStream
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
 * That handler only sees JVM exceptions. A native crash (in MapLibre's C++ code, say) or an ANR
 * kills the process without ever reaching it, so [recordUnreportedExit] also asks Android, on the
 * next launch, why the previous run ended (`ApplicationExitInfo`, Android 11+) and turns a native
 * crash, ANR or failed start into the same kind of report.
 */
object CrashReporter {
    private const val FILE_NAME = "pending_crash.json"
    private const val MAX_STACK_TRACE_LENGTH = 8_000
    private val MAX_AGE: Duration = Duration.ofDays(7)

    private val json = Json { ignoreUnknownKeys = true }

    private const val PREFS_NAME = "dotwatcher_crash_reporter"
    private const val KEY_LAST_EXIT_TIMESTAMP = "last_reported_exit_timestamp"
    private const val MAX_TRACE_BYTES = 256 * 1024
    private const val MAX_LIBRARIES = 40
    private const val MAX_SYMBOLS = 40
    private const val BACKTRACE_LIBRARIES = 12
    private val NOTABLE_LIBRARY = Regex("maplibre|mapbox|mali|adreno|powervr|vulkan|libEGL|GLES|libgl", RegexOption.IGNORE_CASE)

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
     * or when the server will never accept it (400, 413, 422) or it has gone stale; a network failure,
     * a 404 (server not updated yet), 429 or 5xx leaves it for the next launch.
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
        // Only a report the server will never accept is dropped. A 404 (a server that hasn't been
        // updated to have this endpoint yet), 429 and 5xx are all "try again next launch".
        val rejected = response.code() in setOf(400, 413, 422)
        if (response.isSuccessful || rejected) file.delete()
    }

    /**
     * Android 11+: finds the most recent run of this app that ended in a native crash, an ANR or a
     * failed start, that has not been reported yet, and saves it as the pending report. Android
     * keeps these records across app updates, so on the first launch of a build with this code it
     * can surface a crash that happened under an earlier build. Only one exit is reported per
     * launch, and nothing is written if a JVM crash is already waiting to go up (the exit is picked
     * up next time).
     */
    fun recordUnreportedExit(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return
        val file = pendingFile(context)
        if (file.exists()) return

        val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val lastReported = prefs.getLong(KEY_LAST_EXIT_TIMESTAMP, 0L)
        val activityManager = context.getSystemService(ActivityManager::class.java) ?: return
        val exit = runCatching { activityManager.getHistoricalProcessExitReasons(context.packageName, 0, 10) }
            .getOrNull()
            ?.filter { it.timestamp > lastReported && it.processName == context.packageName && it.isInteresting() }
            ?.maxByOrNull { it.timestamp }
            ?: return

        val report = ClientErrorReport(
            stackTrace = describe(exit).take(MAX_STACK_TRACE_LENGTH),
            threadName = null,
            occurredAt = Instant.ofEpochMilli(exit.timestamp).toString(),
            occurrences = 1,
            // The run that ended may have been an older build: Android doesn't record which. This
            // is the build that noticed.
            appVersion = "${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE}) [noticed on launch]",
            osVersion = osVersion(),
            deviceModel = "${Build.MANUFACTURER} ${Build.MODEL}",
        )
        runCatching {
            file.writeText(json.encodeToString(report))
            prefs.edit().putLong(KEY_LAST_EXIT_TIMESTAMP, exit.timestamp).apply()
        }
    }

    private fun ApplicationExitInfo.isInteresting(): Boolean = when (reason) {
        ApplicationExitInfo.REASON_CRASH_NATIVE,
        ApplicationExitInfo.REASON_ANR,
        ApplicationExitInfo.REASON_INITIALIZATION_FAILURE -> true
        else -> false
    }

    private fun reasonName(reason: Int): String = when (reason) {
        ApplicationExitInfo.REASON_CRASH_NATIVE -> "CRASH_NATIVE"
        ApplicationExitInfo.REASON_ANR -> "ANR"
        ApplicationExitInfo.REASON_INITIALIZATION_FAILURE -> "INITIALIZATION_FAILURE"
        else -> "reason $reason"
    }

    private fun describe(exit: ApplicationExitInfo): String {
        val header = buildString {
            appendLine("ApplicationExitInfo: ${reasonName(exit.reason)}")
            appendLine("description: ${exit.description}")
            appendLine("status: ${exit.status}, importance: ${exit.importance}, pid: ${exit.pid}")
        }
        val trace = runCatching { exit.traceInputStream?.use { it.readCapped() } }.getOrNull()
            ?: return header
        // A native crash's trace is a binary tombstone; an ANR's is plain text.
        val body = if (exit.reason == ApplicationExitInfo.REASON_ANR) {
            String(trace, Charsets.UTF_8)
        } else {
            summariseTombstone(trace)
        }
        return header + body
    }

    private fun InputStream.readCapped(): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        val buffer = ByteArray(8 * 1024)
        while (out.size() < MAX_TRACE_BYTES) {
            val read = read(buffer)
            if (read < 0) break
            out.write(buffer, 0, read)
        }
        return out.toByteArray()
    }

    /**
     * Pulls the useful, readable parts out of a binary tombstone: the signal, the libraries and the
     * function names in the backtrace. A native crash's trace is protobuf, so raw strings come glued
     * to length bytes and hashes; matching specific shapes (a path ending `.so`, a `SIG...` name, an
     * identifier containing `_` or `::`) keeps the output readable. The libraries are the part that
     * answers "was it MapLibre, or a GPU driver?".
     */
    internal fun summariseTombstone(bytes: ByteArray): String {
        val text = String(bytes, Charsets.ISO_8859_1)
        val signals = Regex("SIG[A-Z]{3,8}").findAll(text).map { it.value }.distinct().take(5).toList()
        val allLibraries = Regex("/[A-Za-z0-9_.+\\-/]*\\.so").findAll(text).map { it.value.trimStart('/').let { p -> "/$p" } }.distinct().toList()
        // A tombstone lists the crashing thread's backtrace before the process's memory map, so the
        // first few libraries are the frames; the rest is everything that was loaded.
        val backtrace = allLibraries.take(BACKTRACE_LIBRARIES)
        val notable = allLibraries.drop(BACKTRACE_LIBRARIES)
            .filter { NOTABLE_LIBRARY.containsMatchIn(it) }.take(MAX_LIBRARIES)
        val symbols = Regex("[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z0-9_~]+)*").findAll(text).map { it.value }
            .filter { it.length >= 8 && ('_' in it || "::" in it) }
            .distinct().take(MAX_SYMBOLS).toList()
        return buildString {
            appendLine("signals: ${signals.joinToString(", ").ifEmpty { "(none found)" }}")
            appendLine("backtrace libraries:")
            backtrace.forEach { appendLine("  $it") }
            appendLine("map/GPU libraries loaded:")
            notable.forEach { appendLine("  $it") }
            appendLine("symbols:")
            append(symbols.joinToString("\n") { "  $it" })
        }
    }

    fun osVersion(): String = "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})"

    private fun pendingFile(context: Context) = File(context.filesDir, FILE_NAME)

    private fun read(file: File): ClientErrorReport? {
        if (!file.exists()) return null
        return runCatching { json.decodeFromString<ClientErrorReport>(file.readText()) }.getOrNull()
    }
}
