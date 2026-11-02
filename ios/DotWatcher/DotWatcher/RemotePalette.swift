import SwiftUI

// The start screen's colours from the server (`config.palette`, see .ai/plans/remote-palette.md).
// The app uses all of a palette or none of it: every colour is `#rrggbb`, the version is one it knows, and
// the three text-on-background pairs have at least 4.5:1 contrast. Anything else, or no palette at all,
// leaves the built-in sign colours (`SignPalette`) in place, so a bad palette can never break the screen.

/// Every colour the start screen's signs and background use.
struct SignColors {
    let goBg: Color
    let goFg: Color
    let goBar: Color
    let altBg: Color
    let altFg: Color
    let accent: Color
    let accentInk: Color
    /// Nil for the built-in colours, which leave the system background alone.
    let screen: Color?

    static let builtIn = SignColors(
        goBg: SignPalette.navy, goFg: SignPalette.sand, goBar: SignPalette.sand,
        altBg: SignPalette.cream, altFg: SignPalette.navy,
        accent: SignPalette.orange, accentInk: SignPalette.ink,
        screen: nil
    )

    /// The colours for this run of the app: the server's palette if one was saved, else the built-in ones.
    static var current: SignColors { PaletteStore.applied ?? .builtIn }
}

/// The palette as the server sends it. Every field is read leniently (a wrong type becomes nil) so an
/// unexpected shape here can never fail a sign-in; `validated()` decides whether it is usable.
struct PaletteWire: Codable {
    static let supportedVersion = 1
    static let minimumContrast = 4.5

    private(set) var version: Int?
    private(set) var goBg: String?
    private(set) var goFg: String?
    private(set) var goBar: String?
    private(set) var altBg: String?
    private(set) var altFg: String?
    private(set) var accent: String?
    private(set) var accentInk: String?
    private(set) var screen: String?

    private enum CodingKeys: String, CodingKey {
        case version, goBg, goFg, goBar, altBg, altFg, accent, accentInk, screen
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = try? c.decode(Int.self, forKey: .version)
        goBg = try? c.decode(String.self, forKey: .goBg)
        goFg = try? c.decode(String.self, forKey: .goFg)
        goBar = try? c.decode(String.self, forKey: .goBar)
        altBg = try? c.decode(String.self, forKey: .altBg)
        altFg = try? c.decode(String.self, forKey: .altFg)
        accent = try? c.decode(String.self, forKey: .accent)
        accentInk = try? c.decode(String.self, forKey: .accentInk)
        screen = try? c.decode(String.self, forKey: .screen)
    }

    /// The colours if this palette is complete and readable, else nil.
    func validated() -> SignColors? {
        guard version == Self.supportedVersion,
              let goBg = RGB(hex: goBg), let goFg = RGB(hex: goFg), let goBar = RGB(hex: goBar),
              let altBg = RGB(hex: altBg), let altFg = RGB(hex: altFg),
              let accent = RGB(hex: accent), let accentInk = RGB(hex: accentInk),
              let screen = RGB(hex: screen)
        else { return nil }

        guard goFg.contrast(with: goBg) >= Self.minimumContrast,
              altFg.contrast(with: altBg) >= Self.minimumContrast,
              accentInk.contrast(with: accent) >= Self.minimumContrast
        else { return nil }

        return SignColors(
            goBg: goBg.color, goFg: goFg.color, goBar: goBar.color,
            altBg: altBg.color, altFg: altFg.color,
            accent: accent.color, accentInk: accentInk.color,
            screen: screen.color
        )
    }
}

/// `config` on the user object. Read leniently, like `PaletteWire`.
struct UserConfig: Codable {
    let palette: PaletteWire?

    private enum CodingKeys: String, CodingKey { case palette }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        palette = try? c.decode(PaletteWire.self, forKey: .palette)
    }
}

/// The last good palette, kept so the next launch can use it before any network call.
enum PaletteStore {
    private static let key = "remotePalette"

    /// The palette for this run of the app, read once (the app touches it at launch) so a palette saved now
    /// shows from the next launch, with no recolouring part-way through.
    static let applied: SignColors? = {
        guard let data = UserDefaults.standard.data(forKey: key),
              let wire = try? JSONDecoder().decode(PaletteWire.self, from: data)
        else { return nil }
        return wire.validated()
    }()

    /// Remembers the palette in a response that really arrived, or forgets it when that response has none
    /// (the server's palette was removed or is invalid) so the app returns to the built-in colours. A failed
    /// request must not call this.
    static func save(_ config: UserConfig?) {
        if let wire = config?.palette, wire.validated() != nil, let data = try? JSONEncoder().encode(wire) {
            UserDefaults.standard.set(data, forKey: key)
        } else {
            UserDefaults.standard.removeObject(forKey: key)
        }
    }
}

/// A `#rrggbb` colour, with the WCAG contrast maths the palette check needs.
struct RGB {
    let red: Double
    let green: Double
    let blue: Double

    init?(hex: String?) {
        guard let hex, hex.count == 7, hex.hasPrefix("#"),
              let value = Int(hex.dropFirst(), radix: 16),
              hex.dropFirst().allSatisfy(\.isHexDigit)
        else { return nil }
        red = Double((value >> 16) & 0xFF) / 255
        green = Double((value >> 8) & 0xFF) / 255
        blue = Double(value & 0xFF) / 255
    }

    var color: Color { Color(red: red, green: green, blue: blue) }

    private var luminance: Double {
        func linear(_ c: Double) -> Double { c <= 0.03928 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4) }
        return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue)
    }

    func contrast(with other: RGB) -> Double {
        let a = luminance, b = other.luminance
        return (max(a, b) + 0.05) / (min(a, b) + 0.05)
    }
}
