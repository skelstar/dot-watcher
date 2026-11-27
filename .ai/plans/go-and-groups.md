# Go and groups — Plan

*DotWatcher — plan, November 2026. Status (25 November 2026): mock-ups done; step 1 and step 2 built. **Server and admin
released** (#411). **Both phone stacks merged into `staging`** and going out as **1.4.10** on TestFlight and Play
internal; `groups` is still granted per account only. **Start with
[Picking this up again](#picking-this-up-again) below.** Decisions marked **Open** need an answer before the step that
depends on them.*

## Picking this up again

**Server and admin** (#386, #387 with #396, #407, #410) were released to `main` by #411 (24 Nov). **The phone stacks**
were merged into `staging` on 25 Nov and released to `main` by #413, built as **1.4.10** (#414 bumped both apps; iOS
TestFlight needed #415, which signs the archive for distribution):

| PR | Branch | What | Based on | State |
| --- | --- | --- | --- | --- |
| #386 | `groups-casual-visibility` | Step 1: people in the same active session see each other by name in casual (`groups` flag); per-account grants cached 30 s | `staging` | Merged into `staging` |
| #387 | `groups-entry-code` | Step 2 server: entry codes; includes #396 (`endsAt`) | `staging` | Merged into `staging` |
| #407 | `groups-owner-flag` | Step 2 server: `isOwner` (and owner-only `nearby`) on memberships | `staging` | Merged into `staging` |
| #410 | `admin-account-features` | Admin: per-account feature chips in the Users table; Groups label | `staging` | Merged into `staging` |
| #411 | `staging` | Release of all the above (and #403-#405) to `main` | `main` | Merged |
| #388 | `android-groups-join` | Android part 1: join a nearby group without starting tracking; entry code; "You're in" cards; groups survive relaunch | `staging` | Merged |
| #397 | `android-groups-part2` | Android part 2: join while out, group chip, end times, "Join and Go now", organiser codes | `staging` | Merged |
| #408 | `android-groups-flows` | Android: invite-code join and creating are group joins; organiser card (Ping, code, End group) | `staging` | Merged |
| #398 | `ios-groups` | iOS: everything in #388 and #397, plus two fixes found in the simulator | `staging` | Merged (conflict with `staging` in `requestOneShotLocation` resolved by keeping the groups version) |
| #409 | `ios-groups-flows` | iOS: the same as #408 | `staging` | Merged |
| #413 | `staging` | Release of the phone stacks to `main` | `main` | Merged |

Stacks were merged bottom-up with normal merge commits (not squash), so later PRs in a stack didn't show earlier
commits again. Keep doing that for future stacks.

**Trying it (after #411 is deployed):**

1. Admin → Users tab: **Groups** shows under the kill switches (On) and as a chip per account. Tap **Groups** on a
   couple of test accounts only. **Don't** add `groups` to `Features:EnabledForAll` until the new phone builds are out.
   Without a grant nothing changes for anyone; older builds are unaffected (additive fields, API version unchanged,
   one nullable column).
2. Build the phones from the tops of their stacks, **pointed at prod**:
   - iOS: `ios-groups-flows`, scheme **"DotWatcher (Device Prod)"**.
   - Android: `android-groups-flows`, `./gradlew :app:installRelease` (the `release` build type uses prod and the
     release signing config; `debug`/`device` point at a local server).
3. Sign in on each phone with a granted test account. One creates a nearby group with "Ask for a code to join"; the
   other joins it.

*Locally instead* (no deploy needed): run the server from `staging` with
`Features__EnabledForAll__0=crew …__1=casual …__2=events …__3=groups dotnet run --urls "http://*:8080"`; phones on
Android `./gradlew :app:installDevice` (uses `DEVICE_API_BASE_URL` in `local.properties`) and iOS scheme
**"DotWatcher (Device Local)"** (`jakkuu.local:8080`). Needs Docker (Postgres) up.

**Testing on two phones** (both signed in with accounts that have the Groups chip on, both on the test builds):

*Organiser's phone (A)*
- [ ] Create → "Let people nearby join" on → "Ask for a code to join" appears; set a code; Create stays disabled until
      there's a title and the code is 4-8 letters or numbers.
- [ ] Start screen shows **YOUR GROUP · ENDS hh:mm · … LEFT** with the group's name and "People near your join
      location see it on their start screen. You don't need to be out." No sharing prompt, no session map.
- [ ] **Ping here** says "Join location set here" and keeps the range.
- [ ] **Entry code** shows the current code; **Change code** works; **Remove code** gives "Anyone nearby can join
      now". People already in stay in.
- [ ] Press **Go**: the Go map has "‹ Home" and the group chip (name, time left). **‹ Home** shows "You're out · Back
      to the map"; the card says you're out.

*Joiner's phone (B), within range of A's join location*
- [ ] Start screen shows A's group as a tile, with "· Code needed" if A set a code (pull down to refresh).
- [ ] Join sheet: group wording, "Until the group ends at hh:mm", code field. A **wrong code** shows "That code doesn't
      match. Check with the organiser."; the **right code** (any case) joins.
- [ ] **Join group** (before Go): a **YOU'RE IN** card, "Not out yet. Nobody in the group can see you until you press
      Go." Or **Join and Go now**: joins, then opens "Before you go".
- [ ] Joining **by invite code** (Join → code) also lands on a YOU'RE IN card, with no sharing prompt.
- [ ] Joining **while out** (Go, then ‹ Home, then the tile) goes straight back to the Go map.

*Both*
- [ ] Both out: each sees the other **by name** on the Go map, at any distance, even though they aren't crew.
- [ ] The group chip opens **Your groups**: end time, Leave (B) or the organiser controls (A).
- [ ] Relaunch either app: the group is still there.
- [ ] B **Leave group**: A no longer sees B by name (and B no longer sees A). Or A **End group**: B's card goes; B's Go
      run carries on.
- [ ] A phone signed in with an account **without** the Groups chip sees none of this (no tile; old flows unchanged).

Results so far: **iOS, one phone against prod (24 Nov)**: creating a group and the YOUR GROUP card work. Two phones,
and anything on Android, still to do.

**Checked in the iOS simulator** (24 Nov, against a local server with the stack, before #409): tiles with "Code needed";
join sheet with group wording and end time; wrong code then right code; "Join and Go now" → "Before you go"; Go map
with "‹ Home" and the group chip; the organiser showing **by name** although not crew (step 1 end to end); "Your
groups" sheet; Home while out ("Back to the map", no session map); joining a second group while out; the group
surviving a relaunch; "Ask for a code to join" at create. Two bugs found and fixed in #398: the join sheet didn't
scroll (title cut off), and joining while out hung on "Joining…" (a one-off location request never answered while
tracking and standing still; now uses the tracked position, and one-off requests time out after 15 s). Android was
reviewed for the same hang: its one-off request already has a 10 s limit. Android was **not** run (the emulator run was
cut short by a full disk).

**Next, in order:**

1. ~~Merge #411~~ (done 24 Nov). Grant `groups` to test accounts from the admin page.
2. **Try 1.4.10 on devices** against prod, using "Testing on two phones" above. Done so far: iOS organiser card on one
   phone. Not yet: a second phone, invite-code join as a group join, and anything on Android.
3. ~~Merge the phone stacks and release the apps~~ (merged 25 Nov; 1.4.10 deploying). Once step 2 passes, consider
   `groups` for everyone.
4. **Invite links** for groups (join from anywhere, e.g. the entry email); decide whether they ask for the entry code
   (an invite code skips it today).
5. **Start-screen redesign** (Go and "Who can see me" replace the four cards; ties in with `android-start-screen-design`).
6. The **name** for "group members" (the only open design decision).
7. Step 3 onwards.

**Changes to consider** (ideas from using the apps, not yet scheduled). One line each: what, why, where it would go,
and **Maybe** or **Agreed**. When one is scheduled, move it into "Next, in order" or a step's checklist and delete it
here.

- **Agreed — say "tracking", not "out".** "You're tracking" is more accurate than "You're out". Applies to all app text that says someone is out, on both phones. *Where:* any time (text only); do it before
  `groups` goes to everyone. Found on `staging` (25 Nov):
  - "You're out" eyebrow above "Back to the map": iOS `ContentView.swift`, Android `SessionScreen.kt`.
  - YOU'RE IN card, "You're out, so everyone in the group can see you by name." / "Not out yet. Nobody in the group
    can see you until you press Go.": iOS `GroupsViews.swift`, Android `GroupCards.kt`.
  - Your groups sheet, "Everyone in these groups sees you by name while you're out.": same two files.
  - Join sheet, "…sees you by name on their map, wherever they are, while you're out." / "Not out yet? Join now and
    the group waits…": iOS `NearbyJoinViews.swift`, Android `NearbyJoinSheets.kt`.
  - Casual Go block, "1 of your crew is out now" / "N of your crew are out now": iOS `CasualViews.swift`, Android
    `CasualGoBlock.kt` (and its test, `CasualWireTest.kt`). Decide whether "out" about other people changes too
    ("3 of your crew are tracking").
  - iOS in-app help, `Help.md` ("Join while you're out", "…while you're out").
  - This plan's own wording ("join while out", "Both out") can follow once the app text changes.
- **Maybe — "‹ Home · 1 nearby" on the Go map.** Today "‹ Home" shows for every `groups` account, whether or not
  a group is in range (iOS `GoMapGroups` in `CasualViews.swift`, Android `onHome` in `DotWatcherApp.kt`). Hiding it
  unless one is in range was considered and **not** wanted: Home is also how an organiser reaches their YOUR GROUP
  card, and how someone tracking joins by invite code or creates a group. Instead, keep it always and add a count
  when groups are joinable nearby. *Cost:* the Go map would need the nearby list while tracking (it isn't fetched
  there today), so poll it now and then or reuse the casual refresh. *Where:* any time; pairs well with the
  start-screen redesign.

**Things to know:**

- For groups accounts, every way in now follows the groups flow on both phones: nearby tile, invite code, and
  creating (#408, #409). The organiser's controls live on their group's card, not a session map.
- Android used to leave every session on relaunch; for groups accounts it now keeps them (`decideRestore`
  `keepMemberships`). iOS likewise skips its "leave stale sessions" step and never auto-selects a group.
- The `groups` flag is checked for the **viewer** only (#386): if you have it and a fellow member doesn't, you see them
  by name and they see you as before. Members of a session already see each other by name in the session itself.
- Mixed groups: a groups tester in a session doesn't track *in* it, so old-build members don't see them on that
  session's map, only on the Go map. Expected while both models exist.
- **Every group currently behaves as "Everyone"** (everyone in it sees everyone by name). The Race rule (members see
  their crew and the organiser; the organiser sees everyone) is step 3.
- Merging into `main` triggers the server release workflow.

Goal: every time someone goes out, they press **Go** and are tracking. That is the only way to track. Races, events
and groups stop being things you track *in*; they become **groups** that only control who can see whom. Each group is
time-limited, and has rules for who sees whom (a casual group: everyone sees everyone; a race: you see your crew, the
organiser sees everyone).

**Group members** (working name; see Open decisions) are people who can see each other because of a group. It is like
crew, but it only controls visibility while the group is active: no friendship, nothing that lasts after the group
ends.

Related: [`casual-tracking.md`](casual-tracking.md) (casual is the base this builds on),
[`crew-management.md`](crew-management.md), [`nearby-join.md`](nearby-join.md) (the meeting point and radius that
capture regions grow from), [`session-update-interval.md`](session-update-interval.md),
[`satellite-alerts.md`](satellite-alerts.md), mock-up [`crew-main-screen.html`](crew-main-screen.html).

Per `AGENTS.md`, build/test verification is left to CI on each PR. Tick boxes as work lands: `- [ ]` → `- [x]`.

---

## Why

Today there are two ways to track, and the server glues them together:

- **Casual** is one permanent session (`SessionStore.CasualSessionId`). Tapping Casual starts tracking with nothing to
  create or join. Crew see you by name at any distance; strangers within 5 km see a grey dot
  (`server/CasualVisibility.cs`).
- **Named sessions** are a separate tracking target. Positions go to `location_updates` under the session id, and
  `LocationsController.CopyToCasual` copies each one into casual too, with `inSession: true` (the lock on the dot).
  That copy brings its own special cases: `stopped_by_user` so a named-session copy can't restart a run the user
  stopped for 12 hours, the demo-session skip, the "visible to non-crew" gate.

Both phones carry two tracking modes (casual against `CASUAL_SESSION_ID`, and named-session tracking) and the start
screen has four cards (Private / Crew / Group / Race) to explain them.

Turning it around, with casual as the base and groups as visibility on top:

- **One tracking mode on the phones.** Press Go once; join and leave groups while out without restarting tracking or
  splitting a track.
- **No copying.** A person's run (`casual_runs`) is the only record of where they went. `CopyToCasual`, the lock
  flag's reason to exist and most of the `stopped_by_user` logic go away (eventually; see step 4).
- **A simpler start screen.** Go and "who can see me" replace the four cards; a group appears as a tile there when
  you are in its region.
- **Groups are cheap.** A running club, a race and "me and two mates today" are the same thing with different rules.

## Where it gets harder (and how this plan answers each)

1. **Races need more than visibility**: a start time, results or ordering, a recording of the event. *Answer:* every
   group has a time window. An event's recording is each member's run **cut to that window**, not positions owned by
   the session. See step 3.
2. **Joining before tracking.** People may want to sign up for a race in advance, and spectators never track.
   *Answer:* allowed (option B from mock-up 0b): join from the tile or an invite link at any time, and the group
   waits for you to press Go. Watch-only spectators come later.
3. **Passers-by joining a race.** Anyone in the region sees the tile. *Answer:* the organiser can require an entry
   code that only entrants have.
4. **Privacy.** Group members see you by name at any distance, so the group's rules matter. *Answer:* the rules are
   shown on the consent sheet when you join (who will see you, until when). You can leave at any time, visibility ends
   automatically when the group's time runs out, and blocking still wins.
5. **Old app versions** post to named session ids. *Answer:* the server keeps accepting those posts, and treats a post
   to a named session as "a casual run plus membership of that group" (step 4). Old endpoints stay until installed
   builds have moved on.
6. **Per-session settings** (update interval, satellite alerts) assume a session with an owner. *Answer:* they become
   group settings (owner sets them; members' phones read them while in the group) or account settings.

---

## Decisions taken

- **Go is the only way to track** (end state). Casual is the base; "casual" stops being a user-facing word.
- **Groups only control visibility.** They never own positions in the new model.
- **Groups are time-limited.** Every group has an end time set by its owner; when it passes, members stop seeing each
  other through the group. (Length options and a hard cap: settle in the mock-ups.)
- **Group members are like crew, but visibility only.** Inside an active group, the group's rules decide who is named
  to whom. Being in a group never creates a `crew_links` row, and the same reciprocal rules as crew apply
  (`VisibleToCrew` off hides you from them too).
- **Each group has a visibility rule: who sees whom.** The rule is a property of the group, chosen by the owner when
  creating it. Starting set:

  | Rule | Members see | Organiser sees | Example |
  | --- | --- | --- | --- |
  | **Everyone** | every other member, by name | everyone | a run with mates, a club outing |
  | **Race** | their own crew and the organiser, by name (other members as anyone else would: a grey dot or nothing) | every member, by name | a race or a large organised event |

  Crew always see each other by name regardless of the group (that is crew's normal rule). More rules can be added
  later; the server evaluates the rule, not the phones.
- **You join from the main screen, not the Go map.** A group has a **capture region** (a meeting point and radius,
  set by Ping, as in `nearby-join.md`). While your phone is inside the region, the group shows as a **tile on the main
  screen**, like the nearby-join tile does today. Someone already tracking leaves the map for the main screen to join
  (tracking carries on), joins, and goes back to the map. The Go map itself never offers groups. Being in the region
  only shows the tile; it never joins you on its own.
- **The organiser can require an entry code.** A group option, set at creation: "Ask for a code to join". Then the
  tile still shows to anyone in the region, but joining needs a code the organiser gave to entrants (on the entry
  confirmation, a race number sheet, a club message). That stops someone passing by from tapping the tile and joining
  a race. Without the option, the tile joins with one tap and the consent sheet (fine for an informal group run).
- **The region check uses the phone's current position, tracking or not.** The main screen already asks for a
  position to find nearby sessions, so the tile can show before pressing Go.
- **You can join before you press Go; the group waits** (option B in mock-up 0b). Join from the tile, or from an
  invite link anywhere (the entry email the night before), with the entry code if the group asks for one. You are a
  member straight away but have no dot until you press Go, so nobody in the group sees you until then. The main screen
  shows "You're in · starts in 25 min" with Leave and Go. The join sheet also offers "Join and Go now". Joining early
  and never pressing Go is fine: you simply never appear, and membership ends with the group.
- **Watch-only spectators come later** (option C in mock-up 0b), not in the first version. When they come: no code
  needed, the spectator sees what an entrant would see under the group's rule, nobody sees them, and the organiser
  decides whether a group allows watchers. Option A (join only once tracking) was rejected because it blocks race
  sign-up in advance.
- **One entry code per group, changeable by the organiser.** Not one per entrant, for now. Changing it doesn't remove
  anyone who already joined. 4-8 letters or digits, any case. **Stored as typed, not hashed** (built that way in #387):
  the organiser's card shows it again, and it's a shared code printed on a race sheet, not a password. Only ever
  returned to the organiser; guessing is capped by the nearby join limit (20 an hour) and having to be in the area.
- **The invite code skips the entry code** for now: someone given a session's invite code joins without it. Group
  invite links (step 2) must decide whether to ask for it.
- **Strangers see someone in a group as a plain grey dot, with no lock.** Being in a group is no longer special, so
  the `inSession` lock goes (normal stranger rules: both toggles on, within 5 km, start area hidden).
- **The tile shows from shortly before the group's start until its end.** Leaving the region after joining does
  nothing; you stay a member until you leave or the group ends. No hint on the Go map for now. (How long "shortly
  before" is: settle in the mock-ups.)
- **Race details:** members see the organiser by name; other members who aren't their crew follow the normal stranger
  rule (grey dot within 5 km, if both toggles are on).
- **Do it in steps**, each shippable and useful alone. The mock-ups come first.

## Open decisions

- **Open — name.** "Group members" is the working name for the people a group lets you see. Alternatives to try in the
  mock-ups. It needs to sit next to "crew" without being confused with it.

---

## Steps

### Step 0 — Mock-ups

- [x] **0a. Join a group from a main-screen tile.** Done: [`go-and-groups-join.html`](../mockups/go-and-groups-join.html). Show: someone tracking on the Go map goes back
      to the main screen; a tile is there because they are in a group's capture region; tapping it gives the consent
      sheet (group name, rule in plain words, end time), with the **entry code** field when the group asks for one, and
      the wrong-code state; back on the Go map after joining (group chip, time left, Leave); the group ending. Also
      the organiser side: creating a group with a rule, a time limit and "Ask for a code to join", and setting the
      region with Ping.
- [x] **0b. Joining before tracking.** Done: [`go-and-groups-before-go.html`](../mockups/go-and-groups-before-go.html).
      Alternatives side by side: (A) not allowed, the tile only joins once you are tracking; (B) join from the tile or
      an invite link, and the group waits for you to press Go; (C) spectator join (watch the group without tracking).
      Decided: B now, C later.
- [ ] Try alternative names for "group members" in both mock-ups.

### Step 1 — Server: group members count in casual, by the group's rule

Smallest change that proves the idea. Uses existing named sessions as groups; no new tables yet. Existing sessions
have no rule, so they all use **Everyone**; the session owner is the organiser.

Built in **#386** (CI green, not merged). New `SessionStore.Groups.cs`.

- [x] In `SessionStore.Casual.cs`, where the live view builds `crewLabels` from `GetCrewOverview`, also add the
      viewer's **group members** under each active group's rule: other members of active named sessions
      (`session_members`, both `left_at IS NULL`, session not archived, within its event length). Label: the member's
      `display_name` in that session. Crew keep their crew label. Casual and demo sessions never count.
- [x] Respect blocks (already applied by `ForViewer` via `GetBlockedPartyIds`) and `VisibleToCrew`.
- [x] Gate behind a feature flag (new `groups` flag in `FeatureFlags.cs`, per-account like `crew`; checked with
      `FeatureFlags.IsOnFor`) so installed builds and accounts without it see no change. Wired into `GET /casual/live`,
      casual `POST /location` and the casual route download.
- [x] Tests (`GroupsCasualTests`, 11): a group member is named at any distance; a blocked member is hidden; a member
      who left is no longer named; an ended session no longer counts; expiry via `IsGroupActive` (no fake clock in the
      API tests).

Result: someone in casual sees their group by name, and the group sees them, without anyone switching what they track
against.

### Step 2 — Phones: join a group from a main-screen tile

Built from mock-up 0a.

**Server**

- [x] Capture region: already there from nearby join (meeting point, radius, accuracy allowance, Ping, the nearby
      list and tile join). Nothing new needed.
- [x] Entry code (**#387**, CI green, not merged): `entryCode` on `POST /sessions` (nearby only, `groups` accounts
      only); owner-only `GET`/`PUT /sessions/{id}/entry-code` (null clears); nearby list sends `needsCode` to `groups`
      accounts and hides coded sessions from everyone else; nearby join takes `entryCode`, and a missing or wrong code
      is `403 { reason: "entry_code" }` only once every other check passes (otherwise the usual 404). New nullable
      `app_sessions.entry_code`. `EntryCodeTests` (18).
- [x] Each group's end time (**#396**, CI green, not merged): `endsAt` (created plus event length, default 24 h, UTC)
      on every membership (create, both joins, `GET /me/sessions`) and, for `groups` accounts only, on the nearby list.
      `SessionStore.GroupEndsAt` is the one rule, also behind `IsGroupActive`. `GroupEndsAtTests` (4).
- [x] Who organised a group (**#407**, CI green): `isOwner` on every membership, and owner-only `nearby`.
- [x] Per-account feature grants cached on the hot path (#386): `FeatureFlags.IsOnFor` no longer reads the database on
      every casual post and live read; 30 s cache, cleared at once by the admin features endpoint.
- [x] Admin **Groups** tab (`client/src/GroupsTab.tsx`, `AdminGroupsController`, `AdminGroupsTests`): every named
      session with organiser, status (active / expired / ended), nearby area, join location and entry code. Edit
      name, length, nearby, radius, join location (click a map, `JoinLocationPicker.tsx`; OpenStreetMap tiles, since
      LINZ is NZ-only) and code; End, Reopen, Delete; add a member by username (no distance or code check) or remove
      one. No organiser checks, so for test groups.
- [x] Finished groups: `GET /me/sessions` leaves out ended and expired ones; the hourly maintenance deletes them after
      `Groups:RetentionDays` (7), counted from when they were over, keeping any with recorded positions or a route
      (`PurgeFinishedGroups`, `FinishedGroupsTests`).
- [ ] Show the invite code on a nearby group's organiser card. Today the card only shows it for groups without nearby,
      so for a nearby group the tile is the only way in unless the organiser finds the code elsewhere.
- [ ] Invite links for groups that work outside the region (e.g. `dotwatchr.app/g/<slug>`), opening the join sheet
      with the code field; joined from home shows the card on the main screen on the day.

**Android** (**#388**, **#397**, **#408**; none merged nor tried on a device)

- [x] Read the account's `features` (sign-in, register, `GET /me`); `repository.hasFeature("groups")`.
- [x] Join from a tile without switching the tracking target (no consent screen, no tracking in the session).
- [x] Entry code: "· Code needed" on the tile, code field on the join sheet, wrong-code message.
- [x] Group wording on the join sheet ("Join group").
- [x] Join before Go: "You're in" card on the start screen with Leave (no time yet: needs `endsAt`).
- [x] Keep group memberships on relaunch (`decideRestore` `keepMemberships`).
- [x] Join while out (#397): "‹ Home" on the Go map, "You're out · Back to the map" tile on the start screen, back to
      the map after joining.
- [x] Group chip on the Go map (#397): newest group's name and time left; opens "Your groups" with end times and
      Leave.
- [x] End time on the "You're in" cards (#397), from `endsAt` (`groups/GroupTime.kt`).
- [x] "Join and Go now" on the join sheet (#397), before Go only.
- [x] Organiser (#397): "Ask for a code to join" in the create dialog; an Entry code section (change, remove) in the
      nearby sheet on their session map.
- [x] Invite-code join and the organiser's own session follow the groups flow too (#408): creating stays on the start
      screen; the organiser's card has Ping here, Entry code and End group (`endGroup` doesn't stop a Go run).
- [x] Group sign above Go (option D of the "Group button on the start screen" canvas,
      https://claude.ai/artifact/4mn7F9ANH1Ck5NnQTrHrub): one pink sign listing every group, a row each ("Yours ·
      9 h 6 m left", "Kiri's · 2 h left") with a gear. A row opens that group's sheet (the old card: organiser
      controls, or Leave). Replaces the "You're in" cards and Go's "With <group>" line (`GroupSign`, `GroupSheet`
      in `GroupCards.kt`). Not tried on a device.
- [ ] Start screen: Go and "Who can see me" replace the four cards (Private / Crew / Group / Race). Coordinate with
      the `android-start-screen-design` branch.
- [ ] Before pushing: `cd android && ./gradlew :app:compileDebugKotlin :app:testDebugUnitTest`.

**iOS** (**#398** and **#409**, not merged; #398 run in the simulator, #409 builds)

- [x] Read the account's `features` (sign-in, `GET /me`; kept in `UserDefaults`); `LocationManager.hasGroups`.
- [x] Join from a tile without selecting the session or asking to share.
- [x] Entry code: "· Code needed" on the tile, code field on the join sheet, wrong-code message.
- [x] Group wording on the join sheet, "Join group", "Join and Go now".
- [x] "You're in" cards with end time and Leave.
- [x] Groups survive a relaunch: no auto-select of the first membership, no leaving stale sessions, for groups
      accounts.
- [x] Join while out: "‹ Home" on the Go map, "You're out · Back to the map" on the start screen; the session map
      never covers a Go run.
- [x] Group chip on the Go map and the "Your groups" sheet.
- [x] Organiser: "Ask for a code to join" at create; Entry code section in the nearby settings sheet.
- [x] Invite-code join and the organiser's own session follow the groups flow too (#409).
- [x] Fixes from the simulator run (#398): scrolling join sheet; no hang getting a location while out.
- [x] Group sign above Go, as Android (`GroupSign`, `GroupSheet` in `GroupsViews.swift`; membership now reads
      `ownerDisplayName`). Run in the simulator.
- [ ] Start screen redesign (as Android).

### Step 3 — Groups as their own thing; rules; events as run slices

- [ ] New `groups` table (or extend `app_sessions`): name, owner, invite code, `starts_at`, `ends_at` (required),
      visibility rule (`everyone` / `race`), capture region, entry code hash (nullable), settings (update interval,
      satellite alerts).
- [ ] `group_members`: group id, user id, display name, role (member / organiser), joined/left. Leave room
      for a later `spectator` role (0b option C) and a per-group "allow watchers" setting.
- [ ] `CasualVisibility.ForViewer` takes the viewer's groups with their rules and roles, and decides who is named;
      unit tests per rule (Everyone; Race member sees crew and the organiser; Race organiser sees everyone).
- [ ] Event recording and replay: for each member, positions from their `casual_runs` that fall inside the group
      window and their membership period. No copying of positions. Under the Race rule, only the organiser gets the
      full replay; members get their crew's.
- [ ] Race extras (start time, ordering, results) built on the slices. Design separately when a race is wanted.
- [ ] Retention: event slices may need to outlive casual's short retention. Decide whether joining an event keeps the
      member's run data for the event's retention period.

### Step 4 — Retire named-session tracking for new clients

- [ ] New builds never post to a named session id.
- [ ] Server: a post to a named session id from an old build is stored as a casual position (start a run if none) plus
      ensures membership; `CopyToCasual` and the separate `location_updates` path become a compatibility layer.
- [ ] Remove the compatibility layer once installed builds below the cut-off version are rare.
- [ ] Clean-up: `inSession` lock flag, `stopped_by_user` copy guard, per-session `GetRunStartTimestamp` uses.

---

## Risks

- **Data model migration** (steps 3 and 4) is the real cost: positions are keyed by `session_id` in
  `location_updates`, and recordings, merge, admin lists and routes all assume it. Steps 1 and 2 avoid touching it.
- **Cross-pod reads.** Group member lookups run on every live read; keep them in the same connection and cache per
  request, like `GetCrewOverview`.
- **Capture region checks** run against each tracker's latest position; keep them cheap (bounding box first, as
  casual's 5 km filter does).
- **Confusing middle period.** Between steps 2 and 4 both models exist. Keep old-build behaviour unchanged behind the
  flag.
