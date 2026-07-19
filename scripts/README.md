# scripts

## download-session.sh

Downloads a session's data from a server (staging by default) using just its
invite code, and saves it locally. No login/access token is needed — every
`/session-invites/{code}/...` route is unauthenticated by design, since
that's what lets a viewer open a shared link without an account.

### Usage

```bash
scripts/download-session.sh CODE
scripts/download-session.sh CODE --out data/sessions
SERVER_URL=http://localhost:8080 scripts/download-session.sh CODE
```

Saves into `<out>/<CODE>/` (default `data/sessions/<CODE>/`):

- `info.json` — `SessionInfo` (sessionName, ownerDisplayName)
- `meta.json` — `RecordingMeta` (runStartTimestamp, latestTimestamp)
- `recording.ndjson` — the full latest-run recording, one JSON object per line
- `route.gpx` — the saved route, if the session has one

Each file is fetched independently and skipped (with a warning) rather than
failing the whole script if that particular piece 404s — e.g. a session
with no saved route just won't get a `route.gpx`.

Defaults to the staging server (`https://dot-watcher-staging.skelstar.io/api`);
override with the `SERVER_URL` env var to point at local dev or production.

## import-session.sh

Re-imports data previously saved by `download-session.sh` into a local
server, so it shows up in *that* server's own admin panel and is replayable
through the normal app UI. Downloading only saves files locally — it never
touches any server's database, so a session downloaded from staging won't
appear in `localhost`'s admin panel until it's recreated there.

