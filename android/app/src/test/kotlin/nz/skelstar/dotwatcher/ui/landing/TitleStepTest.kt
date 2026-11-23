package nz.skelstar.dotwatcher.ui.landing

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * The sign title rule (.ai/plans/start-screen-font.md), with a stand-in for measuring: Space Mono is monospaced, so a
 * line is about 0.61 x the size per character, 0.02 less per character when tight. 290 is the Nearby tile's room.
 */
class TitleStepTest {
    private fun step(title: String, baseSize: Int = 28, room: Float = 290f, textScale: Float = 1f) =
        chooseTitleStep(baseSize) { s ->
            title.length * s.size * textScale * (0.61f - if (s.tight) 0.02f else 0f) <= room
        }

    @Test fun `a short name keeps its size`() = assertEquals(TitleStep(28, tight = false), step("sunday long run"))

    @Test fun `a little long steps down once`() = assertEquals(TitleStep(26, tight = false), step("hutt park rogaine"))

    @Test fun `longer still is 24 and tight`() = assertEquals(TitleStep(24, tight = true), step("rogaine - hutt park"))

    @Test fun `too long for 24 is cut`() =
        assertEquals(TitleStep(24, tight = true, cut = true), step("ōtaki river trail half"))

    @Test fun `larger text makes a name that fit get cut`() =
        assertEquals(TitleStep(24, tight = true, cut = true), step("rogaine - hutt park", textScale = 1.3f))

    @Test fun `go, join and create never change`() {
        assertEquals(TitleStep(44, tight = false), step("go", baseSize = 44, room = 300f))
        assertEquals(TitleStep(26, tight = false), step("create", baseSize = 26))
    }

    @Test fun `a title already at the minimum is only cut`() =
        assertEquals(TitleStep(22, tight = false, cut = true), step("a very long name indeed here", baseSize = 22))
}
