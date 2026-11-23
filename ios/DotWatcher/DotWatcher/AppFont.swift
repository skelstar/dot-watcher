import SwiftUI
import UIKit

/// Red Hat Text (OFL, ios/third_party), the app's text font everywhere except the start screen's signs
/// (`SignFont`, Space Mono). See .ai/plans/start-screen-font.md.
///
/// Four static weights are bundled (listed under `UIAppFonts` in Info.plist): Regular, Medium, SemiBold and
/// Bold. Each weight is asked for by its PostScript name rather than by family plus a weight trait, so the
/// right face is used for certain; any weight in between rounds to the nearest of the four. If a file is
/// missing, `Font.custom` quietly falls back to the system font.
enum AppFont {
    static func postScriptName(_ weight: Font.Weight) -> String {
        switch weight {
        case .medium: "RedHatText-Medium"
        case .semibold: "RedHatText-SemiBold"
        case .bold, .heavy, .black: "RedHatText-Bold"
        default: "RedHatText-Regular"
        }
    }

    /// The system's size for each text style at the default Dynamic Type setting; `relativeTo:` scales from it.
    static func size(_ style: Font.TextStyle) -> CGFloat {
        switch style {
        case .largeTitle: 34
        case .title: 28
        case .title2: 22
        case .title3: 20
        case .headline, .body: 17
        case .callout: 16
        case .subheadline: 15
        case .footnote: 13
        case .caption: 12
        case .caption2: 11
        @unknown default: 17
        }
    }

    /// The weight the system font gives a style when none is asked for: only `.headline` is semibold.
    static func defaultWeight(_ style: Font.TextStyle) -> Font.Weight {
        style == .headline ? .semibold : .regular
    }

    /// The UIKit counterpart, for navigation bar titles and anything else drawn by UIKit. Scaled with Dynamic Type.
    static func uiFont(_ style: UIFont.TextStyle, size: CGFloat, weight: Font.Weight) -> UIFont {
        guard let font = UIFont(name: postScriptName(weight), size: size) else {
            return UIFont.preferredFont(forTextStyle: style)
        }
        return UIFontMetrics(forTextStyle: style).scaledFont(for: font)
    }

    /// Navigation bar titles and bar buttons in Red Hat Text. Called once at launch. Only the fonts are set, through
    /// the plain appearance properties, so the bars keep the system's own backgrounds (and scroll-edge look).
    static func installAppearance() {
        let navigationBar = UINavigationBar.appearance()
        navigationBar.titleTextAttributes = [.font: uiFont(.headline, size: 17, weight: .semibold)]
        navigationBar.largeTitleTextAttributes = [.font: uiFont(.largeTitle, size: 34, weight: .bold)]
        let barButton = UIBarButtonItem.appearance()
        barButton.setTitleTextAttributes([.font: uiFont(.body, size: 17, weight: .regular)], for: .normal)
        barButton.setTitleTextAttributes([.font: uiFont(.body, size: 17, weight: .regular)], for: .highlighted)
    }
}

extension Font {
    /// Red Hat Text at a text style's size, scaled with Dynamic Type. Replaces `.font(.headline)` and the like:
    /// `.font(.app(.headline))`, `.font(.app(.subheadline, weight: .semibold))`.
    static func app(_ style: Font.TextStyle, weight: Font.Weight? = nil) -> Font {
        .custom(AppFont.postScriptName(weight ?? AppFont.defaultWeight(style)), size: AppFont.size(style), relativeTo: style)
    }

    /// Red Hat Text at a fixed size, like `.system(size:weight:)`: it doesn't scale with Dynamic Type.
    static func app(size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .custom(AppFont.postScriptName(weight), fixedSize: size)
    }
}
