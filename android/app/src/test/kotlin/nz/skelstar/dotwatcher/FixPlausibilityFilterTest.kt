package nz.skelstar.dotwatcher

import nz.skelstar.dotwatcher.location.FixPlausibilityFilter
import nz.skelstar.dotwatcher.location.FixPoint
import nz.skelstar.dotwatcher.location.Plausibility
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class FixPlausibilityFilterTest {
    private val lat = -41.2436
    private val lon = 174.789

    /** About [metres] north of the base point. */
    private fun fix(seconds: Double, northMetres: Double = 0.0, accuracy: Float = 10f) =
        FixPoint(lat + northMetres / 111_320.0, lon, accuracy, seconds)

    private fun rejected(p: Plausibility) = p is Plausibility.Rejected

    @Test fun `first fix is accepted`() {
        assertEquals(Plausibility.Accepted, FixPlausibilityFilter().check(fix(0.0)))
    }

    @Test fun `walking pace is accepted`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        assertEquals(Plausibility.Accepted, filter.check(fix(15.0, northMetres = 25.0)))
    }

    @Test fun `driving on the open road is accepted`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        // 30 m/s (108 km/h) for 15 s.
        assertEquals(Plausibility.Accepted, filter.check(fix(15.0, northMetres = 450.0)))
    }

    @Test fun `a 5 km jump in one tick is rejected`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        assertTrue(rejected(filter.check(fix(15.0, northMetres = 5_000.0))))
    }

    @Test fun `a bad fix after a long gap is still rejected`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        // Silent for ten minutes, then a 5 km jump: the gap is capped, so it can't excuse it.
        assertTrue(rejected(filter.check(fix(600.0, northMetres = 5_000.0))))
    }

    @Test fun `the track recovers after a one-off bad fix`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        assertTrue(rejected(filter.check(fix(15.0, northMetres = 5_000.0))))
        assertEquals(Plausibility.Accepted, filter.check(fix(30.0, northMetres = 20.0)))
    }

    @Test fun `a real relocation is accepted once confirmed`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        assertTrue(rejected(filter.check(fix(15.0, northMetres = 5_000.0))))
        assertEquals(Plausibility.Accepted, filter.check(fix(30.0, northMetres = 5_010.0)))
        assertEquals(Plausibility.Accepted, filter.check(fix(45.0, northMetres = 5_020.0)))
    }

    @Test fun `two scattered bad fixes do not re-anchor`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        assertTrue(rejected(filter.check(fix(15.0, northMetres = 5_000.0))))
        assertTrue(rejected(filter.check(fix(30.0, northMetres = -5_000.0))))
    }

    @Test fun `reset forgets the last position`() {
        val filter = FixPlausibilityFilter()
        filter.check(fix(0.0))
        filter.reset()
        assertEquals(Plausibility.Accepted, filter.check(fix(15.0, northMetres = 5_000.0)))
    }
}
