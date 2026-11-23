# Start screen font: Space Mono signs, Red Hat Text for the rest of the app

Branches: `start-screen-font-ios`, then `start-screen-font-android`. Client only.

Chosen on the font canvas (Design artifact "Main screen font comparison"):
- the frame "Space Mono signs + Red Hat Text";
- the stepped-rule frames (Nearby title fit);
- "Subtitles in Space Mono, 13px regular".

## Decisions

- **Sign text is Space Mono Regular (400):** titles, subtitles and the small caps label (eyebrow) on Go, Join, Create
  and the event / Nearby tile. It replaces EB Garamond ExtraBold.
- **Titles are lowercase** ("go", "join", "create", "rogaine - hutt park"). Lower-casing is display only: spoken labels
  (VoiceOver, TalkBack) keep the text as written.
- **Sizes:**

  | Text | Size |
  |---|---|
  | go | 44 |
  | event / Nearby | 28 |
  | join, create | 26 |
  | subtitle | 13 regular (was 15 ExtraBold) |
  | eyebrow | 11, 1 pt letter spacing, upper case |

- **Only a title that won't fit shrinks.** The other tiles never change. A title that overflows its line steps down 2 pt
  at a time, with the last step 2% tighter (letter spacing −0.02em). The smallest size is **24 pt**. If it still
  doesn't fit at 24, it is cut with "…" on one line.
- **Red Hat Text replaces the system font** (SF Pro / Roboto) across the whole app (decided 2026-11-24): the start
  screen's wordmark and crew row, maps, sheets, Settings, everything except the signs. Code-style text (invite code
  boxes, the crew code, the watch link) stays in the system monospaced font for now. System alerts and dialogs
  can't be restyled and stay in the system font.
- **Macrons:** Space Mono has them (checked on the canvas with "ōtaki"), so no fallback font is needed.
- **Layout is separate.** The current list and Tiles B both use the same sign views, so this work doesn't wait for the
  layout decision. The fitting measures the real space, not a fixed character count, so it holds in either layout.

## How the title fitting works

Space Mono is monospace: every character, space and macron letter is the same width (about 0.61 × the font size).
So whether a title fits depends only on its length, and the result is the same on both platforms. The apps still
measure instead of counting, because two things change the available width:

- **Text size settings.** iOS Dynamic Type (`UIFontMetrics`, already applied in `SignFont`) and Android `sp` both
  scale the fonts.
- **Tile width.** It differs between phones and layouts.

The rule, used for every sign title:

```
steps = base size, base − 2, … down to 24 (the last step at −0.02em)
for each step: if the measured one-line width of the title ≤ the title's available width, use it
none fits: 24 pt, −0.02em, one line, cut at the end with "…"
```

Go, Join and Create never get past their first step. A title already at 24 or below (none today) keeps its size and
is only cut.

## iOS

- [x] Fonts:
  - Add `SpaceMono-Regular.ttf` and four static Red Hat Text weights (`RedHatText-Regular`, `-Medium`, `-SemiBold`,
    `-Bold`, from RedHatOfficial/RedHatFont) to the app target, listed under `UIAppFonts` in Info.plist.
  - Add the OFL licences to `ios/third_party`. Remove `EBGaramond.ttf` and its licence.
- [x] `SignFont` (LandingSigns.swift): Space Mono Regular, still scaled with `UIFontMetrics`; falls back to the system
      monospaced font.
- [x] `AppFont.swift`:
  - `Font.app(_ style, weight:)` (Red Hat Text at a text style's size, scaled with Dynamic Type) and
    `Font.app(size:weight:)` (fixed size, like `.system(size:)`). Each weight is asked for by PostScript name, so
    the right face is certain.
  - Every `.font(.headline)`, `.font(.system(size:weight:))` and `.fontWeight(...)` in the app moved to these.
  - `.font(.app(.body))` on the root view, so unstyled text (buttons, toggles, form rows, sheets) is Red Hat Text.
  - Navigation bar titles and bar buttons through `UINavigationBar` / `UIBarButtonItem` appearance (fonts only).
- [x] `SignContent`: subtitle and "Organised by" 13 pt; titles shown lowercase.
- [x] `FittedSignTitle`: `ViewThatFits` over the title at each step (its size down to 24 by 2, the 24 step at −0.02em,
      one line), and a last candidate at 24 that is cut with "…". It measures at the current text size and tile
      width, so no counting.
- [ ] Check on a device or simulator:
  - The icons still line up. `IconPlacement` reads the new font's metrics, but uses the title's base size even when
    the title has shrunk.
  - Bold and semibold text shows the right faces.
  - The five canvas names at default and largest Dynamic Type.

## Android

- [ ] Fonts:
  - Add `res/font/space_mono_regular.ttf` and `res/font/red_hat_text.ttf` (variable).
  - Add the OFL licences to `android/third_party`.
  - Remove `eb_garamond.ttf` once unused.
- [ ] `SignFontFamily` (ui/theme/SignPalette.kt): Space Mono Regular. Add `UiFontFamily` for Red Hat Text with
      `FontVariation.weight` 400–700.
- [ ] `SignContent` (ui/landing/SignButton.kt):
  - Eyebrow, title and subtitle styles to Space Mono Normal.
  - Subtitle 13.sp.
  - Titles shown lowercase (`lowercase()`), keeping the content description as written.
  - Check `iconPlacement` still lines up the icon. The typeface lookup at line ~367 asks for ExtraBold; change it to
    Normal.
- [ ] `FittedSignTitle`:
  - Measure with `rememberTextMeasurer()` inside `BoxWithConstraints`.
  - Pick the step by the same rule.
  - At the last step: `maxLines = 1`, `overflow = TextOverflow.Ellipsis` and `letterSpacing = (-0.02).em`.
- [ ] Red Hat Text across the app: set `UiFontFamily` as the default in the app's `Typography` (every Material text
      style) so all screens pick it up, plus any hard-coded `FontFamily.Default` / `TextStyle(fontSize = ...)` uses.
- [ ] Unit test the step choice with a fake measurer: the five canvas names; a text-size scale of 1.3 pushes
      "rogaine - hutt park" to the cut.

## Testing

- [ ] Both apps, default text size:
  - "sunday long run" → 28
  - "hutt park rogaine" → 26
  - "rogaine - hutt park" → 24 tight
  - "ōtaki river trail run" and "ōtaki river trail half" → 24 tight, cut with "…"

  The exact cut-over can move by a character between phones; what matters is one line and no other tile changing.
- [ ] Largest text size: titles shrink, then cut; nothing overlaps the icon.
- [ ] Macrons render in Space Mono, not a fallback.
- [ ] VoiceOver / TalkBack read titles as written, not lower-cased or cut.
- [ ] Compare against the canvas frames side by side.

## Open questions

1. ~~Red Hat Text beyond the start screen?~~ Yes, the whole app (2026-11-24).
2. Does the help page or web client need the new font for consistency? Proposed: no, not now.
