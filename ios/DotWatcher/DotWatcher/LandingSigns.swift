import CoreText
import SwiftUI

// The start screen's "sign" buttons: Go, Join and Create, plus a placeholder event sign. Laid out
// like the Android app's, from the Navy and orange palette (Palette 2) in .ai/mockups/landing-signs.html.

/// The sign colours.
enum SignPalette {
    static let navy = Color(red: 0x1F / 255, green: 0x33 / 255, blue: 0x50 / 255)
    static let sand = Color(red: 0xF2 / 255, green: 0xEF / 255, blue: 0xE4 / 255)
    static let cream = Color(red: 0xF6 / 255, green: 0xF1 / 255, blue: 0xE4 / 255)
    static let orange = Color(red: 0xF2 / 255, green: 0x8C / 255, blue: 0x28 / 255)
    static let ink = Color(red: 0x1F / 255, green: 0x2A / 255, blue: 0x3D / 255)
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
        Font(uiFont(size: size))
    }

    /// The same font as `font(size:)`, for reading its metrics.
    static func uiFont(size: CGFloat) -> UIFont {
        _ = registered
        let descriptor = UIFontDescriptor(fontAttributes: [
            .name: "EBGaramond-Regular",
            UIFontDescriptor.AttributeName(rawValue: kCTFontVariationAttribute as String): [weightAxis: 800],
        ])
        let font = UIFont(descriptor: descriptor, size: size)
        guard font.familyName.contains("Garamond") else {
            let system = UIFont.systemFont(ofSize: size, weight: .heavy)
            return system.fontDescriptor.withDesign(.serif).map { UIFont(descriptor: $0, size: size) } ?? system
        }
        return UIFontMetrics.default.scaledFont(for: font)
    }
}

private let signBarHeight: CGFloat = 12
private let signFrameWidth: CGFloat = 2
private let signLineSpacing: CGFloat = 3

/// The line icon on the right of a sign. All are drawn with the same stroke so they read as a set:
/// chevrons to head off (Go, events), an arrow going in to join a run, a plus to make a new one.
enum SignIcon {
    case chevrons, enter, plus

    /// Stroke width in the drawing's own units; the drawings are 56 units tall.
    fileprivate static let stroke: CGFloat = 6.5

    /// The drawing's box, in the units its paths use.
    fileprivate var box: CGSize {
        self == .chevrons ? CGSize(width: 40, height: 56) : CGSize(width: 56, height: 56)
    }

    /// The lines, inset by half the stroke so the ink fills the box exactly.
    fileprivate var path: Path {
        let s = Self.stroke / 2
        var path = Path()
        func line(_ points: CGPoint...) { path.addLines(points) }
        switch self {
        case .chevrons:
            line(CGPoint(x: s, y: s), CGPoint(x: s + 15, y: 28), CGPoint(x: s, y: 56 - s))
            line(CGPoint(x: s + 18, y: s), CGPoint(x: s + 33, y: 28), CGPoint(x: s + 18, y: 56 - s))
        case .enter:
            line(CGPoint(x: 31, y: s), CGPoint(x: 56 - s, y: s), CGPoint(x: 56 - s, y: 56 - s), CGPoint(x: 31, y: 56 - s))
            line(CGPoint(x: s, y: 28), CGPoint(x: 39, y: 28))
            line(CGPoint(x: 27.5, y: 16.5), CGPoint(x: 39, y: 28), CGPoint(x: 27.5, y: 39.5))
        case .plus:
            line(CGPoint(x: 28, y: s), CGPoint(x: 28, y: 56 - s))
            line(CGPoint(x: s, y: 28), CGPoint(x: 56 - s, y: 28))
        }
        return path
    }
}

private struct SignGlyph: Shape {
    let icon: SignIcon

    func path(in rect: CGRect) -> Path {
        let scale = rect.height / icon.box.height
        return icon.path.applying(
            CGAffineTransform(scaleX: scale, y: scale).translatedBy(x: rect.minX / scale, y: rect.minY / scale)
        )
    }
}

/// Where a sign's icon goes: 80% of the height from the top of the title's capitals to the baseline of
/// the line under it, centred on that span. Worked out from the fonts' metrics, from the top of the text.
private struct IconPlacement {
    let top: CGFloat
    let height: CGFloat

    init(hasEyebrow: Bool, titleSize: CGFloat) {
        let titleFont = SignFont.uiFont(size: titleSize)
        let subtitleFont = SignFont.uiFont(size: 15)
        let titleTop = hasEyebrow ? SignFont.uiFont(size: 11).lineHeight + signLineSpacing : 0
        let capTop = titleTop + titleFont.ascender - titleFont.capHeight
        let subtitleBaseline = titleTop + titleFont.lineHeight + signLineSpacing + subtitleFont.ascender
        let span = subtitleBaseline - capTop
        height = span * 0.8
        top = capTop + (span - height) / 2
    }
}

