# Nearby join — Plan

*DotWatcher — plan, October 2026. Status: built on three open PRs (#254 server, #255 iOS, #256 Android), not yet
verified on a device or merged. **Start with [Handover](#handover-30-october-2026) at the end of this file.***

Goal: an organiser at the start of a trail creates a session; anyone physically near the organiser sees it on their
main screen and joins in one tap, with no invite code. Related: [`casual-tracking.md`](casual-tracking.md),
[`crew-feature-flag.md`](crew-feature-flag.md), mock-ups [`create-flow.html`](../mockups/create-flow.html) and
[`landing-signs.html`](../mockups/landing-signs.html).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally. The ticks in the task list are updated on the feature branches, so on `staging`
they lag until the PRs merge: the Handover section is the current picture.

---

## Decisions taken

- **Opt-in per session.** A "Let people nearby join" switch on Create, **off by default**. Off means the session is
  invisible to discovery and works exactly as today (invite code only).
- **Open to everyone.** No crew or watch-list gate. Anyone signed in and near the meeting point can join.
- **Gated by the existing `events` feature flag.** Same surface as "Happening near you"; no second flag.
- **The meeting point is set by a "Ping" button, nothing else.** The organiser taps Ping and the server stores their
  position as the session's join location. It never moves on its own: no tracking involved, no follow-the-organiser
  logic. Tap Ping again to move it (e.g. from the start to the finish). Creating the session with nearby on counts as
  the first ping. Joining is allowed for the whole session. Following the organiser while they track is a possible
  later addition, deliberately left out for now.
- **Location is for discovery only.** The list shows title, organiser name and a rough distance (rounded, no bearing,
  never coordinates). Coordinates never leave the server.
- **Radius gates discovery and the join tap only.** Once joined, nobody is removed for being far away.
- **Selectable radius: 50 m, 100 m (default), 250 m, 500 m.** 20 m is too tight: phone GPS is typically 5-15 m
  outdoors and 30-50 m under trees or near buildings, and the organiser's and joiner's errors add. The server also
  forgives accuracy: a joiner passes if `distance - joiner accuracy - meeting-point accuracy <= radius`, each accuracy
  capped at 50 m so a terrible fix can't open the gate wide. New requests carry `accuracy` (`horizontalAccuracy` on
  iOS). A near-miss costs a retry, not a lockout.
- **Ping buttons on the start screen and the map screen**, organiser only. Tracking isn't needed. The nearby switch and
  radius can be changed from the same place (a small sheet behind the Ping button, or a long press; settle in the UI).
- **One row per session**, nearest first. The title is what people choose by, so it is required when nearby is on, and
  limited to **30 characters** for now. It is decoupled from the invite code.
- **One active session per organiser.** "Active" means owned by them, not archived and within its event length. The
  server enforces it on create (409 with the existing session's id), only for accounts with the `events` flag, so
  older builds and accounts without the flag behave exactly as today. Staging and Production share a database and an
  older Production build can still create a second session, so the client check is a convenience and the rule is
  best-effort until every build has it. Existing owners with several sessions are left alone.
- **End session = archive, not delete.** The owner ends the session for everyone: it is archived (nobody can join or
  rejoin, nearby listing stops), replay and the recording stay, exactly what happens today when the last runner
  leaves. A session also stops being "active" by itself when its event length passes, so an organiser who abandons one
  is never locked out of creating another.
- **Invite codes stay.** Family and remote watchers still need them. Nearby is an extra door, not a replacement.
- **No silence timeout.** A nearby session stays discoverable at its last ping until the organiser switches it off, it
  ends (event length) or it is archived. Simple and predictable; the organiser re-pings to move it.

## What this changes in the Create flow

The mock-up's "Show to people nearby" section (fixed meeting-point pin, draggable, range picker, map) becomes a
single switch with a one-line explanation ("People near you can find this run and join. Turn off any time."), and
below it a four-way radius choice (50 / 100 / 250 / 500 m, default 100 m) with a hint about GPS accuracy. No pin, no
map. The organiser's position, taken when they tap Create, is the first meeting point. The title is shown to
strangers, so limit it to 30 characters (the mock-up allows 40). Today the session name *is* the invite code (4-8
chars, `NormalizeSessionName`), so title and code must be decoupled; check how far the create-flow work already goes
(task N1).

## Existing pieces this reuses

- `GeoMath.DistanceMeters` (`server/CasualVisibility.cs`) for the distance test.
- `GET /me/events/nearby`, `NearbyEvent` and the `events` flag (`EventsController.cs`): currently one placeholder.
  Nearby sessions become entries in that list, so the main-screen "Happening near you" tile needs no second mechanism.
- `JoinSessionByInvite` and the blocked-user check, so a nearby join creates an ordinary `session_members` row and a
  blocked user is refused exactly as with a code.
- Nothing from location tracking: the Ping is its own small request, so the existing `POST /location` path is **not
  touched**. Joiners need no pre-join pings (their position travels with the list request).
- `MapRailButton` (`MapChrome.swift`) for the organiser's map-screen Ping button.

## Server

- **Schema** (additive, nullable or defaulted, as Staging and Production share one database and an older build names
  its columns explicitly): on `app_sessions` add `nearby BOOLEAN NOT NULL DEFAULT FALSE`, `nearby_radius_m SMALLINT`,
  `anchor_lat`, `anchor_lon` (DOUBLE PRECISION), `anchor_accuracy_m REAL`.
- **Create:** `CreateSessionRequest` gains optional `Nearby`, `NearbyRadiusMeters` (one of 50/100/250/500),
  `Latitude`/`Longitude`/`Accuracy`. Nearby requires a position; it seeds the meeting point.
- **Ping:** `PUT /sessions/{id}/nearby` (owner only) with `lat`/`lon`/`accuracy` and, optionally, `enabled` and
  `radiusMeters`. Sets the meeting point (and turns nearby on if it was off and a radius is given). Needs no
  tracking. Switching off hides the session from discovery only; existing members stay. Reject positions with an
  old timestamp or implausible accuracy (> 100 m) so a bad fix isn't stored.
- **List:** `GET /me/events/nearby?lat=&lon=&accuracy=` returns nearby sessions whose own radius covers the caller
  (with the accuracy allowance), excluding sessions the caller is blocked from, archived or ended sessions, and ones
  they already belong to. Capped (proposed 10); distance rounded (proposed to 50 m).
- **Join:** `POST /nearby/sessions/{sessionId}/join` with `lat`/`lon`/`accuracy`, `displayName`. Always joins as a runner (no viewer option: watching is done with the shared watch link). Re-checks
  distance at join time (the server decides, never the client), then joins as an invite join would, returning the same
  response shape so clients reuse their join handling. Failure is uniform ("not available") whether the session is too
  far, non-nearby or unknown, so it can't be probed.
- **Abuse limits:** rate-limit both endpoints per account (as `WatchLinkLimiter`/`CrewCodeLimiter` do). The list takes a
  client-supplied position, so someone could walk it across a map to find organisers; the limits, the small radius,
  rounded distance, no bearing and the result cap are what keep that costly. Accepted residual risk, noted in the PR.
- **Retention:** nothing new to purge; the meeting point lives and dies with the session row.

## Clients (iOS first, then Android)

- **Ping button, organiser only, on the map screen** (a button on the right-hand rail) **and the start screen** (on the
  organiser's session card). Tap = take a fresh fix and set the join location, with a brief "Join location set"
  confirmation. A small sheet from the same place holds the nearby switch and radius. With one active session per
  organiser (below) there is never any doubt which session a Ping applies to.
- **Create is disabled while the organiser has an active session**, with a line saying why ("You already have an active
  session. End it to create another."). The start screen shows that session in its place, with **End session** on the
  session card and in the map-screen sheet. End asks for confirmation ("This ends it for everyone. Replay stays
  available.").
- **Create screen:** replace the pin/range block with the switch and radius choice; send position with Create (needs
  when-in-use location permission at that moment, with a fallback explanation if denied: the switch can't be turned on).
- **Main screen:** while the screen is open and location is granted, request the nearby list on appear and every ~60 s
  (not in the background). One tile per session, title first, organiser and distance beneath; replaces the single
  placeholder sign.
- **Tap a tile:** an information sheet, not a choice. It tells the person who will see them (everyone in the run: name and
  live location), when (only while tracking; they can leave any time), who is in it, and whether replay is kept. Then
  **Join this run**, the nearby join call, and the normal session screen. No runner/viewer choice: watching without
  sharing is the existing shared watch link.
- Android: the same, on `DotWatcherApi`/`AppViewModel`; keep response models identical.

## Tasks

- [x] N0 Decided: reuse `events` flag; selectable radius 50/100/250/500 m (default 100); no silence timeout;
      join location set only by a Ping button (start screen + map screen), no tracking involvement.
- [x] N1 Decouple session title (30 chars) from invite code. The invite code was already generated separately
      (`GenerateCode`); only the controller's 4-8 char name rule tied them. A nearby session now validates its title with
      `NearbyJoin.NormalizeTitle` (1-30 chars, keeps case and spaces); sessions without nearby keep the legacy rule.
- [x] N2 Schema columns (incl. radius and accuracy) + `CreateSessionRequest` fields + meeting-point seeding.
      `server/NearbyJoin.cs`, `SessionStore` migration and `CreateSessionForUser`, `SessionsController.CreateSession`.
      Nearby on create needs the `events` flag, a valid title, radius (default 100) and a position (accuracy <= 100 m).
      Tests: `NearbyCreateTests.cs` (not run locally; CI).
- [x] N3 `PUT /sessions/{id}/nearby` (Ping: set the join location; optional switch and radius; owner only).
      `SessionsController.UpdateNearby` + `SessionStore.UpdateNearby`; tests in `NearbyPingTests.cs` (CI only).
      401 signed out, 404 without `events`, 403 not the owner or unknown id, 410 ended, 400 bad fix/radius. A fix is
      required unless the call is only `enabled: false`. Not done: the "old timestamp" check (the request has no
      timestamp; add one if stale fixes prove a problem). Turning nearby on for a session created without it
      keeps its legacy upper-cased name as the title; a title field on this call may be needed (open for N9).
- [x] N4 List endpoint (per-session radius with accuracy allowance, exclusions, rounding, cap) under the `events` flag
      (404 when off). `GET /me/events/nearby?lat=&lon=&accuracy=` (`EventsController`, `SessionStore.FindNearbySessions`,
      `NearbyJoin.IsWithinRadius`). Entries reuse `NearbyEvent` plus a new `sessionId` (null on the placeholder), so
      clients tell joinable rows apart. With no `lat`/`lon` it still returns the placeholder, so installed builds are
      unchanged. Excludes: switched off, archived, past event length (created_at + max length, default 24 h), caller
      already a member, blocked either way with the organiser. Distance rounded to 50 m ("here" at 0), cap 10.
      Tests in `NearbyListTests.cs` (CI only).
- [x] N5 Nearby join endpoint (re-check distance with accuracy allowance, blocked/archived, uniform failure).
      `POST /nearby/sessions/{id}/join` (`SessionsController.JoinNearbySession`, `SessionStore.JoinNearbySession`).
      Always a runner; same response shape as the invite join. Unknown, not nearby, switched off, ended, blocked and
      too far are all the same 404 `{ "error": "This session isn't available." }` (blocked is 404 here, not the invite
      join's 403, so a blocked user can't tell the session exists). 404 without `events`, 401 signed out, 400 bad
      position. Tests in `NearbyJoinApiTests.cs` (CI only).
- [x] N6 Rate limiters for list and join. `server/NearbyLimiter.cs`: 120 list requests and 20 join attempts per
      account per hour, separate budgets, 429 when spent. The position-less placeholder call is never limited.
      Tests in `NearbyLimiterTests.cs` (CI only). Ping is not limited (owner-only, one session per organiser).
- [x] N7 Server tests: in/out of radius, accuracy allowance (and its cap), flag off = 404, blocked, already a
      member, re-ping moves the location, non-owner can't ping, bad-accuracy ping rejected, uniform failure.
      Spread over `NearbyCreateTests`, `NearbyPingTests`, `NearbyListTests`, `NearbyJoinApiTests`,
      `NearbyLimiterTests`, `NearbyActiveSessionTests` (CI only).
- [~] N8 Update `docs/api-reference.html` and the mock-up (replace pin/range with switch + radius; show several rows).
      Done: `docs/api-reference.html` (create fields, Ping, list, join) and `docs/database-schema.html` (new
      columns). The N3b 409 and `POST /sessions/{id}/end` are documented too. Still to do: the mock-up
      `.ai/mockups/create-flow.html`.
- [~] N9 iOS: Create switch + radius, Ping button (start + map screen), list on main screen, confirm sheet, join.
      Written on branch `nearby-join-ios` (stacked on `nearby-join-server`), **not built or run**: no Swift
      toolchain here and `AGENTS.md` leaves verification to CI/Xcode. `NearbyJoinViews.swift` (new),
      `LocationManager` (when-in-use permission, `currentFix()`, `createSession(nearby:)`, `joinNearby`,
      `pingNearby`, `endSession`, the 409 as `activeSessionExists`), `EventsStore` (position-aware list, polls
      every 3 minutes while the app is in front, and on pull-down), `ContentView` (signs, join sheet, Create wiring),
      `CreateSessionView`, `SessionMapScreen` (join-location button: tap = Ping, hold = settings / End).
      Deviations from the plan, because the app leaves old sessions on relaunch (`leaveStaleSessions`) and leaving
      archives a session once the last runner goes: (1) no persistent "your active session" card on the start
      screen; the 409 raises an "End it and create this one" prompt instead, and keeps the Create entries.
      (2) The map's Ping/End controls show only for a session this device created nearby in this run (remembered in
      memory), which also avoids turning nearby on for a session with a code-style title. Joiners with no location
      permission see a "Find runs near you" sign that asks for it; with it denied they see nothing.
- [ ] N10 Android: the same.
- [x] N3b One active session per organiser: 409 on create (events flag only); "active" = not archived and within event
      length; `POST /sessions/{id}/end` (owner only, archives for everyone); tests for each.
      `SessionStore.FindActiveOwnedSessionId` / `EndSession`, `SessionsController.CreateSession` / `EndSession`.
      409 body carries `sessionId` of the existing session. End archives, marks every member as left (so it leaves
      their lists), drops live positions, keeps replay; owner only (403, same for unknown ids), 404 without `events`,
      idempotent. The controllers now read the injected `TimeProvider` so expiry is testable. Tests in
      `NearbyActiveSessionTests.cs` (CI only). The check-then-create is not atomic and older builds bypass it, as
      the decision above accepts.
- [~] N9b Clients: Create disabled with explanation when an active session exists; End session (confirm) on the
      start-screen card and map-screen sheet. iOS: End session (with confirmation) is in the map's join-location
      menu and settings sheet; Create is not disabled but answers a 409 with an "End it and create this one"
      prompt (see N9). Android not started.
- [~] N11 Privacy copy: Create explanation, join confirmation, Help text. iOS has the Create explanation, the
      join sheet ("who sees what, when") and an updated when-in-use purpose string in `Info.plist`. Still to do:
      `Help.md` and the Android strings.

## Open questions

1. Should an organiser see who joined by proximity vs by code? (Probably not; members are members.)
2. Later, optional: should the join location follow the organiser while they track? Left out on purpose.

---

## Handover (30 October 2026)

Written so work can resume from a fresh session. Everything below was true when it was written; check the PRs
(`gh pr list`) before trusting the state.

### Where the code is

All of it is on three open PRs against `staging`. The two client branches are **stacked on the server branch**, so
until #254 merges their diffs also show the server commits.

| PR | Branch | Contains | CI at time of writing |
|---|---|---|---|
| #254 | `nearby-join-server` | Server API, tests, API and schema docs, this plan's server ticks (N1 to N8, N3b) | Server .NET: build, publish and tests **pass** |
| #255 | `nearby-join-ios` | iOS client (N9, iOS half of N9b and N11) | Server job passes. **Nothing builds iOS** |
| #256 | `nearby-join-android` | Android client (N10, Android half of N9b), `NearbyWireTest` | Server job passes. Android Gradle runs `assembleDebug` only: it compiles, it does **not** run unit tests |

Merge order: #254 first, then #255 and #256 in either order. Both client PRs edit the task list in this file (N9,
N9b, N10, N11), so expect a small conflict on the second one. #252 and #253 are unrelated open PRs.

### Task status

| Task | State |
|---|---|
| N0 decisions, N1 title vs code, N2 schema and create fields | Done (server) |
| N3 Ping, N4 list, N5 join, N6 rate limits | Done (server) |
| N3b one active session, End session | Done (server) |
| N7 server tests | Done: `NearbyCreateTests`, `NearbyPingTests`, `NearbyListTests`, `NearbyJoinApiTests`, `NearbyLimiterTests`, `NearbyActiveSessionTests` |
| N8 docs | Partly: `docs/api-reference.html` and `docs/database-schema.html` done. **`.ai/mockups/create-flow.html` still shows the old pin and range design** |
| N9, N9b iOS | Written, **never built or run** |
| N10, N9b Android | Written, **never built (CI compile pending) or run** |
| N11 privacy copy | iOS: Create text, join sheet, `Info.plist` when-in-use string. **Not done: `Help.md` (iOS), Android help text** |

### What was built, and where

- **Server:** `server/NearbyJoin.cs` (rules, radii, accuracy forgiveness), `NearbyLimiter.cs`;
  `SessionStore.cs` (`UpdateNearby`, `FindNearbySessions`, `JoinNearbySession`, `FindActiveOwnedSessionId`,
  `EndSession`, five `app_sessions` columns); `SessionsController.cs` (`PUT /sessions/{id}/nearby`,
  `POST /nearby/sessions/{id}/join`, `POST /sessions/{id}/end`, nearby fields and the 409 on `POST /sessions`);
  `EventsController.cs` (`GET /me/events/nearby?lat=&lon=&accuracy=`). Wire shapes: `docs/api-reference.html`.
- **iOS:** `NearbyJoinViews.swift` (new), `LocationManager.swift` (when-in-use permission, `currentFix()`, nearby
  calls, `activeSessionExists` error), `EventsStore.swift`, `ContentView.swift`, `CreateSessionView.swift`,
  `SessionMapScreen.swift`. Xcode uses synchronized folders, so new Swift files need no project edit.
- **Android:** `location/NearbyFix.kt`, `nearby/NearbyJoin.kt`, `ui/nearby/NearbyJoinSheets.kt`, `AppViewModel.kt`,
  `EventsModel.kt`, `DotWatcherApi.kt` / `DotWatcherRepository.kt`, `SessionScreen.kt`, `LiveMapScreen.kt`.

### Behaviour worth knowing

- Everything is behind the `events` feature flag (admin Users table checkbox). Without it the new endpoints are 404,
  and `POST /sessions` ignores the nearby rules. Older builds are unaffected.
- Limits: 120 list requests and 20 join attempts per account per hour (429). A request with no position gets the old
  placeholder event and is never limited.
- The radius test forgives GPS error: `distance - joiner accuracy - meeting-point accuracy <= radius`, each accuracy
  capped at 50 m, a missing accuracy counts as 0. A join location needs accuracy of 100 m or better.
- Every refusal on nearby join (too far, ended, switched off, unknown, blocked) is the same 404. Blocked is 404 here,
  not the invite join's 403, so a blocked person can't tell the session exists.
- One active session per organiser applies only to `events` accounts: "active" means owned, not ended and within its
  event length (default 24 h). The 409 body carries the existing `sessionId`. The check is not atomic and an older
  Production build can bypass it.
- The apps refresh the nearby list on open, on returning to the front, on pull-down, and every **3 minutes** while
  the app is visible and not tracking. Each refresh takes a one-off GPS fix.

### Deliberate departures from the plan above

- **No persistent "your active session" card on the start screen.** Both apps leave old sessions on relaunch
  (`leaveStaleSessions` on iOS, `restoreActiveSession` on Android), and leaving archives a session once the last
  runner goes, so a card would rarely have anything to show. A 409 on Create shows "End it and create this one"
  instead and keeps what was typed.
- **Map controls only for a session this phone created nearby in this run** (kept in memory). That also avoids
  turning nearby on for a session whose title is a code-style name, which would be shown to strangers.
- **iOS** Pings on a tap of the map's join-location button (hold for settings and End). **Android** opens the sheet.
- iOS asks for when-in-use location from a "Find runs near you" sign. Android already asks at launch.

### Verify before shipping (nothing here has been run)

1. Read CI on all three PRs. Run the Android unit tests locally or in Android Studio (`./gradlew testDebugUnitTest`);
   CI does not.
2. Build iOS in Xcode. Likeliest compile trouble: the `ContentView` alert using `presenting:`, `Menu(primaryAction:)`
   in `SessionMapScreen`, and `LocationManager` using the new file's types.
3. Likeliest Android compile trouble: `PullToRefreshBox` and its opt-in, `FilterChip`, `HorizontalDivider`,
   `nearbyFix::current` as a suspend reference.
4. Two-phone test with the `events` flag on both accounts: create nearby on phone A, see it on phone B (location
   allowed), join, move the join location, switch off, End session, then create again. Also check: a joiner outside the
   range gets the "can't join from here" message; a second Create while one is active shows the prompt; location denied
   or precise location off shows the explanation; pull-down refreshes.

### Still to do

- Update `.ai/mockups/create-flow.html` (switch and range instead of the pin and map; show several nearby rows).
- Help text: `ios/.../Help.md` and the Android equivalent. Check the App Store and Play location declarations, since
  the apps now use location on the start screen and not only while tracking.
- Refresh the stale parts of `docs/database-schema.html` (`max_length_hours`, `archived_at`, the old name rule).
- Server follow-ups, all optional: a stale-fix check on Ping (the request has no timestamp); a `title` field on the
  Ping call so an existing session can be turned nearby safely; a rate limit on Ping; contract tests for the new
  endpoints in `ContractTests.cs`; remove the placeholder event once nothing depends on it.
- iOS has no tests of its own for this, and there are no UI tests on either client.
- The two open questions above are still open.

### Working notes for the next session

- `AGENTS.md`: do not build, publish or test locally; CI does it. The machine this was written on (Windows) has no
  Swift or Android toolchain, so client code was written without compiling it.
- Server tests: the test factory enables only `crew` for everyone, so nearby tests set `EnabledForAll = ["events"]`.
  With `events` on, a user can have one active session, so a test that needs two sessions needs two organisers.
- Branch from `nearby-join-server` for client work; branch from `staging` for anything unrelated.
