# Casual tracking — Plan

*DotWatcher — plan, October 2026. Status: drafted, not started.*

Goal: someone opens DotWatcher and taps **Casual** to start tracking, with no session to create or join. Their crew see
them (named, in colour). If they are tracking too, they see their crew. Anyone else out tracking nearby shows as an
anonymous grey dot. Related: [`crew-management.md`](crew-management.md), [`crew-feature-flag.md`](crew-feature-flag.md).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally.

---

## Decisions taken

- **Option A: one permanent session called "casual".** Tapping Casual joins it seamlessly; it never ends. The session
  machinery (location POST, in-memory cache, Postgres rows) is reused. Visibility rules are layered on top.
- **Crew is mutual** (already built). **Runners and watchers** can both use casual: crew can watch you without tracking.
- **Non-crew are anonymous.** No name, no initials, no id, no trail. Just a position and heading.
- **Strangers only within 5 km of the viewer.** Keeps payload and traffic small and limits exposure. Crew are always
  returned regardless of distance (the crew list is capped at `MaxCrewSize` = 50).
- **A "show me to non-crew" toggle**, per account.
- **Someone in a named session also appears in casual** (subject to the same toggle).
- **Gated by a feature flag** (`casual`), same mechanism as crew.
- **Toggle defaults to off.** The choice is remembered: the server stores it (it has to enforce it), and each client
  also keeps a local copy so the setting survives sign-out/reinstall prompts and the UI never flashes the wrong state.
  Once someone says yes, it stays yes.
- **Reciprocal.** If you're hidden from non-crew, you don't see non-crew either.
- **Named-session runners appear in casual automatically** (when the toggle is on), with a **lock indicator** on their
  dot meaning "in a private session". Only a boolean is sent; never the session name or id.
- **Start/end fuzz is in v1:** the first and last ~300 m of a run are hidden from non-crew. Crew see everything.
- **Retention is 7 days** (replay window and purge). Stale cutoff stays 10 min.

## Implementation status

Branch `casual-tracking` (off `staging`), **server only**, uncommitted. Written but **not built or run locally**
(per `AGENTS.md`); CI is the first compile. Files: `server/Stores/SessionStore.Casual.cs` (new partial of
`SessionStore`), `server/CasualVisibility.cs`, `server/Models/CasualModels.cs`, `server/Controllers/CasualController.cs`,
`server/CasualMaintenanceService.cs`, edits to `SessionStore.cs` (schema, delete guards), `LocationsController.cs`,
`FeatureFlags.cs`, `Program.cs`; tests in `CasualApiTests.cs` and `CasualVisibilityTests.cs`.

Deviations from the design above, found while reading the code:
- **Invite code:** the invite-code endpoints are unauthenticated and return positions/recordings, so the casual
  session's code is 32 hex characters. Invite codes are 4-8 characters, so no lookup can ever match it, on old or new
  code. (Stronger than "hidden".)
- **Settings** are a separate `GET/PUT /me/casual/settings`, not part of `/me/profile`: a `PUT /me/profile` with no
  nickname clears the nickname, so an older client's profile save could have toggled or wiped things.
- **Live reads** come from one Postgres query reused for 5 s (`Casual:SnapshotSeconds`), not the in-memory cache, because
  the two servers share a database but not memory.
- **Posting needs an open run.** After `POST /casual/stop` (or a run closed as abandoned) a post returns 403. **Client
  requirement:** on a 403 from a casual `POST /location`, call `POST /casual/start` and retry once. A queued/offline
  position that arrives after stop must not bring the runner back, which is why posting never starts a run itself.
  Only the copy from a named session starts one.
- **Replay** (casual_runs is in place; the endpoints are not built).

