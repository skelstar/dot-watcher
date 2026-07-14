import SwiftUI

/// Assigns each runner a stable color from an 8-color palette, hashed from their name — mirrors
/// the web client's `runnerColour()` (`client/src/useRunnerMarkers.ts`) so the same runner shows
/// the same color on both the web viewer and the native map.
enum RunnerColorPalette {
    /// The color used to mark the local user's own dot/badge everywhere (map pin, legend row,
    /// app title badge) so it's unambiguous at a glance and consistent across the app.
    static let currentUser: Color = .blue

    private static let colors: [Color] = [
        Color(red: 0x25 / 255, green: 0x63 / 255, blue: 0xeb / 255), // blue — reserved for currentUser, see color(for:)
        Color(red: 0xdc / 255, green: 0x26 / 255, blue: 0x26 / 255), // red
        Color(red: 0x16 / 255, green: 0xa3 / 255, blue: 0x4a / 255), // green
        Color(red: 0xd9 / 255, green: 0x77 / 255, blue: 0x06 / 255), // amber
        Color(red: 0x93 / 255, green: 0x33 / 255, blue: 0xea / 255), // purple
        Color(red: 0xdb / 255, green: 0x27 / 255, blue: 0x77 / 255), // pink
        Color(red: 0x08 / 255, green: 0x91 / 255, blue: 0xb2 / 255), // teal
        Color(red: 0xea / 255, green: 0x58 / 255, blue: 0x0c / 255), // orange
    ]

    /// Other runners hash into the same 8-color palette the web client uses, so index 0 (blue)
    /// can land on someone other than the current user. Since `currentUser` always renders as
    /// blue, a colliding runner is bumped to the next slot so no two participants are ever the
    /// same color on screen — this bump is local-only and doesn't need to match the web client,
    /// which never highlights a "current user" the same way.
    static func color(for name: String) -> Color {
        let index = Int(hash(name) % UInt32(colors.count))
        return index == 0 ? colors[1] : colors[index]
    }

    /// Matches the web's `h = (h * 31 + charCode) >>> 0` string hash exactly, so the same name
    /// hashes to the same palette index on both platforms.
    private static func hash(_ name: String) -> UInt32 {
        var h: UInt32 = 0
        for scalar in name.unicodeScalars {
            h = h &* 31 &+ scalar.value
        }
        return h
    }
}
