package nz.skelstar.dotwatcher.network

import kotlinx.serialization.SerializationException
import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import javax.net.ssl.SSLHandshakeException
import kotlin.coroutines.cancellation.CancellationException

class NetworkErrorsTest {
    private val local = "http://10.0.2.2:8080"
    private val production = "https://dot-watcher.skelstar.io/api"

    @Test fun `the server is named by host, with the port when there is one`() {
        assertEquals("10.0.2.2:8080", serverHostLabel(local))
        assertEquals("dot-watcher.skelstar.io", serverHostLabel(production))
        assertEquals("the server", serverHostLabel("not a url"))
    }

    @Test fun `a server that can't be reached says which one`() {
        val expected = "Can't reach 10.0.2.2:8080. Check your connection and try again."
        assertEquals(expected, networkErrorMessage(ConnectException("refused"), local))
        assertEquals(expected, networkErrorMessage(UnknownHostException("10.0.2.2"), local))
        assertEquals(
            "Can't reach dot-watcher.skelstar.io. Check your connection and try again.",
            networkErrorMessage(UnknownHostException("dot-watcher.skelstar.io"), production),
        )
    }

    @Test fun `a slow server, a bad certificate and an unreadable reply each get their own words`() {
        assertEquals(
            "dot-watcher.skelstar.io didn't answer in time. Try again.",
            networkErrorMessage(SocketTimeoutException(), production),
        )
        assertEquals(
            "Can't make a secure connection to dot-watcher.skelstar.io.",
            networkErrorMessage(SSLHandshakeException("expired"), production),
        )
        assertEquals(
            "The server sent a reply the app couldn't read. Try updating the app.",
            networkErrorMessage(SerializationException("bad json"), production),
        )
    }

    @Test fun `a cancelled request and other failures are described plainly`() {
        assertEquals("The request was interrupted. Try again.", networkErrorMessage(CancellationException(), production))
        assertEquals("Network error. Check your connection and try again.", networkErrorMessage(IOException("boom"), production))
        assertEquals("Network error.", networkErrorMessage(IllegalStateException("boom"), production))
    }
}