/// Title and a smaller line under it on the left, an icon on the right.
private struct SignContent: View {
    var eyebrow: String?
    let title: String
    let subtitle: String
    let titleSize: CGFloat
    let icon: SignIcon
    let ink: Color
    /// Whether the icon nudges to the right now and then (Go only).
    var nudging = false

    var body: some View {
        let placement = IconPlacement(hasEyebrow: eyebrow != nil, titleSize: titleSize)
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: signLineSpacing) {
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
            SignGlyph(icon: icon)
                .stroke(style: StrokeStyle(
                    lineWidth: placement.height * SignIcon.stroke / icon.box.height,
                    lineCap: .round, lineJoin: .round
                ))
                .frame(width: placement.height * icon.box.width / icon.box.height, height: placement.height)
                .modifier(ArrowNudge(active: nudging))
                .padding(.top, placement.top)
                .accessibilityHidden(true)
        }
        .foregroundStyle(ink)
    }
}

/// Two quick 7pt pushes to the right, then a rest, every 3.5 seconds: a small "this way" on the Go
/// chevrons. Off while `active` is false (Go is held) and when Reduce Motion is on. Moves the drawing
/// only, so the sign's layout doesn't change.
private struct ArrowNudge: ViewModifier {
    let active: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let push: CGFloat = 7
    private static let step: TimeInterval = 0.21

    func body(content: Content) -> some View {
        if active && !reduceMotion {
            content.keyframeAnimator(initialValue: CGFloat(0), repeating: true) { view, x in
                view.offset(x: x)
            } keyframes: { _ in
                KeyframeTrack {
                    CubicKeyframe(Self.push, duration: Self.step)
                    CubicKeyframe(0, duration: Self.step)
                    CubicKeyframe(Self.push, duration: Self.step)
                    CubicKeyframe(0, duration: Self.step)
                    LinearKeyframe(0, duration: 3.5 - 4 * Self.step)
                }
            }
        } else {
            content
        }
    }
}

/// The cream sign (Join, Create): navy text and frame.
struct SignButton: View {
    let title: String
    let subtitle: String
    let icon: SignIcon
    let action: () -> Void

    var body: some View {
        let colors = SignColors.current
        Button(action: action) {
            SignContent(title: title, subtitle: subtitle, titleSize: 26, icon: icon, ink: colors.altFg)
                .padding(.horizontal, 16)
                .padding(.vertical, 14)
                .frame(maxWidth: .infinity)
                .background(colors.altBg)
                .overlay(Rectangle().strokeBorder(colors.altFg, lineWidth: signFrameWidth))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(title). \(subtitle)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The Go sign, about 2.2x the height of `SignButton` with a sand bar across the top. Held rather than
/// tapped, like the round Go button was: a lighter fill sweeps across while it is pressed and
/// `onComplete` runs when full, so a pocket can't start tracking.
struct HoldSignButton: View {
    let title: String
    let subtitle: String
    let onComplete: () -> Void
    @State private var progress: CGFloat = 0

    var body: some View {
        let colors = SignColors.current
        VStack(spacing: 0) {
            colors.goBar.frame(height: signBarHeight * 1.5)
            SignContent(
                title: title, subtitle: subtitle, titleSize: 44, icon: .chevrons,
                ink: colors.goFg, nudging: progress == 0
            )
            .padding(.horizontal, 20)
            .padding(.vertical, 18)
            .frame(maxWidth: .infinity, minHeight: 162, alignment: .bottomLeading)
            .background(colors.goBg)
            .overlay(alignment: .leading) {
                GeometryReader { proxy in
                    colors.goFg.opacity(0.28)
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

/// A public event happening near the runner, shown under Go in orange with ink text and a ink bar.
/// `eyebrow` is the small caps line above the title.
struct EventSignButton: View {
    let eyebrow: String
    let title: String
    let subtitle: String
    let action: () -> Void

    var body: some View {
        let colors = SignColors.current
        Button(action: action) {
            VStack(spacing: 0) {
                colors.accentInk.frame(height: signBarHeight)
                SignContent(
                    eyebrow: eyebrow, title: title, subtitle: subtitle, titleSize: 28, icon: .chevrons,
                    ink: colors.accentInk
                )
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity)
            }
            .background(colors.accent)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(eyebrow). \(title). \(subtitle)")
        .accessibilityAddTraits(.isButton)
    }
}

/// The invite-code entry that used to be the inline "Have an invite code?" card: paste or type the six
/// characters, then join. Opened from the Join sign.
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
