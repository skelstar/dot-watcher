package nz.skelstar.dotwatcher.crew

import java.net.URI

/**
 * Crew codes and the links that carry them. A crew code is 8 characters, one-time, and names a
 * person; it is shared as a message with a `https://<web host>/crew/{CODE}` link (also what the QR
 * code encodes), so a tap or a camera-app scan opens the app via Android App Links, and a recipient
 * without the app lands on the web fallback page. Pure Kotlin (no android.* types) so it can be
 * unit-tested. The server's rules live in `SessionStore.NormalizeCrewCode`.
 */
object CrewLink {
    const val CODE_LENGTH = 8

    // No 0/O/1/I/L, matching the server's alphabet exactly.
    private const val ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"
    private val codeInLink = Regex("/crew/([A-Za-z0-9]{$CODE_LENGTH})(?![A-Za-z0-9])")
    private val codeAfterLabel = Regex("crew code:\\s*([A-Za-z0-9-]{$CODE_LENGTH,${CODE_LENGTH + 1}})", RegexOption.IGNORE_CASE)

    /** The code in canonical form (upper case, no spaces or dashes), or null if [raw] isn't a
     *  well-formed crew code. Same rules as the server, so a code that passes here is at least
     *  worth asking the server about. */
    fun normalize(raw: String?): String? {
        val code = raw?.replace("-", "")?.replace(" ", "")?.trim()?.uppercase().orEmpty()
        if (code.length != CODE_LENGTH) return null
        return code.takeIf { c -> c.all { it in ALPHABET } }
    }

    /** The code in an `https://…/crew/{CODE}` link, or null for any other URL or a malformed code.
     *  The host isn't checked: App Links already limit which hosts reach the app, and a pasted
     *  link from another host carries nothing but a code. */
    fun codeFromUrl(url: String?): String? {
        if (url.isNullOrBlank()) return null
        val path = runCatching { URI(url.trim()).path }.getOrNull() ?: return null
        val parts = path.split('/').filter { it.isNotEmpty() }
        if (parts.size != 2 || !parts[0].equals("crew", ignoreCase = true)) return null
        return normalize(parts[1])
    }

    /** Finds a crew code in pasted text: the link out of a whole shared message, a "Crew code: X"
     *  line, or the text itself if that is just a code. Looks for the structure rather than any
     *  8-letter word, because a shared message contains plenty of those ("skelstar"). */
    fun extractFromPaste(text: String): String? {
        codeInLink.find(text)?.groupValues?.get(1)?.let { normalize(it)?.let { code -> return code } }
        codeAfterLabel.find(text)?.groupValues?.get(1)?.let { normalize(it)?.let { code -> return code } }
        return normalize(text)
    }

    fun url(webBaseUrl: String, code: String): String = "${webBaseUrl.trimEnd('/')}/crew/$code"

    /** The message sent through the share sheet. Mirrors the session invite message: a tappable link
     *  first, then the plain code for anyone whose phone doesn't open the link. */
    fun shareMessage(webBaseUrl: String, code: String): String =
        "Add me to my DotWatcher crew!\n\n" +
            "Tap to add me in the app:\n${url(webBaseUrl, code)}\n\n" +
            "Crew code: $code\n\n" +
            "This code works once and expires in 24 hours."
}
