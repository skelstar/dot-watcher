package nz.skelstar.dotwatcher.ui.landing

import androidx.compose.ui.graphics.Color
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** The contrast check behind the organiser dot's initials colour on the nearby sign. */
class ContrastRatioTest {
    @Test
    fun blackOnWhite_isTheMaximum() {
        assertEquals(21f, contrastRatio(Color.Black, Color.White), 0.01f)
        assertEquals(21f, contrastRatio(Color.White, Color.Black), 0.01f) // order doesn't matter
    }

    @Test
    fun aColourOnItself_isOne() {
        assertEquals(1f, contrastRatio(Color(0xFFEAF1EE), Color(0xFFEAF1EE)), 0.001f)
    }

    @Test
    fun creamOnCream_failsTheSmallTextBar_andDarkGreenPasses() {
        // "Camel Go": the cream Join / Create background on the cream tile ink can't carry the initials.
        assertTrue(contrastRatio(Color(0xFFF6F1E4), Color(0xFFEDE3CC)) < 4.5f)
        assertTrue(contrastRatio(Color(0xFFEAF1EE), Color(0xFF1B4433)) >= 4.5f)
    }
}
