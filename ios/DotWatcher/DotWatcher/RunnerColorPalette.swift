import SwiftUI

/// Assigns each runner a stable color from an 8-color palette, hashed from their name — mirrors
/// the web client's `runnerColour()` (`client/src/useRunnerMarkers.ts`) so the same runner shows
/// the same color on both the web viewer and the native map.
enum RunnerColorPalette {
    private static let colors: [Color] = [
        Color(red: 0x25 / 255, green: 0x63 / 255, blue: 0xeb / 255), // blue
        Color(red: 0xdc / 255, green: 0x26 / 255, blue: 0x26 / 255), // red
        Color(red: 0x16 / 255, green: 0xa3 / 255, blue: 0x4a / 255), // green
        Color(red: 0xd9 / 255, green: 0x77 / 255, blue: 0x06 / 255), // amber
        Color(red: 0x93 / 255, green: 0x33 / 255, blue: 0xea / 255), // purple
        Color(red: 0xdb / 255, green: 0x27 / 255, blue: 0x77 / 255), // pink
        Color(red: 0x08 / 255, green: 0x91 / 255, blue: 0xb2 / 255), // teal
        Color(red: 0xea / 255, green: 0x58 / 255, blue: 0x0c / 255), // orange
    ]

    static func color(for name: String) -> Color {
        colors[Int(hash(name) % UInt32(colors.count))]
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
