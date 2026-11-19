# Go and groups — Plan

*DotWatcher — plan, November 2026. Status: idea agreed in principle, nothing built. Decisions marked **Open** below
need an answer before the step that depends on them.*

Goal: every time someone goes out, they press **Go** and are tracking. That is the only way to track. Races, events
and groups stop being things you track *in*; they become **groups** that only control who can see you. Joining a group
gives everyone in it **temporary crew status** with each other: they see you by name at any distance, for as long as
the group is active.

Related: [`casual-tracking.md`](casual-tracking.md) (casual is the base this builds on),
[`crew-management.md`](crew-management.md), [`nearby-join.md`](nearby-join.md),
[`session-update-interval.md`](session-update-interval.md), [`satellite-alerts.md`](satellite-alerts.md),
mock-up [`crew-main-screen.html`](crew-main-screen.html).

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
- **A simpler start screen.** Go, "who can see me", and "join a group" replace the four cards.
- **Groups are cheap.** A running club, a race and "me and two mates today" are the same thing.

## Where it gets harder (and how this plan answers each)

1. **Races need more than visibility**: a start time, a time window, results or ordering, a recording of the event.
   *Answer:* a group has a time window. An event's recording is each member's run **cut to that window**, not
   positions owned by the session. See step 3.
2. **Joining shouldn't require tracking.** People sign up for a race in advance; spectators and watch-link viewers
   never track. *Answer:* group membership exists on its own. "Press Go, then join" is the common path, not the only
   one. A member who isn't tracking simply has no dot.
3. **Privacy.** Temporary crew status means strangers in a big event see you by name at any distance. *Answer:*
   consent on join (who will see you, until when), leave at any time, visibility ends automatically when the group's
   window ends, blocking still wins.
4. **Old app versions** post to named session ids. *Answer:* the server keeps accepting those posts, and treats a post
   to a named session as "a casual run plus membership of that group" (step 4). Old endpoints stay until installed
   builds have moved on.
5. **Per-session settings** (update interval, satellite alerts) assume a session with an owner. *Answer:* they become
   group settings (owner sets them; members' phones read them while in the group) or account settings.

---

## Decisions taken

- **Go is the only way to track** (end state). Casual is the base; "casual" stops being a user-facing word.
- **Groups only control visibility.** They never own positions in the new model.
- **Temporary crew** = inside an active group, members are treated exactly as crew by `CasualVisibility.ForViewer`:
  named, any distance, routes offered. Same reciprocal rules as crew (`VisibleToCrew` off hides you from them too).
- **Membership is independent of tracking.** You can join before going out, while out, or only to watch.
- **Do it in steps**, each shippable and useful alone. Step 1 is server-only and tests whether the idea feels right.

## Open decisions

- **Open — does temporary crew follow the crew rules from the crew mock-up?** Crew today is mutual (`crew_links` is
  symmetric) and per account. Temporary crew is naturally mutual (everyone in the group) and per group. Proposal: yes
  mutual; per group; does *not* create a lasting `crew_links` row.
- **Open — does the "visible to crew" switch also cover groups,** or does a group get its own switch at join time?
  Proposal: one switch per group, defaulting to on when you join (joining is the consent).
- **Open — what does a non-crew stranger see of someone in a group?** Today: grey dot with a lock. Proposal: keep the
  grey dot, drop the lock (being in a group is no longer special).
- **Open — group window.** Fixed start/end set by the owner, or "until the owner ends it" with a cap (like the casual
  auto-stop)? Races want fixed; casual groups want open-ended. Proposal: both, owner chooses, hard cap of 24 h.
- **Open — big events.** Is there a size above which members see each other as grey dots unless crew (so a 500-person
  race isn't 500 names)? Proposal: defer; start with small groups.

---

## Steps

### Step 1 — Server: group co-members count as crew in casual

Smallest change that proves the idea. Uses existing named sessions as "groups"; no new tables.

- [ ] In `SessionStore.Casual.cs`, where the live view builds `crewLabels` from `GetCrewOverview`, also add the
      viewer's **co-members of active named sessions** (`session_members`, both `left_at IS NULL`, session not
      archived, within its event length). Label: the member's `display_name` in that session.
- [ ] Respect blocks (already applied by `ForViewer` via `GetBlockedPartyIds`) and `VisibleToCrew`.
- [ ] Gate behind a feature flag (new `groups` flag in `FeatureFlags.cs`, per-account like `crew`) so installed builds
      and accounts without it see no change.
- [ ] Unit tests in the `CasualVisibility` tests: co-member is named at any distance; blocked co-member hidden;
      left member no longer named; archived session no longer counts.

Result: someone in casual sees their group by name, and the group sees them, without anyone switching what they track
against.

### Step 2 — Phones: "Join a group" from the Go map

- [ ] iOS and Android: on the casual (Go) map, a "Join a group" action (invite code, and nearby join from
      `nearby-join.md`) that adds membership **without** switching the tracking target.
- [ ] Consent sheet on join: "Everyone in *name* will see you by name until *time*."
- [ ] Group chip on the map showing the active group(s), with Leave.
- [ ] Start screen: Go button, "Who can see me", and "Join a group" replace the four cards (Private / Crew / Group /
      Race). Coordinate with the `android-start-screen-design` branch.
- [ ] Android: `./gradlew :app:compileDebugKotlin` before pushing.

### Step 3 — Groups as their own thing; events as run slices

- [ ] New `groups` table (or extend `app_sessions`): name, owner, invite code, `starts_at`, `ends_at`, kind
      (casual / event / race), settings (update interval, satellite alerts).
- [ ] `group_members`: group id, user id, display name, role (member / organiser / spectator), joined/left.
- [ ] Event recording and replay: for each member, positions from their `casual_runs` that fall inside the group
      window and their membership period. No copying of positions.
- [ ] Race extras (start time, ordering, results) built on the slices. Design separately when a race is wanted.
- [ ] Retention: event slices may need to outlive casual's short retention. Decide whether joining an event pins the
      member's run data for the event's retention period.

### Step 4 — Retire named-session tracking for new clients

- [ ] New builds never post to a named session id.
- [ ] Server: a post to a named session id from an old build is stored as a casual position (start a run if none) plus
      ensures membership; `CopyToCasual` and the separate `location_updates` path become a compatibility shim.
- [ ] Remove the shim once installed builds below the cut-off version are negligible.
- [ ] Clean-up: `inSession` lock flag, `stopped_by_user` copy guard, per-session `GetRunStartTimestamp` uses.

---

## Risks

- **Data model migration** (step 3/4) is the real cost: positions are keyed by `session_id` in `location_updates`, and
  recordings, merge, admin lists and routes all assume it. Steps 1 and 2 avoid touching it.
- **Cross-pod reads.** Co-member lookups run per live read; keep them in the same connection and cache per request,
  like `GetCrewOverview`.
- **Confusing middle period.** Between steps 2 and 4 both models exist. Keep old-build behaviour unchanged behind the
  flag.
