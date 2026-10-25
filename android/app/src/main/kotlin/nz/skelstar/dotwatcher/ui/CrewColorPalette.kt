package nz.skelstar.dotwatcher.ui

import androidx.compose.ui.graphics.Color
import nz.skelstar.dotwatcher.crew.CrewUiState

/**
 * The colours of the dots for people in your crew: ten that look different from each other and from
 * the blue that always means you. Separate from [nz.skelstar.dotwatcher.ui.map.RunnerColorPalette],
 * which colours map pins by name. Each person hashes to a slot from their user id, so it is the same
 * on every screen. Two people who land on the same slot are separated in [assign], so nobody in one
 * crew shares a colour until there are more than ten of them. Same colours and rules as iOS.
 */
object CrewColorPalette {
    private val colors: List<Color> = listOf(
        Color(0xFFD32F2F), // red
        Color(0xFFEF6C00), // orange
        Color(0xFFB28900), // gold
        Color(0xFF7A9A01), // olive
        Color(0xFF2E7D32), // green
        Color(0xFF00838F), // teal
        Color(0xFF7B1FA2), // purple
        Color(0xFFE91E8C), // pink
        Color(0xFF6D4C41), // brown
        Color(0xFF37474F), // charcoal
    )

    /** A colour for each user id. Ids are taken in sorted order; each one gets its own slot, or the
     *  next free one after it. With the same crew this always gives the same answer; if someone joins
     *  or leaves, only people who had to move off a taken slot can change colour. */
    fun assign(userIds: Collection<String>): Map<String, Color> {
        val taken = mutableSetOf<Int>()
        val result = mutableMapOf<String, Color>()
        for (id in userIds.toSortedSet()) {
            var slot = (hash(id) % colors.size.toUInt()).toInt()
            if (taken.size < colors.size) {
                while (slot in taken) slot = (slot + 1) % colors.size
                taken += slot
            }
            result[id] = colors[slot]
        }
        return result
    }

    /** The same string hash as RunnerColorPalette (`h = h * 31 + char`). */
    private fun hash(text: String): UInt {
        var h = 0u
        for (char in text) h = h * 31u + char.code.toUInt()
        return h
    }
}

/** The dot colour for one of your crew. Falls back to a lone hash for someone not in [CrewUiState.members]. */
fun CrewUiState.colorFor(userId: String): Color =
    CrewColorPalette.assign(members.map { it.userId } + userId)[userId] ?: Color.Gray
