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
2. **Separate service, own namespace on Tatooine, not its own cluster.** Own Deployment, own token, a NetworkPolicy
   allowing only egress to the server. A second cluster adds ops for no isolation we need. *(Open question 2.)*
3. **Write-only service credential.** Ro-dots do not get user tokens that can read. The service authenticates with
   one **bot service token** (config, like `BearerToken`) and calls bot-only endpoints, so a leaked token can't read
   anyone's map or crew.
4. **Batch endpoint.** One `POST /bots/positions` per tick carrying every ro-dot's position, instead of N user-token
   posts. The server validates each (`LocationUpdateValidation`) and writes it through the casual write path.
5. **Ro-dots fill gaps, they don't replace people.** Spawn only where real nearby density is below a threshold, and
   thin out as real runners appear.
6. **Server-side privacy rules still apply.** A ro-dot is subject to the same visibility rules as a person, so a
   viewer who hasn't turned on "show me to people nearby" sees no ro-dots either (reciprocal), exactly like real
   strangers.
7. **No history for ro-dots.** Their latest position only. At 15 s cadence, 50 ro-dots would add ~290k
   `location_updates` rows a day (~2M inside the 7-day retention), and no one will replay them.
8. **Routes never come from real users' recordings.** Real people's tracks are not ours to replay as someone else.
   Sources: your own activities, OSM-derived routes, and hand-drawn GPX.
9. **Ro-dots only exist in casual.** Never in a named session, never in "Happening near you", never in a race.

---

## Architecture

```
                     ┌────────────────────────────┐
  phones / web ◄─────┤  dot-watcher-server        │◄──── Postgres (shared)
                     │  /casual/live, /location   │
                     │  /me/crew/*                │
                     │  /bots/*   (service token) │
                     └─────▲──────────┬───────────┘
        positions (batch)  │          │ demand cells, practice requests
                     ┌─────┴──────────▼───────────┐
                     │  ro-dots service (k3s)     │  one replica, in-memory fleet
                     │  Spawner · Fleet · Routes  │  state is disposable
                     │  Movement engine · Poster  │
                     └────────────────────────────┘
```

**Server additions (small, all in `server/`)**
- `users.is_bot BOOLEAN NOT NULL DEFAULT FALSE` (additive, like the other columns; Staging and Production share the
  database, so nothing here may break the older build). Bot accounts get a random unusable password, as `dw-demo` does.
- `Bots:Enabled` (default `false`) and `Bots:ServiceToken` in config. When disabled, `/bots/*` is 404 and nothing is
  drawn.
- `POST /bots/positions` (service token): `[{ botId, lat, lon, heading, timestamp, nextExpectedAt }]`. Rejects
  unknown ids and non-bot users. Writes the latest position only (decision 7), into a small `bot_live` table that
  `LoadCasualSnapshot` unions in as `in_session = false`, `visible_to_non_crew = true`, start fuzz on.
- `POST /bots/ensure` (service token): creates or fetches N bot accounts by stable key (so the pool survives restarts
  and ids stay constant), returns ids. Initials random, so crew practice looks natural.
- `GET /bots/demand` (service token): **coarse cells only** (geohash length 5, ≈ 5 km) holding at least one
  currently-live, opted-in casual runner, with counts of real people and ro-dots already there. No user ids, no exact
  positions. This is how the service learns where to spawn without being a location store.
- `POST /bots/clear` (service token): removes every ro-dot's live row (called on shutdown). Stale cutoff would do it in
  10 min anyway.
- Client wire: `NearbyDot.isRo` (default false) and `isBot` on crew entries. Old clients ignore unknown fields.
- Admin: `GET /admin/bots` (count, cells, last post) for a quick look; kill switch is `Bots:Enabled` plus a
  `PUT /admin/bots/enabled` that persists in the existing `feature_switches` store so it works without a deploy.

**Service (`ro-dots/`, .NET worker, single project + test project)**
- **Why .NET**: the CI job already builds and discovers .NET tests, `GeoMath` can be copied or shared, and the image
  is small. Node/TS (reusing `tools/simulator` movement code) is the alternative; pick one in the spike.
