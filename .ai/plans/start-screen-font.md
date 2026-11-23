# Start screen font: Space Mono signs, Red Hat Text for the rest

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
- **Red Hat Text replaces the system font** (SF Pro / Roboto) for the start screen's other text: the wordmark, the
  crew row and the "Mia is out running now" line. See open question 1 for the rest of the app.
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

- [ ] Fonts:
  - Add `SpaceMono-Regular.ttf` (static) and `RedHatText.ttf` (variable, `wght` axis) to the app target.
  - Add the OFL licences to `ios/third_party`.
  - Remove `EBGaramond.ttf` and its licence once nothing uses them.
- [ ] `SignFont` (LandingSigns.swift): register Space Mono and return it at Regular. No `wght` variation (static
      font). Keep the `UIFontMetrics` scaling and the system fallback (`.monospaced` design).
- [ ] `UiFont`: a small helper for Red Hat Text (weights 400–700 through `wght`). Use it for the wordmark, the crew
      row and the crew-out line on the start screen.
- [ ] `SignContent`: subtitle 13 pt Regular (and "Organised by"). Titles `lowercased()` on screen. Check that
      `IconPlacement` still lines the icon up from the new font's metrics; it reads them, so it should.
- [ ] `FittedSignTitle`: a small view or helper that picks the step.
  - Measure with `NSString.size(withAttributes:)` on the scaled `UIFont`.
  - Learn the available width with a `GeometryReader` or `onGeometryChange` on the title's row.
  - At the last step: `.lineLimit(1)`, `.truncationMode(.tail)` and `.tracking(-0.02 × size)`.
- [ ] Check without tests (there's no iOS test target): a debug preview with the five canvas names (15, 17, 19, 21 and 22
      characters) and the largest Dynamic Type size.

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
- [ ] Start screen's wordmark, crew row and crew-out line to `UiFontFamily`.
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

1. Red Hat Text beyond the start screen (maps, sheets, Settings)? Proposed: start screen only now, the rest as its
   own change, so this one stays small and easy to review.
2. Does the help page or web client need the new font for consistency? Proposed: no, not now.
