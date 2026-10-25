import SwiftUI

/// The colours of the dots for people in your crew: ten that look different from each other and from
/// the blue that always means you. Separate from `RunnerColorPalette`, which colours map pins by name.
/// Each person hashes to a slot from their user id, so it is the same on every screen. Two people who
/// land on the same slot are separated in `assign`, so nobody in one crew shares a colour until there
/// are more than ten of them.
enum CrewColorPalette {
    private static let hexes: [UInt32] = [
        0xD32F2F, // red
        0xEF6C00, // orange
        0xB28900, // gold
        0x7A9A01, // olive
        0x2E7D32, // green
        0x00838F, // teal
        0x7B1FA2, // purple
        0xE91E8C, // pink
        0x6D4C41, // brown
        0x37474F, // charcoal
    ]

    static var colors: [Color] {
        hexes.map { Color(red: Double($0 >> 16 & 0xFF) / 255, green: Double($0 >> 8 & 0xFF) / 255, blue: Double($0 & 0xFF) / 255) }
    }

    /// A colour for each user id. Ids are taken in sorted order; each one gets its own slot, or the next
    /// free one after it. With the same crew this always gives the same answer; if someone joins or
    /// leaves, only people who had to move off a taken slot can change colour.
    static func assign(_ userIds: [String]) -> [String: Color] {
        let palette = colors
        var taken = Set<Int>()
        var result: [String: Color] = [:]
        for id in Set(userIds).sorted() {
            var slot = Int(hash(id) % UInt32(palette.count))
            if taken.count < palette.count {
                while taken.contains(slot) { slot = (slot + 1) % palette.count }
                taken.insert(slot)
            }
            result[id] = palette[slot]
        }
        return result
    }

    /// The same string hash as `RunnerColorPalette` (`h = h * 31 + char`).
    private static func hash(_ text: String) -> UInt32 {
        var h: UInt32 = 0
        for scalar in text.unicodeScalars { h = h &* 31 &+ scalar.value }
        return h
    }
}