Changes after code review (PR #211):
- **Positions belong to a run** (`location_updates.casual_run_id`), so a restart never shows the previous run's last
  position, and the 300 m start fuzz always has its run's start point.
- **"Out" is judged on the server's clock** (`location_updates.received_at`), not the phone's timestamp. A wrong phone
  clock can no longer keep a dot alive for hours or hide a runner. The client's own posting interval
  (`nextExpectedAt - timestamp`) still extends the window for slow (satellite) cadences.
- **Late (queued) positions** (older than 10 min on arrival) are stored for replay but have no `received_at`, so they
  never become the runner's shown position or set a run's start point. A named-session copy ignores them entirely.
- **Inserts verify the run is still open in Postgres**, so a run closed by the other server or the purge can't keep
  accepting posts from this process's cache. Reusing an open run after a cache miss applies the same abandoned-run check
  as `/casual/start`.
- **`GET /casual/live` takes no position.** Nearby dots are only returned to someone who is out themselves, centred on
  where the server last placed them (otherwise any opted-in account could scan the map for strangers). Watchers who
  aren't tracking get crew only. This changes the earlier "watcher mode with the viewer's location" idea.
- **Pressing stop sticks**: for 12 hours a copy from a named session won't start a run again (`stopped_by_user`);
  `/casual/start` clears it.
- **The casual owner account can't be deleted**, casual is excluded from admin session lists, merge and recording
  deletion, and the hourly job recreates the session if a pre-guard Production build deletes it.
- **Fewer queries per post** (account row read once; crew and blocks share a connection; one shared parameter helper
  for the position INSERT).

Server tasks: S1 storage, S2 bootstrap, S3 flag, S4 start/stop, S5 write path + fan-out, S6 read + visibility, S7
guards (delete refused; session endpoints refuse by having no memberships), S8 retention, S9 tests are written.
Not done: client work, replay endpoints, a config/ops check of `Features:EnabledForAll` for `casual` on Staging.

---

## Android status

Branch `android-casual` (off `staging`, which has the server). Built and run on the emulator against a local
server; unit tests pass (`CasualWireTest`). Not yet on a real phone. Decisions from the design review
(`Casual Go Button` mock-up) that this implements:

- **Hold to go** (0.9 s ring) on the start screen, above the existing Join and Create, which are unchanged.
- **Before you go** every time Go is held: who sees you, **Share for 2h / 4h / 8h / 24h (default 4h)**, Go, and
  Choose who can see me. Sharing always auto-stops, as the session consent screen already does.
- **Casual map** like the session map: wordmark, auto-stop countdown (tap to extend +30 min / +1 hr / +2 hr or stop
  now), fit-all, an eye for Who can see me, a "N crew out · M nearby" chip, grey dots for nearby people with a padlock
  for those in a session, and **Hold to stop**.
- **Who can see me**: crew always; "Show me to people nearby" off by default, remembered on the phone (the server is
  the source of truth).
- **Hidden unless the server has it**: the start screen probes `GET /me/casual/settings`; a 404 (older server, or an
  account without the `casual` feature) shows nothing, as crew does. Verified: an account without the feature sees
  the unchanged start screen.
- **Service**: tracking runs against the fixed casual session id (`CASUAL_SESSION_ID`). A 403 on a casual post calls
  `/casual/start` and retries once (the run was closed); expiry calls `/casual/stop`; sign-out and starting a named
  session end a casual run properly. After the app is killed, a share that hasn't run out resumes quietly.

Not built: the **share a watch link** button (needs the server's watch token + web page), crew count refresh while on
the map beyond the 10 s poll, iOS.

---

## Watch link status

Branch `casual-watch-link` (off `staging`): the **server and web page** for sharing a run with someone who has no
account or app. The apps' buttons (Android, iOS) are not built yet.

- `POST /casual/watch-link` (signed in, out on a run; 409 otherwise) makes a secret link for the current run and
  returns `{ token, path }`; `DELETE /casual/watch-link` ends every link for the run now. Each call makes a **new**
  link (only a SHA-256 of the 128-bit secret is stored, so an old one can't be shown again); all of a run's links die
  when the run ends.
- `GET /watch/{token}` (no sign-in) returns `{ label, ended, live, position }` for that one run and nothing else:
  the label is the account's initials, never a username or id. Unknown, malformed or revoked tokens are a bare 404;
  repeated wrong guesses get 429 without ever refusing a valid link. The response is `Cache-Control: no-store`.
- The exact position is shown (no hidden 300 m near the start): the runner chose to send this link to a person.
- The secret is kept out of the request logs (`/watch/{token}` is what gets logged, and watch-link responses aren't
  captured) and out of referrers and search indexes (the page sets `noindex` and `no-referrer`).
- Web: `/watch/{token}` shows one dot with a heading chevron and "Live · updated 12 s ago", falling back to
  "Not updating · last seen 13 min ago", "Waiting for the first position…", or "This run has ended"; an unknown link gets
  a plain "This link isn't working" card. A browser with no WebGL gets a text view with an "Open in Maps" link.

---

## Precedent in the code: the demo session

`SessionStore.EnsureDemoSession` already creates a permanent session with a placeholder system user, and
`GetSessionRunners` / `GetParticipants` / `GetLatestPositions` already filter what each *caller* sees
(`IsDemoSession`). Casual follows the same pattern: a permanent session, a system owner, and per-caller scoping
inside the store. It differs in two ways: positions **are** persisted, and the filter depends on the caller's crew
and location rather than a fixed rule.

---

## Design

### Visibility is computed per viewer, in one function

One function, e.g. `CasualVisibility.ForViewer(viewerId, viewerLat, viewerLon, livePositions)`, does all of it, so
the rule lives in one place (and can later move to a "presence" model without touching callers):

| Other person | Shown to viewer? | As |
|---|---|---|
| The viewer themselves | yes | named (own name) |
| Crew (mutual link), not blocked | yes, any distance | named: crew nickname, else initials (as in crew) |
| Non-crew, within 5 km of viewer, **and** both of you have the toggle on, and not in the hidden start/end ~300 m | yes | anonymous grey dot (lat, lon, heading, `inSession` lock flag) |
| Anyone blocked either way (`AreBlocked`) | never | n/a |
| Everyone else | no | n/a |

The toggle is **reciprocal** (decided): if your toggle is off you get crew only, and strangers don't see you. Crew
always see you regardless of the toggle.

"Live" means: has an open casual run **and** a position within the stale cutoff (default 10 min, honouring
`NextExpectedAt` + grace so a satellite user on a slow cadence doesn't vanish; hard cap 30 min).

### Authorization: no memberships

Casual does **not** create `session_members` rows. Reads and writes are special-cased in the store
(`IsCasualSession`) and gated on the feature flag. Reasons:

- **Shared Staging/Production database** (see `shared-database-cutover.md`). Old Production code doesn't know about
  casual. A membership row would show a "Casual" session in `GET /me/sessions` on old clients. With no rows, old code
  never sees it.
- Session-scoped endpoints (`/locations/{id}`, `/sessions/{id}/recording`, route, merge, delete, leave) all check
  membership, so they refuse the casual session **automatically**. This matters: those endpoints would otherwise dump
  every user's history.
- The casual session still needs an `invite_code` (NOT NULL UNIQUE). Generate a random 12+ char code, never display it,
  and make `JoinSessionByInvite` / `GetSessionInfoByInviteCode` refuse it explicitly (old Production code can't know to
  refuse it, so the code must be unguessable, not just hidden).

### Seamless start/stop

- `POST /casual/start` (idempotent): checks the flag, opens a **run** (see below) if none is open, returns the casual
  `sessionId`. The client then posts to the existing `POST /location` with that id.
- `POST /casual/stop`: closes the open run; the user disappears from everyone's live list immediately.
- If the app dies, the stale cutoff removes the dot. The run is closed by the server after the cutoff (lazily, on next
  start or by the retention job).

### Read path

- **Runners:** `POST /location` for the casual session returns the existing `{ participants, positions }` for
  self + crew, plus an **additive** `nearby: [{ latitude, longitude, heading, inSession }]` array. Old clients never join casual,
  so the shape change is invisible to them.
- **Watchers (not tracking):** `GET /casual/live?lat=&lon=` returns the same payload, centred on the supplied point.
  If the watcher has no useful location, crew only.
- Cross-pod: each pod's `_sessions` cache is private, so (as in `GetLatestPositions`) fall back to Postgres:
  `DISTINCT ON (runner_user_id)` for rows in the last stale-window. Uses the existing
  `idx_session_timestamp (session_id, timestamp)`. The 5 km filter is a lat/lon bounding box, then a haversine
  check in code; no PostGIS needed at this scale.

### Persistence, and the problems of a session that never ends

| Existing code | Problem for casual | Handling |
|---|---|---|
| `AddPosition` appends to `_sessions[sid][uid]` list forever | Unbounded memory | Casual keeps only the latest position (or a short ring buffer) per user |
| `location_updates` rows | Grow forever | Retention: purge casual rows older than N days (default 30; hosted service or opportunistic delete) |
| `GetRunStartTimestamp` (`MIN/MAX` over the whole session, 24 h window) | Meaningless and slow for a perpetual session | Casual never uses it; replay goes through runs (below) |
| `GetRecordedSessions`, `GetAllSessions`, admin | Would list casual | Exclude casual from admin lists and merge/delete/archive; refuse them |
| `GetSessionsForUser` / "recent left" | n/a, no memberships | no change |
| Satellite alerts, update interval plan | Assume a session owner / session-wide settings | Casual uses client defaults; satellite alerts for casual are out of scope for v1 |

### Replay (deferred, design settled enough to avoid painting into a corner)

Fixed time-boxing doesn't fit casual: runs overlap, start at arbitrary times, and different people run different
lengths. Anchor replay to **one runner's start and stop**:

- New `casual_runs (id, user_id, started_at, ended_at NULL)` table, FK `ON DELETE CASCADE`. `POST /casual/start` /
  `stop` write it. This gives exact boundaries; a gap in updates is not needed to infer them.
- A named-session user who is fanned out to casual (see below) also gets a casual run for the duration of that session
  run, or simply isn't replayable via casual (their replay is the named session's). **Recommended for v1:** not
  replayable via casual.
- `GET /me/casual/runs` lists the caller's runs. `GET /me/casual/runs/{id}/recording` returns NDJSON in the existing
  recording format for: the caller's rows in `[started_at, ended_at]`, plus rows from **crew who were live during that
  window** (labelled by nickname/initials). **Stranger grey dots are never in a replay.**
- Reuses the existing replay player; the recording query is a variant of `GetRecordingWindowAsNdjson` keyed by
  user-set + time window rather than session id.
- Whether crew can replay *each other's* runs (not just their own) is a later decision.

### Named session + casual at once

Do the fan-out **on the server**: in `AddLocation`, after the named-session write, if the user has the toggle on
(and the flag), also `AddPosition` into the casual session, marked `inSession = true`. Clients draw a **lock** on those
dots (grey for strangers, coloured for crew). Only the boolean is exposed. Someone in a named session with the toggle
off is not fanned out, so they stay invisible to non-crew. One POST from the phone, no second request, no extra
battery. The named session's behaviour is unchanged.

### Privacy defaults

- Anonymous dots: no name, no id, no trail, only the latest position.
- **Hide the first and last ~300 m** of a run from non-crew (home exposure). Needs the run's first point (from
  `casual_runs`/first row) and, for the end, a short delay: a dot is withheld from non-crew until the runner is >300 m
  from the start, and the end is covered by dropping the dot as soon as `stop` arrives or the stale cutoff hits (the last
  stretch can't be known in advance, so v1 also withholds any dot within 300 m of that run's start point, which covers the
  usual loop-back-home case). Crew see everything.
- First-use explanation before the toggle is ever on, plus the toggle in Settings.
- The server never sends a stable identifier for non-crew, so a client can't link dots across polls to build a track.
  (Corollary: grey dots may jump identity between polls; clients must not animate them as the same entity across
  responses.)

---

## Server tasks

### S1. Storage (all additive, FKs cascade, shared DB)
- [ ] `users.casual_visible_to_non_crew BOOLEAN NOT NULL DEFAULT FALSE` (additive; old code names columns explicitly).
      Server is the source of truth; clients mirror it locally.
- [ ] `casual_runs` also stores `start_lat/start_lon` (for the 300 m fuzz).
- [ ] `casual_runs` table (above), `ON DELETE CASCADE`, index on `(user_id, started_at)` and a partial index on open runs.
- [ ] Surface the toggle via `GET/PUT /me/profile` (alongside nickname).

### S2. Casual session bootstrap
- [ ] `EnsureCasualSession()` modelled on `EnsureDemoSession`: system user (`dw-casual`), random unguessable invite
      code, idempotent, cached `CasualSessionId`. Called from `Program.cs` next to the demo session.
- [ ] `IsCasualSession(sessionId)`.

### S3. Feature flag
- [ ] `Features.Casual = "casual"`; all `/casual/*` endpoints and the casual branch of `POST /location` refuse callers
      without it. Casual also requires nothing from crew to *start*, but crew-visible output needs the `crew` flag too.

### S4. Start / stop
- [ ] `POST /casual/start`, `POST /casual/stop` (idempotent, rate limited like other write endpoints).

### S5. Write path
- [ ] `LocationsController.AddLocation`: casual branch. Skips `GetMembership(...)!` (would be null); runner name comes
      from the user row. `CanWriteLocation` allows casual when the flag is on and the user has an open run.
- [ ] `AddPosition`: latest-only cache for casual; still persists the row.
- [ ] Fan-out from named sessions (above).

### S6. Read path + visibility
- [ ] `CasualVisibility` function + unit tests (crew, non-crew, blocked, toggle, reciprocity, 5 km edge, stale cutoff).
- [ ] `POST /location` casual response with `nearby`; `GET /casual/live`.
- [ ] Postgres fallback query for cross-pod freshness.

### S7. Guards
- [ ] Exclude casual from `GetAllSessions`, `GetRecordedSessions`, merge, delete, archive, leave; refuse in invite join.
- [ ] Confirm every session-scoped endpoint 403s casual (no memberships), with a test.

### S8. Retention
- [ ] Purge casual `location_updates` and closed `casual_runs` older than 7 days (config `Casual:RetentionDays`, default 7).
- [ ] Close orphaned open runs past the stale cutoff.

### S9. Tests
- [ ] New test project cases for visibility, start/stop idempotency, authorization refusals, shared-DB-safety (casual
      rows don't affect `DELETE FROM users`).

## Client tasks (after the server is on Staging)

- [ ] **Android / iOS main screen:** a **Casual** card alongside "Start a new one" (the Android start-screen branch
      already has these cards). Tap = `POST /casual/start`, then the normal tracking screen. Hidden when the `casual`
      feature is absent or `/casual/*` returns 404 (same silent-hide rule as crew).
- [ ] Render `nearby` as grey dots (no label, no tap-through, no animation between polls); draw a lock on dots with
      `inSession` (crew dots too).
- [ ] Toggle default off, mirrored locally so a "yes" persists.
- [ ] Settings: "Show me to people who aren't in my crew" toggle + first-use explanation.
- [ ] Watcher mode: `GET /casual/live` with the viewer's location.
- [ ] Stop button calls `POST /casual/stop`.
- [ ] Web client: out of scope for v1.

## Replay tasks (phase 2)

- [ ] `GET /me/casual/runs`, `GET /me/casual/runs/{id}/recording` (see design).
- [ ] Replay entry point in the clients' run history.

---

## Rollout hazards

| # | Severity | Hazard | Mitigation |
|---|---|---|---|
| C1 | 🔴 High | **Staging and Production share one DB.** The casual session row exists for old Production code that doesn't know it: it appears in admin session lists, and its invite code could be used to join. | Random unguessable invite code, never displayed; no membership rows (so it never appears in `/me/sessions`). New tables/columns additive with `ON DELETE CASCADE`, as for crew (H1 in the crew plan). |
| C2 | 🟠 Medium | **Per-pod in-memory caches.** A runner posting to Staging is invisible in Production's memory. | Postgres fallback in the read path (already the pattern in `GetLatestPositions`). |
| C3 | 🟠 Medium | **Privacy.** Strangers' live positions, even anonymous, near someone's home. | Toggle (default open question), 5 km radius, no identifiers, optional start/end fuzz, feature flag for staged rollout. |
| C4 | 🟡 Low | **Unbounded growth** (memory + rows). | Latest-only cache and 7-day retention job (S5, S8). |
| C5 | ℹ️ Guard rail | Do not bump `MinimumApiVersion` / `API_VERSION`; everything here is additive. | Explicit check in the PR. |

---

## Open decisions

Decided in review: toggle default off (and remembered), reciprocal, named-session runners appear with a lock, start/end
fuzz in v1 (simple version: withhold non-crew dots within 300 m of the run's start), 7-day retention, 10-min stale
cutoff, lock = "in a session, not a casual run" (so not joinable by the viewer), own-run replay. Still open:

1. **Crew replaying each other's runs.** Recommended: **not in v1.** You can replay your own run, which includes crew
   who were live during it (labelled by nickname/initials). Replaying a crew member's run as the subject is a new
   permission (their whole track, not just where you overlapped), worth deciding once crew has real usage.
