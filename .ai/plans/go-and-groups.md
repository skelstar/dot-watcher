# Go and groups — Plan

*DotWatcher — plan, November 2026. Status: idea agreed in principle, nothing built. Two mock-ups come first (step 0).
Decisions marked **Open** below need an answer before the step that depends on them.*

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
   *Answer:* the main-screen tile works with or without tracking, so this is mostly a policy question; mock it up
   first (step 0b).
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
  position to find nearby sessions, so the tile can show before pressing Go. This bears on the joining-before-tracking
  decision below.
- **One entry code per group, changeable by the organiser.** Not one per entrant, for now. Changing it doesn't remove
  anyone who already joined.
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
- **Open — joining before tracking.** Since the tile is on the main screen and doesn't need tracking, joining before
  Go comes almost for free. Open: is that allowed, and can you join from an invite link without being in the region
  (race sign-up the night before, spectators)? Mock it up (step 0b) before deciding.

---

## Steps

### Step 0 — Mock-ups

- [x] **0a. Join a group from a main-screen tile.** Done: [`go-and-groups-join.html`](../mockups/go-and-groups-join.html). Show: someone tracking on the Go map goes back
      to the main screen; a tile is there because they are in a group's capture region; tapping it gives the consent
      sheet (group name, rule in plain words, end time), with the **entry code** field when the group asks for one, and
      the wrong-code state; back on the Go map after joining (group chip, time left, Leave); the group ending. Also
      the organiser side: creating a group with a rule, a time limit and "Ask for a code to join", and setting the
      region with Ping.
- [ ] **0b. Joining before tracking.** Alternatives side by side: (1) not allowed, the tile only joins once you are
      tracking; (2) join from the tile or an invite link, and the group waits for you to press Go; (3) spectator join
      (watch the group without tracking). Use it to settle the Open decision.
- [ ] Try alternative names for "group members" in both mock-ups.

### Step 1 — Server: group members count in casual, by the group's rule

Smallest change that proves the idea. Uses existing named sessions as groups; no new tables yet. Existing sessions
have no rule, so they all use **Everyone**; the session owner is the organiser.

- [ ] In `SessionStore.Casual.cs`, where the live view builds `crewLabels` from `GetCrewOverview`, also add the
      viewer's **group members** under each active group's rule: other members of active named sessions
      (`session_members`, both `left_at IS NULL`, session not archived, within its event length). Label: the member's
      `display_name` in that session.
- [ ] Respect blocks (already applied by `ForViewer` via `GetBlockedPartyIds`) and `VisibleToCrew`.
- [ ] Gate behind a feature flag (new `groups` flag in `FeatureFlags.cs`, per-account like `crew`) so installed builds
      and accounts without it see no change.
- [ ] Unit tests: a group member is named at any distance; a blocked member is hidden; a member who left is no longer
      named; an archived or expired session no longer counts.

Result: someone in casual sees their group by name, and the group sees them, without anyone switching what they track
against.

### Step 2 — Phones: join a group from a main-screen tile

Built from mock-up 0a.

- [ ] Server: capture region on a group (reuse nearby-join's meeting point, radius and accuracy allowance); the
      nearby lookup returns groups whose region contains the caller's position; optional entry code on the group,
      checked on join (hashed, rate-limited like `CrewCodeLimiter`), and the tile says whether a code is needed.
- [ ] iOS and Android: group tiles on the main screen while in a region; join with the consent sheet (plus the code
      when asked), without switching the tracking target, and back to the Go map if tracking. Invite code join as a
      fallback.
- [ ] Group chip on the map showing the active group(s), time left, and Leave.
- [ ] Start screen: Go and "Who can see me" replace the four cards (Private / Crew / Group / Race). Coordinate with
      the `android-start-screen-design` branch.
- [ ] Android: `./gradlew :app:compileDebugKotlin` before pushing.

### Step 3 — Groups as their own thing; rules; events as run slices

- [ ] New `groups` table (or extend `app_sessions`): name, owner, invite code, `starts_at`, `ends_at` (required),
      visibility rule (`everyone` / `race`), capture region, entry code hash (nullable), settings (update interval,
      satellite alerts).
- [ ] `group_members`: group id, user id, display name, role (member / organiser), joined/left. Add spectator if 0b
      says so.
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