- **Spawner**: every 30 s, read `/bots/demand`. For each cell: `target = clamp(K - realCount, 0, KMax)` (start with
  K = 3, KMax = 5), cap total fleet (start 40), spawn up to target, retire extras (let their trip finish, don't
  respawn).
- **Route source** (see Routes): picks a route that passes through the cell, never one that starts or ends at the
  demand point, so a ro-dot never converges on a particular person.
- **Fleet**: each ro-dot = persona + route + start offset + seed. Trip length 20-60 min, then it stops and a new
  trip (new persona/route, same account, or a different one) starts later.
- **Movement engine**: pure function of `(route, persona, seed, t)`, so it is unit-testable and restart-safe.
- **Poster**: one batch call every 15 s with jittered per-bot offsets; exponential backoff on 5xx; on 404 (feature
  off) sleeps and polls. Shutdown calls `/bots/clear`.
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
- **Phase B**: generate loops anywhere from OpenStreetMap: a self-hosted **Valhalla** (pedestrian costing) on a NZ
  extract in the same cluster, with a loop generator (pick a bearing and radius, route out and back via two waypoints,
  reject routes that cross a road more than N times or are mostly cycleway-less highway). Valhalla also gives map
  matching if we ever want to snap noisy output.
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
  picks it up from `GET /bots/practice-requests` and posts the bot as a **named, visible-to-crew** runner. The crew
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
- [ ] `users.is_bot`, `bot_live` table, schema bootstrap, delete/merge guards.
- [ ] `Bots:*` config, service-token auth, `/bots/ensure|positions|demand|clear`.
- [ ] `LoadCasualSnapshot` unions `bot_live`; `NearbyDot.isRo`; exclude bots from admin counts.
- [ ] Tests: `BotApiTests`, `CasualBotVisibilityTests` (honours reciprocal toggle, 5 km, start fuzz, stale), service
      token required, disabled → 404, bots can't read.
- [ ] Docs: `docs/api-reference.html`, `docs/database-schema.html`, server README.

### P2: Service MVP (branch `ro-dots-service`)
- [ ] Project skeleton, config, structured logging to Seq.
- [ ] Movement engine (pure) and persona definitions, with unit tests (speed distribution, heading, stop behaviour,
      determinism by seed).
- [ ] GPX route library and selector; loop shift/rotation.
- [ ] Spawner against `/bots/demand`; fleet cap; retire logic.
- [ ] Poster, backoff, shutdown clear.
- [ ] Dockerfile, `.deploy.yaml`, namespace/secret/NetworkPolicy notes in the README, CI path filter.
- [ ] Run against Staging with one real phone: grey ro-dots appear and go away.

### P3: Ro-crew (branch `ro-dots-crew`, name PRs "Crew: ...")
- [ ] Server: `POST /me/crew/practice`, `/practice/run`, `GET /bots/practice-requests`; `isBot` on crew rows.
- [ ] Service: practice trips (named, visible to crew), scenarios.
- [ ] iOS and Android: Crew row, badge, and the run/stop button; hidden when the server has no practice endpoint.

### P4: Routes at scale
- [ ] Valhalla on a NZ extract, loop generator, route quality filters, tile cache.

### P5: Tuning
- [ ] Time-of-day curve, density thresholds, persona mix, metrics dashboard, alert on stalled ticks.

---

## Open questions

1. **Labelled or unlabelled?** Recommend labelled (decision 1). If unlabelled, do you accept the trust and App Store
   risk, and what should the "N nearby" chip count?
2. **Own cluster or own namespace on Tatooine?** Recommend namespace.
3. **Store/policy wording**, and whether ro-dots should be visible to everyone in casual or only to accounts that
   opt in to "Show practice runners" in Who can see me.
4. **Which areas first?** Wellington only (cheap GPX library), or anywhere with real users (needs P4).
5. **Does a practice runner watch the user back?** Today crew are only read by apps, so "the bot is watching you" has
   nothing to hook into until crew alerts exist.
6. **Sizing**: initial fleet cap and K (3 per cell, 40 total proposed).