Creates a brand-new local session (a new sessionId/inviteCode — the
original staging IDs aren't reused) using the downloaded session's name and
owner display name, then uploads the downloaded recording and route via the
existing admin-token-protected bulk endpoints.

### Usage

```bash
scripts/import-session.sh CODE
scripts/import-session.sh CODE --dir data/sessions
SERVER_URL=http://localhost:8080 ADMIN_TOKEN=dev-token scripts/import-session.sh CODE
```

Defaults to `http://localhost:8080` and the local dev admin token
(`dev-token`, from `server/appsettings.Development.json`).

### Notes

- Requires `data/sessions/CODE/` to already exist (i.e. run
  `download-session.sh CODE` first).
- Session names must be unique per server — if you've already imported a
  given code once, re-running will fail at the "create session" step with
  a 409 rather than silently duplicating it. Use the admin panel (or the
  admin API) to delete the old session first if you want a clean re-import.
- The recording/route uploads must be sent with an explicit non-form
  `Content-Type` (`application/x-ndjson` / `application/gpx+xml`) — a plain
  `curl --data-binary` defaults to `application/x-www-form-urlencoded`,
  which ASP.NET then tries to parse as form data and rejects.
- Caveat: the recording upload endpoint doesn't associate rows to a user,
  so the admin panel's per-member position counts will read as zero for
  imported data even though the session, recording, and route all replay
  correctly on the map.
- Leaves behind a throwaway owner account (`import_<code>_<timestamp>`) so
  the session has a valid owner; delete it via `DELETE /me` with that
  user's token if you don't want it kept around.

## simulate-gps-track.ps1

Simulates a live Dot Watcher session for testing the GPS signal-loss warning,
without needing a real device or a real run.

It registers two runners, creates a session, and posts a live-updating track
for each over a set duration. Every posted position is written straight to
the server's database (the same `location_updates` table a real run uses),
so the session is fully replayable afterwards through the app's normal
scrubber/timeline UI — this isn't a live-only simulation, it leaves behind
a real recording.

The same two user accounts and the same session name are reused every run —
accounts are only registered the first time (subsequent runs just log in).
By default the session's location history is cleared at the start of each
run so you always start from a clean track; pass `-KeepHistory` to skip
that and append the run onto the existing recording instead, e.g. if you
want to preserve a specific run for replay before generating more test
data. Each runner independently cycles between three states so you can see
the app react to different kinds of GPS trouble at different times:

- **good** — a real heading is reported every tick (normal running).
- **erratic** — heading is `null` (the device has a position but can't
  determine course). This is what the app's "Possible signal loss" warning
  and the red marker badge key off.
- **nodata** — no update is posted at all for that tick (out-of-coverage /
  dropout). The dot just stops moving; no warning is shown, since there's
  nothing to distinguish this from the runner pausing.

Every run guarantees at least 5 ticks of good GPS at the very start (see
`$LeadInTicks`), so there's always a clean, visible lead-in before the
first warning appears — bad windows never start right away.

### Requirements

- The local server running (default `http://localhost:8080`).
- PowerShell 7+ (`pwsh`), cross-platform. On macOS: `brew install
  powershell` (a regular formula, not a cask).

### Usage

```powershell
./scripts/simulate-gps-track.ps1
```

This registers (or logs in as) two runners named `SK` and `AB`, creates
(or reuses) the `SIMTEST` session, and runs a 10-minute track, posting
updates every 8 seconds. It prints the session URL to open in the browser
(e.g. `http://localhost:5174/SIMTEST`) and a summary of each runner's
scheduled bad-GPS windows before it starts.

### Options

| Param            | Default                 | Description                                  |
|-------------------|--------------------------|-----------------------------------------------|
| `-Name1`          | `SK`                    | First runner's name/initials                  |
| `-Name2`          | `AB`                    | Second runner's name/initials                 |
| `-Server`         | `http://localhost:8080` | Server base URL                               |
| `-Minutes`        | `10`                    | Track duration in minutes                     |
| `-TickSeconds`    | `8`                     | Seconds between location updates              |
| `-Seed`           | none                    | Random seed, for a reproducible bad-GPS schedule |
| `-SessionName`    | `SIMTEST`               | Fixed session name to create/reuse             |
| `-AdminToken`     | `dev-token`             | Admin bearer token used to clear the session's recording (local dev default; see `server/appsettings.Development.json`) |
| `-KeepHistory`    | off                     | Skip clearing the session's recording; append this run instead of starting fresh |

### Examples

Quick smoke test (under a minute, updates every second):

```powershell
./scripts/simulate-gps-track.ps1 -Minutes 0.5 -TickSeconds 1
```

Custom runner names, reproducible schedule:

```powershell
./scripts/simulate-gps-track.ps1 -Name1 JD -Name2 KL -Seed 42
```

Point at a non-default server:

```powershell
./scripts/simulate-gps-track.ps1 -Server http://localhost:8080
```

Preserve this run's recording instead of clearing it next time:

```powershell
./scripts/simulate-gps-track.ps1 -KeepHistory
```

### Replaying a run

Every position posted by the script is a normal, authenticated
`POST /location` call — the server persists it to the database exactly like
a real device would. That means once a run finishes (or even while it's
still going), you can open the session in the app
(`http://localhost:5174/SIMTEST`) and use the scrubber/timeline to replay
it, the same as any real recorded session.

The catch: by default, the *next* time you run the script it clears that
session's recording first (see `-KeepHistory` above), so only the most
recent run is replayable unless you either pass `-KeepHistory` or use a
different `-SessionName` per run you want to keep around.

### Notes

- The two test user accounts (`sim-<name1>`, `sim-<name2>`, lowercased) and
  the `SIMTEST` session (or whatever session name you pass) are reused
  across runs. First run registers the accounts and creates the session;
  later runs log in and reuse the same session, so its invite code stays
  stable and you can keep the same browser tab open across runs.
- Clearing the session's recording requires the admin bearer token
  (defaults to the local dev value `dev-token` baked into
  `server/appsettings.Development.json`). Against a non-local server, pass
  the real token explicitly.
- The two runners' bad-GPS windows are randomized independently each run
  (unless a seed is set), so they'll usually go bad at different times —
  useful for checking that the warning toast and marker badges track
  multiple affected runners correctly.
- Runner 1 is the session owner; runner 2 joins via the invite code, same
  as a real viewer/runner joining flow.
- If Homebrew reports a `dotnet` symlink conflict while installing
  `powershell`, leave it — don't `brew link --overwrite dotnet`, since that
  can point `dotnet` away from the SDK the server itself uses. `pwsh` works
  fine regardless of that conflict.
