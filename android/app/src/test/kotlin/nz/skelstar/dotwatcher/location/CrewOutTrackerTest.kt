package nz.skelstar.dotwatcher.location

import nz.skelstar.dotwatcher.network.RunnerPosition
import org.junit.Assert.assertEquals
import org.junit.Test
import java.time.Duration
import java.time.Instant

class CrewOutTrackerTest {
    private val t0 = Instant.parse("2026-11-23T08:00:00Z")
    private fun at(minutes: Long) = t0.plus(Duration.ofMinutes(minutes))

    @Test
    fun `first reply sets the baseline silently`() {
        val tracker = CrewOutTracker()
        assertEquals(emptyList<String>(), tracker.update(setOf("MIA", "SAM"), t0))
    }

    @Test
    fun `someone newly out is announced once`() {
        val tracker = CrewOutTracker()
        tracker.update(setOf("SAM"), t0)
        assertEquals(listOf("MIA"), tracker.update(setOf("SAM", "MIA"), at(1)))
        assertEquals(emptyList<String>(), tracker.update(setOf("SAM", "MIA"), at(2)))
    }

    @Test
    fun `several at once come back sorted`() {
        val tracker = CrewOutTracker()
        tracker.update(emptySet(), t0)
        assertEquals(listOf("JO", "MIA"), tracker.update(setOf("MIA", "JO"), at(1)))
    }

    @Test
    fun `leaving and coming back inside the cooldown stays quiet, after it alerts`() {
        val tracker = CrewOutTracker()
        tracker.update(emptySet(), t0)
        assertEquals(listOf("MIA"), tracker.update(setOf("MIA"), at(1)))
        tracker.update(emptySet(), at(5))
        assertEquals(emptyList<String>(), tracker.update(setOf("MIA"), at(10)))
        tracker.update(emptySet(), at(20))
        assertEquals(listOf("MIA"), tracker.update(setOf("MIA"), at(31)))
    }

    @Test
    fun `someone in the baseline who leaves and returns soon stays quiet`() {
        val tracker = CrewOutTracker()
        tracker.update(setOf("MIA"), t0)
        tracker.update(emptySet(), at(2))
        assertEquals(emptyList<String>(), tracker.update(setOf("MIA"), at(4)))
    }

    @Test
    fun `reset starts a new baseline`() {
        val tracker = CrewOutTracker()
        tracker.update(emptySet(), t0)
        tracker.reset()
        assertEquals(emptyList<String>(), tracker.update(setOf("MIA"), at(1)))
    }

    @Test
    fun `yourself and crew in a named session never count`() {
        fun pos(name: String, isSelf: Boolean = false, inSession: Boolean = false) = RunnerPosition(
            runnerName = name, latitude = 0.0, longitude = 0.0, timestamp = "2026-11-23T08:00:00Z",
            isSelf = isSelf, inSession = inSession,
        )
        val out = CrewOutTracker.outNames(listOf(pos("ME", isSelf = true), pos("BOB", inSession = true), pos("MIA")))
        assertEquals(setOf("MIA"), out)
    }

    @Test
    fun `titles`() {
        assertEquals("DW: Mia has joined", CrewOutTracker.title(listOf("Mia")))
        assertEquals("DW: Mia and Sam have joined", CrewOutTracker.title(listOf("Mia", "Sam")))
        assertEquals("DW: Jo, Mia and Sam have joined", CrewOutTracker.title(listOf("Jo", "Mia", "Sam")))
    }
}
