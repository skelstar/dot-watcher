# Group sign on the start screen — Plan

*DotWatcher — plan, 27 November 2026. Status: nothing below is built. Chosen design: option D.*

While someone is in a group, the start screen shows a big group card above Go (`GroupInCard`). It holds the end time,
Leave, and for the organiser Ping here, Choose on map, the entry code, Join area and End group. With all of that it
has outgrown the space above the fixed signs: in the 1.4.12 build its bottom runs under the Go sign.

**Change:** replace the card with a **group sign**, shaped like the nearby group tile people see before joining. It
has one row per group, and each row has a gear. Tapping a row opens that group's **sheet**, which holds everything the
card holds now. The start screen goes back to Go, Join and Create, with the group sign above them.

Mock-up: the "Group button on the start screen" design canvas (claude.ai artifact `4mn7F9ANH1Ck5NnQTrHrub`, private
to Sean). Option D is the chosen layout; option A shows the same sign with one group. The two sheets on that canvas
are the organiser and joined versions.

Related: [`go-and-groups.md`](go-and-groups.md) (groups, rules, several groups at once),
[`join-zone-privacy.md`](join-zone-privacy.md) (the join area stays organiser-only, which the sheet must keep).

Per `AGENTS.md`, build and test verification is left to CI. Tick boxes as work lands: `- [ ]` → `- [x]`.

---

## The design

**The group sign** sits where the card is now, above Go:

- Same look as the nearby tile (`EventSignButton`): the palette's `accent` background, an `accentInk` bar along the
  top, `accentInk` text in the sign font.
- **One group:** eyebrow `YOUR GROUP · 9 H 6 M LEFT` (or `YOU'RE IN · …`), the group's name as the title, and a
  line under it: `Ends 21:36`. A framed solid gear sits on the right.
- **Two or more:** eyebrow `2 GROUPS`, then one row per group: the name, and `Yours · 9 h 6 m left` or
  `Kiri's · 2 d left`. Each row has its own gear. Rows are split by a thin rule in `accentInk`.
- **The whole row is the button,** not only the gear: a large target, labelled for screen readers as
  "Testing again, your group, 9 hours left. Group settings". The gear is a cue, hidden from screen readers.
- **Order:** the organiser's own group first, then the rest by soonest ending (open question 3).

**The group sheet** opens from a row, as a bottom sheet with Done:

- **Organiser:** eyebrow with the end time, the name, a line on who can join. Then "Where people join" (the map
  preview of the join area, Ping here, Choose on map), "Join area" (the four sizes), "Entry code" (the dashed code
  box with Change, or the button to add one) and End group at the bottom.
- **Joined:** eyebrow with the end time, the name, "Organised by" with the organiser's dot, three lines on what the
  group can see, and Leave group at the bottom.
- A private group (invite code, not shown nearby) shows its invite code in the dashed box and has no "Where people
  join" or "Join area".

**The Go sign** loses the `WITH …` line from #433, since the group sign right above it says the same (open question 1).

**The Go map** keeps its group chip. Its "Your groups" sheet shows the same rows as the group sign, and a row opens
the same group sheet, so the organiser's controls work from the map too.

## Work

### Shared

- [ ] **G1 Wording helpers, iOS and Android.** One function per string, next to `goSignEyebrow`
      (`GroupsViews.swift`, `GroupsModel.kt`): the sign's eyebrow (one group or `N GROUPS`), a row's line
      (`Yours` / `<organiser>'s` plus time left, from `GroupTime`), and the row's accessibility label. Android unit
      tests in `android/app/src/test/.../groups/`, like `GoSignEyebrowTest.kt`.
- [ ] **G2 Organiser name on iOS.** The server already sends `ownerDisplayName` on memberships; iOS's
      `SessionMembership` (`LocationManager.swift`) doesn't decode it. Add it as optional. Android has it.

### iOS

- [ ] **G3 `GroupSign`** in `GroupsViews.swift`: the sign above, built like `EventSignButton` in `LandingSigns.swift`
      (same `SignColors`, sign font and bar). The gear is `gearshape.fill` in a framed 44 pt square. Each row is a
      `Button` that sets the group to show.
- [ ] **G4 `GroupSheet`** in `GroupsViews.swift`: the organiser and joined layouts above, made from what
      `GroupInCard` holds today. Reuse `GroupAreaSection`, `EntryCodeSection`, `NearbyRangePreview` and
      `JoinLocationPickerSheet` as they are; the code box and its "only when no code" rule move across unchanged.
      Leave and End keep their confirmation dialogs.
- [ ] **G5 Start screen** (`ContentView.swift`): replace the `ForEach` of `GroupInCard` with one `GroupSign`, and
      present `GroupSheet` for the chosen group (`.sheet(item:)`). Drop `eyebrow:` from the Go sign (G9).
- [ ] **G6 Go map** (`GroupsOnMapSheet`, used from `CasualViews.swift`): show the same rows instead of cards; a row
      opens `GroupSheet`. `GroupInCard` can then be deleted.

### Android

- [ ] **G7 `GroupSign` and `GroupSheet`** in `ui/groups/GroupCards.kt`, matching iOS. The sign follows
      `EventSignButton` in `ui/landing/SignButton.kt`; the gear is Material's filled Settings icon. The sheet is a
      `ModalBottomSheet` holding today's card content (`CodeBox`, `GroupAreaSection`, `JoinLocationPickerDialog`,
      `EntryCodeSection`), driven by the same `GroupOrganiserActions`.
- [ ] **G8 Start screen and Go map** (`ui/session/SessionScreen.kt`, `ui/map/CasualMapScreen.kt` via
      `GroupsOnMapSheet`): the sign in place of the cards, the sheet on tap, and the Go sign without `eyebrow` (G9).
      Delete `GroupInCard` once nothing uses it.

### Clean-up and follow-ons

- [ ] **G9 Go sign wording.** Remove the `goSignEyebrow` calls on both start screens. Keep `HoldSignButton`'s
      `eyebrow` parameter (it's harmless) but delete `goSignEyebrow` and `GoSignEyebrowTest.kt` if nothing else uses
      them.
- [ ] **G10 Open the sheet after Create.** When the organiser creates a group, open its sheet straight away, so
      Ping, the code and the area are in reach while they set up.
- [ ] **G11 Release.** A version bump on both apps (the next after 1.4.12), and a line for What's new.

## Open questions

1. **`WITH …` on Go:** remove it, since the group sign above says the same? *Recommendation:* remove (G9).
2. **"14 joined":** the mock-up shows how many have joined. Memberships don't carry a count, so it needs a server
   field (for example `memberCount`, organiser only). *Recommendation:* leave it out for now; add it with the server
   alerts work if wanted.
3. **Row order:** server order (newest first), or the organiser's own group first, then soonest ending?
   *Recommendation:* own first, then soonest ending, so the one that matters next is on top.
4. **Many groups:** with three or more, the sign pushes Create below the fold. Fine, since the screen scrolls; or cap
   the sign at three rows plus "and 2 more", which opens the Go map's list. *Recommendation:* no cap until someone is
   in that many.
5. **Gear icon:** the built-in symbols (`gearshape.fill`, Material Settings) instead of the Font Awesome gear in the
   mock-up, so there's no attribution to add. *Recommendation:* built-in.

## Not in this plan

- A per-group "share while out" switch, a group filter on the Go map, and combining the Race rule with other groups.
  These come up once people are in overlapping groups; see [`go-and-groups.md`](go-and-groups.md).
