# Ro-dot army — Plan

*DotWatcher — plan, November 2026. Status: drafted, not started. Nothing below is built.*

Goal: a separate service that runs "ro-dots" (robot runners). They send positions like real runners, so people see
them as **grey dots** on the casual map, and a person can add one as **crew** to practise the crew flow. The service
spawns ro-dots near people who are using the app so the map isn't empty. Related:
[`casual-tracking.md`](casual-tracking.md), [`crew-management.md`](crew-management.md),
[`nearby-join.md`](nearby-join.md), [`crew-feature-flag.md`](crew-feature-flag.md).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR.

---

## What the code already gives us (read 2026-11-10)

- **Grey dots are a casual feature.** `GET /casual/live` and the `POST /location` reply return `nearby` dots
  (`CasualVisibility.ForViewer`): anonymous lat/lon/heading, within 5 km of where the server last placed the viewer,
  only if the viewer **and** the dot both have "show me to people nearby" on, never within 300 m of the dot's own run
  start (unless they turned that off), stale after 10 min. A ro-dot that is a normal casual runner gets all of this
  for free, and **no client change is needed to draw grey dots**.
- **Casual reads come from one Postgres query** (`LoadCasualSnapshot`, `DISTINCT ON (runner_user_id)`, shared for
  5 s), so both servers (Staging and Production share one database) see the same dots without sharing memory.
- **Posting is `POST /location`** with a user bearer token to the fixed casual session id, after `POST /casual/start`.
  A 403 means "run closed, call start and retry once". Apps post every **15 s**.
- **Crew is mutual** (`crew_links`), made only by one-time 8-char codes (`crew_codes`), capped at `MaxCrewSize` = 50.
  Crew see each other named (nickname, else initials) at any distance.
- **Precedent for a synthetic runner:** the demo session (`EnsureDemoSession`, user `dw-demo`) is a placeholder
  account whose position is computed from the clock and never stored. Casual is different: a ro-dot must look like a
  normal poster, so it goes through the real casual write path.
- **Features are on unless an admin switches them off** (`FeatureFlags.IsOn`). A new bot switch must therefore default
  to **off** through config, not through that mechanism.
- **Deploy**: server and client are built into images and run on Tatooine (home k3s) via `server/scripts/deploy.sh`
  and `.deploy.yaml`. CI runs `dotnet restore/build/publish` and discovers .NET test projects.

---

## Decisions proposed (confirm or change)

1. **Ro-dots are marked, not disguised.** Every ro-dot is a real row with `users.is_bot = true`. The wire carries it
   (`NearbyDot.isRo`, and `isBot` on a crew member) so each client decides how to draw it. **Recommendation:** v1
   draws a ro-dot as the normal grey dot with a small robot mark when tapped (or a distinct ring), and crew practice
   runners are named "Ro-…" with a robot badge. Reasons: a user who learns the dots were fake loses trust in every
   dot; App Store review and the privacy policy are simpler when we can say "practice runners are labelled"; and the
   "N nearby" chip is a claim about real people. If you still want them unlabelled, the server flag lets us ship the
   labels later without a data change, but the chip count must at least exclude them. *(Open question 1.)*
2. **Same repo, own pod.** Code lives in `ro-dots/` in this repo (own Dockerfile and `.deploy.yaml`, like `server/`
   and `client/`) and deploys as its own Deployment on Tatooine. A separate cluster would work but adds nothing the
   pod doesn't give us, so the default is a pod in the existing cluster (own namespace). *(Open question 2.)*
