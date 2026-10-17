package nz.skelstar.dotwatcher.ui.map

/**
 * Assigns each runner a stable color from an 8-color palette, hashed from their name. Ported
 * from ios/DotWatcher/DotWatcher/RunnerColorPalette.swift — note that despite that file's own
 * comment claiming it mirrors the web client's `runnerColour()`
 * (client/src/useRunnerMarkers.ts), the two actually use different palettes, different color
 * ordering, and the web client has no reserved "current user" slot at all; this is a pre-existing
 * cross-platform inconsistency, not something introduced here. Android matches iOS exactly (not
 * web), since this milestone's goal is iOS parity and Android's native map is otherwise closer in
 * spirit to iOS's native map than to the web viewer.
 */
object RunnerColorPalette {
    /** Index of the color reserved for the local user's own dot everywhere, so it's unambiguous
     *  at a glance. Matches iOS's `currentUser: Color = .blue` (this palette's own index-0 blue). */
    const val CURRENT_USER_INDEX: Int = 0

    // ARGB values match RunnerColorPalette.swift's RGB triples exactly (0x2563eb, 0xdc2626, etc.).
    private val colors: List<Long> = listOf(
        0xFF2563EB, // blue — reserved for the current user, see indexForName()
        0xFFDC2626, // red
        0xFF16A34A, // green
        0xFFD97706, // amber
        0xFF9333EA, // purple
        0xFFDB2777, // pink
        0xFF0891B2, // teal
        0xFFEA580C, // orange
    )

    /** The full palette, in index order — for callers (e.g. pre-registering one map icon per
     *  color) that need every color rather than one runner's. */
    fun allColors(): List<Long> = colors

    fun colorAt(index: Int): Long = colors[index]

    /** Other runners hash into the same 8-color palette; since [CURRENT_USER_INDEX] always
     *  renders as blue, a colliding runner is bumped to index 1 so no two participants are ever
     *  the same color on screen. Matches iOS's `color(for:)` exactly, including this bump. */
    fun indexForName(name: String): Int {
        val index = (hash(name) % colors.size.toUInt()).toInt()
        return if (index == CURRENT_USER_INDEX) 1 else index
    }

    /** Matches iOS's `hash(_:)` — and, in turn, the same string-hash formula the web client uses
     *  (`h = (h * 31 + charCode) >>> 0`) — character by character, so the same name would hash to
     *  the same *index* on any platform, even though iOS/Android's and web's palettes differ. */
    private fun hash(name: String): UInt {
        var h: UInt = 0u
        for (char in name) {
            h = h * 31u + char.code.toUInt()
        }
        return h
    }
}
