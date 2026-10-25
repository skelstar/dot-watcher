import CoreText
import SwiftUI

// The start screen's "sign" buttons: Go, Invite code and Create, plus a placeholder event sign. Laid out
// like the Android app's, from the Coffee Go palette in .ai/mockups/landing-signs.html.

/// The sign colours.
enum SignPalette {
    static let coffee = Color(red: 0x2B / 255, green: 0x21 / 255, blue: 0x18 / 255)
    static let camel = Color(red: 0xAF / 255, green: 0x91 / 255, blue: 0x64 / 255)
    static let ivory = Color(red: 0xF7 / 255, green: 0xF3 / 255, blue: 0xE3 / 255)
    static let lava = Color(red: 0x6F / 255, green: 0x1A / 255, blue: 0x07 / 255)
}

/// EB Garamond (OFL, ios/third_party) at ExtraBold, the nearest free face to the ITC Garamond on the
/// park signs. One variable font file, registered on first use and set to weight 800 through its
/// `wght` axis. Falls back to the system serif if the file can't be loaded.
enum SignFont {
    private static let registered: Void = {
        if let url = Bundle.main.url(forResource: "EBGaramond", withExtension: "ttf") {
            CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
        }
    }()

    private static let weightAxis = 0x7767_6874  // 'wght'

    static func font(size: CGFloat) -> Font {
        _ = registered
        let descriptor = UIFontDescriptor(fontAttributes: [
            .name: "EBGaramond-Regular",
            UIFontDescriptor.AttributeName(rawValue: kCTFontVariationAttribute as String): [weightAxis: 800],
        ])
        let font = UIFont(descriptor: descriptor, size: size)
        guard font.familyName.contains("Garamond") else {
            return .system(size: size, weight: .heavy, design: .serif)
        }
        return Font(UIFontMetrics.default.scaledFont(for: font))
    }
}

private let signBarHeight: CGFloat = 12
private let signFrameWidth: CGFloat = 2

/// Title and a smaller line under it on the left, an arrow on the right.
private struct SignContent: View {
    var eyebrow: String?
    let title: String
    let subtitle: String
    let titleSize: CGFloat
    let arrowSize: CGFloat
    let ink: Color
    var alignBottom = false

    var body: some View {
        HStack(alignment: alignBottom ? .bottom : .center, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                if let eyebrow {
                    Text(eyebrow.uppercased())
                        .font(SignFont.font(size: 11))
                        .tracking(1)
                }
                Text(title)
                    .font(SignFont.font(size: titleSize))
                Text(subtitle)
                    .font(SignFont.font(size: 15))
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Image(systemName: "arrow.right")
                .font(.system(size: arrowSize, weight: .semibold))
                .accessibilityHidden(true)
        }
        .foregroundStyle(ink)
    }
}

/// The ivory sign (Invite code, Create): coffee text and frame.
struct SignButton: View {
    let title: String
    let subtitle: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            SignContent(title: title, subtitle: subtitle, titleSize: 26, arrowSize: 24, ink: SignPalette.coffee)
                .padding(.horizontal, 16)
                .padding(.vertical, 14)
                .frame(maxWidth: .infinity)
                .background(SignPalette.ivory)
                .overlay(Rectangle().strokeBorder(SignPalette.coffee, lineWidth: signFrameWidth))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(title). \(subtitle)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The Go sign, about 1.5x the height of `SignButton` with a lava bar across the top. Held rather than
/// tapped, like the round Go button was: a lighter fill sweeps across while it is pressed and
/// `onComplete` runs when full, so a pocket can't start tracking.
struct HoldSignButton: View {
    let title: String
    let subtitle: String
    let onComplete: () -> Void
    @State private var progress: CGFloat = 0

    var body: some View {
        VStack(spacing: 0) {
            SignPalette.lava.frame(height: signBarHeight)
            SignContent(
                title: title, subtitle: subtitle, titleSize: 44, arrowSize: 30, ink: SignPalette.camel,
                alignBottom: true
            )
            .padding(.horizontal, 16)
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity, minHeight: 108, alignment: .bottomLeading)
            .background(SignPalette.coffee)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    SignPalette.camel.opacity(0.28)
                        .frame(width: proxy.size.width * progress)
                }
            }
            .contentShape(Rectangle())
            .holdToComplete(progress: $progress, onComplete: onComplete)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(title). \(subtitle)")
        .accessibilityHint("Hold to start tracking")
        .accessibilityAction { onComplete() }
    }
}

/// A public event happening near the runner, shown under Go in lava with ivory text and a camel bar.
/// `eyebrow` is the small caps line above the title.
struct EventSignButton: View {
    let eyebrow: String
    let title: String
    let subtitle: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 0) {
                SignPalette.camel.frame(height: signBarHeight)
                SignContent(
                    eyebrow: eyebrow, title: title, subtitle: subtitle, titleSize: 28, arrowSize: 24,
                    ink: SignPalette.ivory
                )
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity)
            }
            .background(SignPalette.lava)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(eyebrow). \(title). \(subtitle)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The invite-code entry that used to be the inline "Have an invite code?" card: paste or type the six
/// characters, then join. Opened from the Invite code sign.
struct JoinCodeSheet: View {
    @Binding var code: String
    let isBusy: Bool
    let onJoin: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 20) {
            Text("Enter your invite code")
                .font(.title2.bold())
            Button {
                if let pasted = UIPasteboard.general.string { code = pasted }
            } label: {
                Label("Paste", systemImage: "doc.on.clipboard")
            }
            CodeBoxField(text: $code, length: 6, autoFocus: true)
            Button("Join Session") {
                onJoin()
                dismiss()
            }
            .buttonStyle(.borderedProminent)
            .disabled(isBusy || code.count < 6)
            Button("Cancel", role: .cancel) { dismiss() }
                .foregroundStyle(.secondary)
        }
        .padding(32)
    }
}
