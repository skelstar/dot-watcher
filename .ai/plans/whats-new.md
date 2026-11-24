# Start tiles and What's new — Plan

*DotWatcher — plan, November 2026*

Goal: rebuild the start screen as a grid of tiles under the dot-watchr wordmark (Tiles B in the mock-up), add a
What's new tile that lists the changes since the version the phone last ran, and let testers rearrange and resize the
tiles. Mock-ups: [`.ai/mockups/start-tiles.html`](../mockups/start-tiles.html) (Today vs Tiles A vs Tiles B) and
[`.ai/mockups/whats-new.html`](../mockups/whats-new.html) (the list the tile opens). Related:
[`crew-feature-flag.md`](crew-feature-flag.md) (the per-account features mechanism reused here) and
[`remote-palette.md`](remote-palette.md) (the colours the tiles use, and the existing use of `X-App-Version`).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally. Nothing in this plan is built yet.

---

## Decisions taken

### Layout
- **Tiles B is the layout.** The wordmark is the hero. Every tile, Go included, sits right under it on a 6-wide grid of
  squares: 2x2 Crew (with faces), Join and Create; then a row of 1x1 tiles (What's new, Help, Account, Settings);
  then Go at 6x3.
- **1x1 tiles are icon only.** Bigger tiles add a title and a short line.
- **Badges are number only.** Navy over the corner on light tiles, sand inside Go. Go shows how many of your crew are out.
- **Event tile.** Orange, shown above Go when an event is nearby. Allowed sizes 4x2 or 6x2.
- **Light only**, matching #347 (the first cut drops the Appearance setting).

### Account and Settings
- **Two screens instead of one.** Today one sheet (`AuthSheet.swift` on iOS) is titled Settings when signed in and
  Account when signed out, and mixes the two. It splits into:
  - **Account** (person icon, the user's initials): about you, needs you signed in. Who you're signed in as, crew
    name, blocked users, sign out, delete account. Signed out, it's the sign-in / create account form.
  - **Settings** (gear icon): about this phone, works signed out. Map dot size, satellite alerts and sound, developer
    options, legal.
- **Fixes the signed-out gap:** dot size is hidden today when signed out, because it sits inside the signed-in branch.
- Appearance (theme) is not carried over: the first cut is light only (#347).

### What's new
- **The tile is always there.** It never hides; only its count badge comes and goes.
- **The count is the changes since the version the phone last ran.** The phone keeps its last-run version locally
  and asks the server for notes; the badge shows how many are newer than that version.
- **Opening the list clears the count**, until the next update that has notes.
- **The list always has something in it.** It shows recent versions, newest first, with the ones that are new since
  the last-run version marked. So the tile is still useful when there is no count.
- **Fresh installs get the tile but no count** (no previous version on the phone).
- **Notes are tagged Both / iOS / Android.** Version numbers are shared across platforms (both apps are 1.4.x).
- **Notes are written in a new What's new tab on `/admin`.**
- **The list** is grouped by version, newest first, as in `whats-new.html`.

### Editing the layout
- **Editing ships, but only for testers.** It sits behind a per-account feature flag, `tile-edit`. Everyone else gets
  the fixed Tiles B layout.
- **The layout is saved on the phone only**, not on the account. It is lost on reinstall or a new phone; that is
  acceptable for a small, low-stakes setting. Syncing it to the account can come later if anyone asks.
- **Interaction as in the mock-up.** Press and hold a tile (or tap Edit tiles) to enter edit mode and select it. A round
  handle cycles through the tile's allowed sizes. Drag to move; dashed slots show where tiles can go and the landing
  spot is highlighted. Tiles that are covered move to the first free space and the grid packs upward. Done (or a tap
  on empty space) finishes. Reset puts Tiles B back.
- **Allowed sizes:** What's new, Help, Account and Settings 1x1 or 2x2. Crew, Join and Create 1x1, 2x2 or 4x2. Go 2x2, 4x2, 6x2
  or 6x3. Event 4x2 or 6x2.
- **The flag gates only the editing.** The layout never leaves the phone, so the server has nothing to block. Hiding
  the controls is the whole gate.
- **Signed-out users never get edit mode**, because the flag belongs to an account.
- **Losing the flag keeps the layout.** A tester taken off the flag keeps seeing their saved layout but can no longer
  edit it. (Proposed; confirm.)

---

## Server

### S1. Feature flag
- [ ] Add `TileEdit = "tile-edit"` to `Features` in `server/FeatureFlags.cs` and to `Features.All`.
- [ ] Testers are granted it with `PUT /admin/users/{userId}/features`. Login, register and `GET /me` already return
      each account's features; no new route.
- [ ] Add a toggle for `tile-edit` to the users list on `/admin` if the existing features UI doesn't pick it up from
      `Features.All`.

### S2. Release notes storage
- [ ] `release_notes` table, created with the idempotent pattern in `SessionStore.cs`: id, version (e.g. `1.4.2`),
      platform (`both` / `ios` / `android`), text, sort order, created at. Additive, so older builds never see it
      (Staging and Production share the database).
- [ ] Version comparison is numeric per part (`1.4.10` > `1.4.9`), not string order.

### S3. Reading notes
- [ ] `GET /whats-new?since=<version>`: notes up to the caller's own version (from `X-App-Version`), filtered to the
      caller's platform plus `both`, grouped by version, newest first, limited to recent versions. Each version says
      whether it is newer than `since`, and the response carries `newCount` (notes newer than `since`). Works signed
      in or out.
- [ ] No `since` (fresh install) → `newCount` 0, the list still returned.

### S4. Writing notes (admin)
- [ ] `GET` / `POST` / `PUT` / `DELETE /admin/release-notes`, admin bearer, like the other admin endpoints.
- [ ] Tests in `tests/DotWatcher.Server.Tests` for the version filter, the platform filter and ordering.
- [ ] `docs/api-reference.html` and `server/README.md` updated.

## Admin web (`client/src/AdminPanel.tsx`)

- [ ] What's new tab: list notes by version, add/edit/delete, choose Both / iOS / Android. A preview drawn like the
      phone's list.

## iOS

- [ ] Tiles B start screen in SwiftUI, replacing today's start buttons in `ContentView.swift`. Uses the remote palette.
- [ ] Split `AuthSheet.swift` into an Account sheet and a Settings sheet as described in Decisions; Settings opens
      signed out.
- [ ] Badges: crew-out count on Go, counts on other tiles where the mock-up shows them.
- [ ] Event tile above Go when an event is nearby.
- [ ] What's new: the 1x1 tile is always shown. Store the last-seen version; on launch, fetch
      `GET /whats-new?since=…` and show `newCount` as the badge when it is above 0. Opening the list records the
      current version as seen, which clears the badge. Fresh installs record the version without a badge.
- [ ] Edit mode, shown only when `tile-edit` is in the account's features: hold to select, size handle, drag with
      slots, pack upward, Done, Reset. Layout saved in `UserDefaults`.
- [ ] Saved layouts handle the Event tile coming and going: when it's missing it leaves no hole (the grid packs);
      when it appears it goes in its Tiles B place, or the first free space if that's taken.

## Android

- [ ] Same as iOS, in Compose, including the Account / Settings split. Layout saved in DataStore. Features read from login and `GET /me` (Android doesn't use
      features since the gates were removed, so this needs adding back to the user model).

---

## Open questions

- Losing the flag: keep the tester's saved layout (proposed) or reset to Tiles B?
- How many recent versions the list shows when nothing is new (proposed: the last five).
- Where Help leads today, and whether it stays as it is behind the new tile.
- Android's equivalent of `AuthSheet` and what it holds, to confirm the split matches iOS.
