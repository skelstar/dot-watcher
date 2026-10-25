package nz.skelstar.dotwatcher.network

import kotlinx.serialization.SerializationException
import nz.skelstar.dotwatcher.BuildConfig
import java.io.IOException
import java.net.SocketException
import java.net.SocketTimeoutException
import java.net.URI
import java.net.UnknownHostException
import javax.net.ssl.SSLException
import kotlin.coroutines.cancellation.CancellationException

/** The server's `host` or `host:port` ("10.0.2.2:8080"), for messages. Names the server because a build pointed
 *  at the wrong one (the emulator's host machine, a LAN machine, staging) looks exactly like an outage. */
fun serverHostLabel(baseUrl: String): String =
    runCatching {
        val uri = URI(baseUrl)
        if (uri.port != -1) "${uri.host}:${uri.port}" else uri.host
    }.getOrNull() ?: "the server"

/**
 * Words for a request that failed before the server gave a usable answer, so the person is told what to try
 * rather than just "Network error.". [baseUrl] is the API address the request went to.
 */
fun networkErrorMessage(error: Throwable, baseUrl: String): String {
    val host = serverHostLabel(baseUrl)
    return when (error) {
        is CancellationException -> "The request was interrupted. Try again."
        is UnknownHostException, is SocketException -> "Can't reach $host. Check your connection and try again."
        is SocketTimeoutException -> "$host didn't answer in time. Try again."
        is SSLException -> "Can't make a secure connection to $host."
        is SerializationException -> "The server sent a reply the app couldn't read. Try updating the app."
        is IOException -> "Network error. Check your connection and try again."
        else -> "Network error."
    }
}

/** [networkErrorMessage] for the server this build talks to. */
fun Throwable.networkMessage(): String = networkErrorMessage(this, BuildConfig.API_BASE_URL)