3. **Ro-dots are ordinary accounts using the existing API.** Each registers (`/auth/register`, username prefix
   `rodot-`), logs in, calls `/casual/start` and posts `POST /location` every 15 s, exactly like a phone. No batch,
   ensure or clear endpoints: 40 ro-dots is under 3 requests a second, so batching buys nothing. The server derives
   "is a ro-dot" from the username prefix (as the simulator's `sim-` accounts are recognisable), and `/auth/register`
   refuses that prefix unless the admin bearer is sent, so people can't pose as ro-dots.
4. **One new endpoint only: where to spawn.** `GET /admin/casual/cells` (admin bearer) returns coarse cells with live
   opted-in runners. Nothing else new is needed to get grey dots. (Practice crew, below, needs its own endpoints.)
5. **Ro-dots fill gaps, they don't replace people.** Spawn only where real nearby density is below a threshold, and
   thin out as real runners appear.
6. **Server-side privacy rules still apply.** A ro-dot is subject to the same visibility rules as a person, so a
   viewer who hasn't turned on "show me to people nearby" sees no ro-dots either (reciprocal), exactly like real
   strangers.
7. **Short history for ro-dots.** At 15 s cadence, 50 ro-dots would add ~290k `location_updates` rows a day (~2M inside
   the 7-day retention) and no one will replay them. The existing hourly maintenance job (`PurgeCasualData`) deletes a
   ro-dot's positions after 1 hour instead of 7 days. No new table.
8. **Routes never come from real users' recordings.** Real people's tracks are not ours to replay as someone else.
   Sources: your own activities, OSM-derived routes, and hand-drawn GPX.
9. **Ambient ro-dots only exist in casual.** Never in a named session, never in "Happening near you", never in a race.
   Game ro-dots (see Future: games) are a separate kind that joins a session its players opted into.
10. **Look.** Initials **"XX"**, nickname **"X-bot-123"** (random number), and a distinct **ro-dot colour** (through the
    remote palette, so it can change without an app release). The colour is a label, so this is decision 1's
    "labelled" in practice. Open: grey dots stay grey with a small tell, or ro-dots get the colour even in the
    nearby layer. *(Open question 1.)*

---

## Architecture

```
                     ┌────────────────────────────┐
  phones / web ◄─────┤  dot-watcher-server        │◄──── Postgres (shared)
                     │  /casual/live, /location   │
                     │  /me/crew/*                │
                     │  /admin/casual/cells       │
                     └─────▲──────────┬───────────┘
        posts (as users) │          │ demand cells, practice requests
                     ┌─────┴──────────▼───────────┐
                     │  ro-dots service (k3s)     │  one replica, in-memory fleet
                     │  Spawner · Fleet · Routes  │  state is disposable
                     │  Movement engine · Poster  │
                     └────────────────────────────┘
```

**Server additions (small, all in `server/`)**
- `Bots:Enabled` (default `false`) in config. When off, the cells endpoint is 404 and the purge rule does nothing.
- "Is a ro-dot" = username starts with `rodot-`; `/auth/register` refuses the prefix without the admin bearer.
  (Alternative if you prefer an explicit column: `users.is_bot`, additive; Staging and Production share one
  database, so either must not break the older build.)
- `GET /admin/casual/cells` (admin bearer): **coarse cells only** (geohash length 5, about 5 km) holding at least one
  currently-live, opted-in casual runner, with counts of real people and ro-dots there. No user ids, no exact
  positions. This is how the service learns where to spawn without being a location store.
- Ro-dot accounts get `visible_to_non_crew = true` from the service through the existing `PUT /me/casual/settings`,
  and the `casual` feature through the existing admin features endpoint.
- Retention: `PurgeCasualData` deletes ro-dot positions older than 1 hour (decision 7).
- Client wire: `NearbyDot.isRo` (default false) and `isBot` on crew entries. Old clients ignore unknown fields. Admin
  user lists and any "active users" count exclude ro-dots.
- Kill switch: `Bots:Enabled`, plus the existing `PUT /admin/features/casual` which already stops every dot.

**Service (`ro-dots/`, .NET worker, single project + test project)**
- **Why .NET**: the CI job already builds and discovers .NET tests, `GeoMath` can be copied or shared, and the image
  is small. Node/TS (reusing `tools/simulator` movement code) is the alternative; pick one in the spike.
- **Spawner**: every 30 s, read `/admin/casual/cells`. For each cell: `target = clamp(K - realCount, 0, KMax)` (start with
  K = 3, KMax = 5), cap total fleet (start 40), spawn up to target, retire extras (let their trip finish, don't
  respawn).
- **Route source** (see Routes): picks a route that passes through the cell, never one that starts or ends at the
  demand point, so a ro-dot never converges on a particular person.
- **Fleet**: each ro-dot = persona + route + start offset + seed. Trip length 20-60 min, then it stops and a new
  trip (new persona/route, same account, or a different one) starts later.
- **Movement engine**: pure function of `(route, persona, seed, t)`, so it is unit-testable and restart-safe.
- **Poster**: each ro-dot posts every 15 s with its own jittered offset; exponential backoff on 5xx; on 403 calls
  `/casual/start` and retries once, like the apps. On shutdown it calls `/casual/stop` for each ro-dot (the stale
  cutoff would clear them in 10 min anyway).
- **Deploy**: `ro-dots/Dockerfile`, `ro-dots/.deploy.yaml`, namespace `ro-dots` on Tatooine, one replica,
  `strategy: Recreate` so a rollout never has two instances posting. Logs to Seq (`SEQ_URL`) like the server. Add
  `ro-dots/**` to the `server-dotnet.yml` path filter.

### Making them look real

| Aspect | Approach |
| --- | --- |
| Pace | Persona: easy jogger 5:30-6:45 /km, steady 4:45-5:30, walker 11-13 /km, cyclist 2:30-3:30 /km (rare). Speed = persona mean + slow random walk, slowing on climbs if elevation is known. |
| GPS noise | 3-8 m jitter that wanders (AR(1)), never independent per sample; occasional 10-15 m excursions. |
| Heading | Course over ground from smoothed successive points, not the route bearing. |
| Stops | 5-20 s at road crossings (OSM crossings/junctions) and rare 30-90 s pauses. |
| Cadence | 15 s like the apps, with ±1 s timestamp jitter and the occasional missed sample. |
| Loops | Out-and-back or loops, not point-to-point forever. Routes that end where they began. |
| Time of day | Fewer ro-dots at 1-5 am local time; more at 6-8 am and 5-7 pm. |
| Tail | A trip ends by walking a bit slower, then `stop` (dot disappears like a real stop). |

### Routes

- **Phase A (MVP)**: a folder of GPX files shipped in the image, from your own Strava activities (Strava MCP is
  connected), hand-drawn routes, and public-domain loops, covering where people actually use the app first
  (Wellington, from the simulator defaults, then wherever the App Store analytics say). Selected by "passes within 2 km
  of the cell centre", then shifted/rotated slightly so it isn't recognisably one route.
- **Phase B**: generate loops anywhere from OpenStreetMap with a self-hosted **Valhalla** on a NZ extract, in the
  same cluster. **Trails work**: its pedestrian profile routes over `path`, `footway`, `track`, `bridleway` and steps,
  exposes costing options such as `use_tracks`, `use_hills`, `max_hiking_difficulty` and `step_penalty`, and can use
  elevation data for hill-aware pace. Quality depends on how well OSM tags the trails (NZ and DOC tracks are
  generally good). Valhalla has no round-trip mode, so the service builds loops: pick a bearing and radius, route
  out and back through two or three waypoints, and reject routes that are mostly road or cross roads more than N
  times. The persona picks the costing (trail runner prefers tracks and hills, jogger prefers paths and flat, walker
  avoids steep). First do a spike: generate five loops from a trail near you and look at them on the map. Valhalla's
  map matching can also snap noisy output.
- **Never**: public tile/route APIs without a licence for this use, or any route derived from a real user's recording.

---

## Ro-crew (practice crew)

A ro-dot you can add as crew, to try the crew flow without recruiting a friend.

- **One practice runner per user**, created on demand. Account `is_bot`, `owner_user_id`, nickname "Ro-xxx".
- **Server**: `POST /me/crew/practice` (signed in, `crew` feature on, no existing practice runner) creates the bot and
  the mutual `crew_links` row directly (no code, since there's no other human to redeem it). Counts toward
  `MaxCrewSize`. Removing them is the normal `DELETE /me/crew/{userId}` and deletes the bot account.
- **Sending it out**: `POST /me/crew/practice/run` asks for a trip starting ~1-2 km from the user's last known
  position (or a fixed meeting point if they aren't tracking), 20-40 min. Stored as a pending request; the service
  picks it up from `GET /admin/practice-requests` and posts the bot as a **named, visible-to-crew** runner. The crew
  list shows a robot badge and, while it is out, the usual named dot, so the user can watch, tap to follow, and so on.
- **Scenarios** (later): "runs late and stops", "drops out of signal" (satellite cadence), "low battery" using the
  existing `batteryLevel` field, and "finishes", to let users see how each looks.
- **Privacy**: a practice runner can't read the user's location; the service only receives "start a trip near cell X".
- **Clients**: a "Add a practice runner" row in the Crew screen (iOS and Android), robot badge on crew rows, and a
  Web `/watch` page is unaffected.

---

## Safety and guard rails

- Off by default (`Bots:Enabled = false`), with a kill switch that works without a deploy.
- **Never targets a person.** Demand is cells, spawn offset and route choice are independent of the exact position,
  and a ro-dot never starts within 1 km of a viewer's last position.
- **Never presents as someone's contact.** Grey ro-dots carry no name, ever. Named ro-dots exist only for the owning
  user's practice runner.
- **Fleet cap** and per-cell cap in config; the spawner refuses to exceed them even if demand says otherwise.
- **Data**: ro-dots write the latest position only; the live table is cleared on shutdown and by the stale cutoff.
- **Metrics/logs** (Seq): fleet size, spawns/retires per cell, post latency, last successful tick, errors. An alert
  if the last tick is older than 2 min while enabled.
- **Statistics hygiene**: admin user lists and any "active users" counts must exclude `is_bot`.
- **Legal/store**: mention practice runners in the privacy policy and store description; confirm with App Store
  guideline on simulated users before release. *(Open question 3.)*

---

## Phases and tasks

### P0: Decide and spike
- [ ] Settle open questions 1-3.
- [ ] Spike: one hand-written bot posting through the existing path (a normal account with a bearer token and
      `visible_to_non_crew`) and confirm a second phone sees it as a grey dot within 5 km.
- [ ] Pick .NET vs Node for the service.

### P1: Server (branch `ro-dots-server`)
- [ ] `Bots:Enabled` config; `rodot-` username rule (and refusal in `/auth/register`).
- [ ] `GET /admin/casual/cells`; `NearbyDot.isRo`; exclude ro-dots from admin counts; 1 hour purge for ro-dot positions.
- [ ] Tests: `BotCellsTests` (admin bearer required, coarse cells only, disabled returns 404), `CasualBotVisibilityTests`
      (honours reciprocal toggle, 5 km, start fuzz, stale), register prefix refused without admin bearer.
- [ ] Docs: `docs/api-reference.html`, `docs/database-schema.html`, server README.

### P2: Service MVP (branch `ro-dots-service`)
- [ ] Project skeleton, config, structured logging to Seq.
- [ ] Movement engine (pure) and persona definitions, with unit tests (speed distribution, heading, stop behaviour,
      determinism by seed).
- [ ] GPX route library and selector; loop shift/rotation.
- [ ] Spawner against `/admin/casual/cells`; fleet cap; retire logic.
- [ ] Poster, backoff, shutdown clear.
- [ ] Dockerfile, `.deploy.yaml`, namespace/secret/NetworkPolicy notes in the README, CI path filter.
- [ ] Run against Staging with one real phone: grey ro-dots appear and go away.

### P3: Ro-crew (branch `ro-dots-crew`, name PRs "Crew: ...")
- [ ] Server: `POST /me/crew/practice`, `/practice/run`, `GET /admin/practice-requests`; `isBot` on crew rows.
- [ ] Service: practice trips (named, visible to crew), scenarios.
- [ ] iOS and Android: Crew row, badge, and the run/stop button; hidden when the server has no practice endpoint.

### P4: Routes at scale
- [ ] Valhalla on a NZ extract, loop generator, route quality filters, tile cache.

### P5: Tuning
- [ ] Time-of-day curve, density thresholds, persona mix, metrics dashboard, alert on stalled ticks.

---

## Future: games ("hunt the dot")

You want DW to host games where a ro-dot plays a part. That shapes the service now, even though no game is built:

- **Build the service as agents with behaviours**, not just "follow a route". A behaviour is a small strategy with
  the same shape (state in, next position out): `FollowRoute` (ambient), `Evade` (the hunted dot flees the players),
  `Chase`, `Patrol`, `Wander between checkpoints`. Ambient and practice runners are just two behaviours.
- **Game ro-dots live in a named session** that the players joined, as ordinary members through the existing session
  membership. That is a different kind from ambient ro-dots (decision 9), with a different privacy basis: players
  consented to the game.
- **Games need to read player positions** (a hunted dot that evades has to know where the hunters are). That breaks
  the ambient rule "never reacts to a person". Keep them separate: ambient ro-dots never read anyone; a game ro-dot
  may read only the positions of its own session's members, through the session endpoints, for as long as the game
  runs.
- **Real paths, not magic**: the hunted dot moves on real paths (Valhalla again), so the game is physically fair.
- **Open game questions** (own plan later): who ends the game (catch radius, timer), how a "catch" is detected
  (server-side distance, so players can't cheat), whether players see the hunted dot, how scores are stored.

---

## Open questions

1. **Labelled or unlabelled?** Recommend labelled (decisions 1 and 10): "XX" / "X-bot-123" and a ro-dot colour.
   Does the colour apply to ro-dots in the nearby grey layer too, or only to practice crew? What should the "N nearby"
   chip count?
2. **Pod in the existing cluster (recommended) or a separate cluster?** Your call; both work.
3. **Store/policy wording**, and whether ro-dots should be visible to everyone in casual or only to accounts that
   opt in to "Show practice runners" in Who can see me.
4. **Which areas first?** Wellington only (cheap GPX library), or anywhere with real users (needs P4). Do you want the
   Valhalla spike (five trail loops from a spot near you) before any server work?
5. **Does a practice runner watch the user back?** Today crew are only read by apps, so "the bot is watching you" has
   nothing to hook into until crew alerts exist.
6. **Sizing**: initial fleet cap and K (3 per cell, 40 total proposed).
