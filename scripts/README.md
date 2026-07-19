# scripts

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
