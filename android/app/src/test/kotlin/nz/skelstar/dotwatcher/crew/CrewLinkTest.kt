package nz.skelstar.dotwatcher.crew

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class CrewLinkTest {
    private val code = "ABCDEFGH"

    @Test fun `normalize accepts a well formed code in any case, with spaces or dashes`() {
        assertEquals(code, CrewLink.normalize("abcdefgh"))
        assertEquals(code, CrewLink.normalize(" ABCD-EFGH "))
        assertEquals(code, CrewLink.normalize("ABCD EFGH"))
    }

    @Test fun `normalize rejects wrong length and characters that are never issued`() {
        assertNull(CrewLink.normalize(null))
        assertNull(CrewLink.normalize(""))
        assertNull(CrewLink.normalize("ABCDEFG")) // 7
        assertNull(CrewLink.normalize("ABCDEFGHJ")) // 9
        assertNull(CrewLink.normalize("ABCDEF0H")) // 0
        assertNull(CrewLink.normalize("ABCDEF1H")) // 1
        assertNull(CrewLink.normalize("ABCDEFIH")) // I
        assertNull(CrewLink.normalize("ABCDEFLH")) // L
        assertNull(CrewLink.normalize("ABCDEFOH")) // O
    }

    @Test fun `codeFromUrl reads the code from a crew link`() {
        assertEquals(code, CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/ABCDEFGH"))
        assertEquals(code, CrewLink.codeFromUrl("https://dot-watcher-staging.skelstar.io/crew/abcdefgh"))
        assertEquals(code, CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/ABCDEFGH/"))
        assertEquals(code, CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/ABCDEFGH?utm=x#frag"))
    }

    @Test fun `codeFromUrl ignores other links and malformed codes`() {
        assertNull(CrewLink.codeFromUrl(null))
        assertNull(CrewLink.codeFromUrl(""))
        assertNull(CrewLink.codeFromUrl("not a url"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/join/ABC123"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/code/ABC123"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/SHORT"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/ABCDEF0H"))
        assertNull(CrewLink.codeFromUrl("https://dot-watcher.skelstar.io/crew/ABCDEFGH/extra"))
    }

    @Test fun `extractFromPaste finds the code in a whole shared message`() {
        val message = CrewLink.shareMessage("https://dot-watcher.skelstar.io", code)
        assertEquals(code, CrewLink.extractFromPaste(message))
    }

    @Test fun `extractFromPaste is not fooled by other eight letter words`() {
        // "skelstar" and "dotwatch" are both 8 letters and valid crew characters; only the link or the
        // labelled code is the real one.
        val message = "Hi from skelstar! Add me: https://dot-watcher.skelstar.io/crew/PQRSTUVW thanks"
        assertEquals("PQRSTUVW", CrewLink.extractFromPaste(message))
        assertEquals("PQRSTUVW", CrewLink.extractFromPaste("dotwatch crew code: pqrs-tuvw"))
    }

    @Test fun `extractFromPaste accepts a bare code and rejects other text`() {
        assertEquals(code, CrewLink.extractFromPaste("abcdefgh"))
        assertEquals(code, CrewLink.extractFromPaste("  ABCD-EFGH  "))
        assertNull(CrewLink.extractFromPaste("just some words with no code in them"))
        assertNull(CrewLink.extractFromPaste("https://dot-watcher.skelstar.io/join/ABC123"))
    }

    @Test fun `share message holds a tappable link then the plain code`() {
        val message = CrewLink.shareMessage("https://dot-watcher.skelstar.io/", code)
        assertEquals(
            "Add me to my DotWatcher crew!\n\n" +
                "Tap to add me in the app:\nhttps://dot-watcher.skelstar.io/crew/ABCDEFGH\n\n" +
                "Crew code: ABCDEFGH\n\n" +
                "This code works once and expires in 24 hours.",
            message,
        )
    }

    @Test fun `url does not double the slash`() {
        assertEquals("https://x.test/crew/ABCDEFGH", CrewLink.url("https://x.test/", code))
        assertEquals("https://x.test/crew/ABCDEFGH", CrewLink.url("https://x.test", code))
    }
}
