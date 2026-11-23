# Go and groups — Plan

*DotWatcher — plan, November 2026. Status (24 November 2026): mock-ups done; step 1 and the server half of step 2 built
and green in CI but **not merged**; the Android half of step 2 mostly built (not tried on a device); iOS not started. **Start with
[Picking this up again](#picking-this-up-again) below.** Decisions marked **Open** need an answer before the step that
depends on them.*

## Picking this up again

Everything is in a stack of open PRs, each based on the one before (merge from the bottom; GitHub retargets the next
one when its base branch is deleted, otherwise retarget it to `staging` by hand):

| PR | Branch | What | State |
| --- | --- | --- | --- |
| #379 | `go-and-groups-plan` | This plan and both mock-ups | Docs only |
| #386 | `groups-casual-visibility` | Step 1: people in the same active session see each other by name in casual (`groups` flag) | CI green, not merged |
| #387 | `groups-entry-code` | Step 2 server: entry codes on nearby sessions | CI green, not merged |
| #388 | `android-groups-join` | Step 2 Android, part 1: join a nearby group without starting tracking (based on #387) | Compiles, 91 unit tests pass locally; not tried on a device |
| #396 | `groups-ends-at` | Step 2 server: each group's end time (`endsAt`) on memberships and the nearby list (based on #387) | CI green, not merged |
| #397 | `android-groups-part2` | Step 2 Android, part 2: join while out, group chip, end times, "Join and Go now", organiser codes (based on #388) | Compiles, 99 unit tests pass locally; not tried on a device |

To try it end to end: deploy the server stack, then grant `groups` to a test account (`PUT /api/admin/users/{id}/features`,
body `{ "features": ["groups"] }`, admin token). Without the flag, nothing changes for anyone.

**Next, in order:**

1. ~~**Server: send each group's end time**~~ Done in #396.
2. ~~**Android, part 2**~~ Done in #397. Still open on Android: invite-code join and the organiser's own session
   following the groups flow, and the start-screen redesign.
3. **Try both Android PRs on a device** against a server with the stack deployed and `groups` granted.
4. **iOS:** the same as Android parts 1 and 2 (`NearbyJoinViews.swift`, `EventsStore.swift`, `ContentView.swift`,
   `CasualViews.swift`, `LocationManager.swift`). iOS doesn't read the account's `features` yet either.
5. **Invite links** for groups (join from anywhere, e.g. the entry email); decide whether they ask for the entry code.
6. Step 3 onwards.

**Things to know:**

- Only the **nearby-tile join** follows the groups flow so far. Joining by invite code, and an organiser's own session,
  still go to consent and track in the session, on both phones.
- Android used to leave every session on relaunch; for groups accounts it now keeps them (`decideRestore`
  `keepMemberships`), so a group joined ahead of Go survives.
- The `groups` flag is checked for the **viewer** only (#386): if you have it and a fellow member doesn't, you see them
  by name and they see you as before. Members of a session already see each other by name in the session itself.

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
- [ ] Invite links for groups that work outside the region (e.g. `dotwatchr.app/g/<slug>`), opening the join sheet
      with the code field; joined from home shows the card on the main screen on the day.

**Android** (part 1 is **#388**, part 2 is **#397**; neither merged nor tried on a device)

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
- [ ] Invite-code join and the organiser's own session follow the groups flow too.
- [ ] Start screen: Go and "Who can see me" replace the four cards (Private / Crew / Group / Race). Coordinate with
      the `android-start-screen-design` branch.
- [ ] Before pushing: `cd android && ./gradlew :app:compileDebugKotlin :app:testDebugUnitTest`.

**iOS** (not started)

- [ ] Everything in the Android list above. iOS doesn't read `features` yet either.

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
