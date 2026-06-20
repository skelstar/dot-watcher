# Dot Watcher

Real-time GPS tracking for running groups. Runners share their position during a group run, race, or solo outing. Supporters and fellow runners can watch everyone's location on a live map in a browser.

---

## Repo structure

```
dot-watcher/
  .ai/         AI-assisted PR review template and helper prompt script
  server/      .NET Core minimal API
  tests/       .NET integration tests
  client/      React web app (Mapbox)
  ios/         Swift iOS app
  android/     Kotlin Android app (not started)
  AGENTS.md   Codex/agent repository instructions
  README.md
```

---

## AI review workflow

This repo uses a small, practical split for AI-assisted work:

- `AGENTS.md` contains standing Codex/agent instructions. The main rule is that agents should not run local build, publish, or test commands because GitHub Actions CI owns that verification.
- `.ai/pr-review-template.md` contains the reusable static PR review template. It is intentionally separate from `AGENTS.md` so the always-on agent rules stay short.
- `.ai/review-pr.sh` prints a ready-to-copy PR review prompt for the current branch.

Run the helper from the repo root:

```bash
./.ai/review-pr.sh main
```

Generated review files belong under `.ai/reviews/` and are ignored by git. This keeps local review artifacts out of commits while preserving the template and helper script in the repo.

---

## How it works

1. Each runner signs in to the iOS app, creates or selects an owner/runner session, and starts tracking
2. The app sends GPS coordinates and compass heading to the server at a configurable interval (default 60s)
3. The server stores the latest positions for all runners in that session
4. Other users sign in and join the session from an invite code or link
5. The web viewer polls the server every 10–15 seconds and renders all runners as directional markers on a Mapbox map

---

## Components

### server

A lightweight .NET Core minimal API.

**Responsibilities**

- Receive position updates from phone apps (`POST /location`)
- Store latest live positions in memory and persisted history in SQLite
- Serve current runner positions to authenticated session members (`GET /locations/{sessionCode}`)
- No geometry or bearing logic — that is handled by the client

**Auth**

- App endpoints require a signed user access token in the `Authorization` header
- Admin/debug endpoints require the server admin bearer token
- Session membership controls who can read or write location data

**Hosting**

- Runs on Tatooine (home server, k3s cluster)
- Exposed publicly via Cloudflare Tunnel
- HTTPS handled by Cloudflare

**Data**

- SQLite persistence for recorded history
- In-memory live state for latest runner positions
- Designed for up to 20 concurrent runners
- Session data can be cleared between runs

---

### client

A React web app using Mapbox GL JS.

**Behaviour**

- Full-screen Mapbox map, responsive and touch-friendly
- On first load, prompts for sign-in, then lists existing memberships or offers create/join actions
- Session code can be embedded in the URL for members (e.g. `https://dot-watcher.yourdomain.com/SESSIONCODE`)
- Polls the server for updated positions every 10–15 seconds
- Renders each runner as a named marker with a directional arrow
- Arrow orientation is driven by the `heading` field from the server (compass bearing from the phone)
- When heading is unavailable for a runner, renders a plain dot instead
- Map auto-fits bounds to show all runners

**No bearing calculation in the client** — heading comes directly from the phone's compass via the server payload.

---

### ios

A native Swift app.

**Behaviour**

- Runs as a background task with the screen locked
- Uses `CLLocationManager` with `allowsBackgroundLocationUpdates = true`
- Sends position updates (coordinates + compass heading) to the server at a configurable interval
- Interval is configurable within the app (e.g. 30s, 60s, 120s)
- Failed transmissions are queued locally and retried
- Uses the device compass for heading — does not calculate bearing from consecutive positions
- When heading is unavailable (e.g. runner is stationary), sends position without heading

**Setup screen**

- Runner signs in
- Runner creates a session or joins from an invite code
- Runner starts/stops tracking manually

**Auth**

- Sends a user access token in the `Authorization` header on protected app API calls

**Distribution**

- Distributed via TestFlight (not the App Store)

---

## Auth model

| Actor      | Method                         | Auth                                   |
| ---------- | ------------------------------ | -------------------------------------- |
| App user   | `POST /auth/register`, `POST /auth/login` | Username/password, returns user access token |
| Runner     | `POST /location`               | User access token plus owner/runner session membership |
| Viewer     | `GET /locations/{sessionCode}` | User access token plus session membership |
| Admin/debug dashboard | `GET /sessions`, `GET /log`, recording mutations | Admin bearer token in `Authorization` header |

Session codes are identifiers, not credentials. Invite codes/links are used to join a session, then the server stores membership and authorizes future reads/writes from the authenticated user identity.

```text
                 public account endpoints
        +--------------------------------------+
        | POST /auth/register, POST /auth/login |
        +-------------------+------------------+
                            |
                            v
                  +-------------------+
                  | User access token |
                  | Authorization:    |
                  | Bearer <token>    |
                  +---------+---------+
                            |
          +-----------------+-----------------+
          |                                   |
          v                                   v
+-------------------+              +----------------------+
| Create session    |              | Join from invite     |
| POST /sessions    |              | POST /session-       |
|                   |              | invites/{code}/join  |
+---------+---------+              +----------+-----------+
          |                                   |
          v                                   v
 +----------------+                 +----------------+
 | app_sessions   |                 | session_members|
 | session_code   |<--------------->| user_id        |
 | invite_code    |                 | role           |
 | owner_user_id  |                 | display_name   |
 +--------+-------+                 +--------+-------+
          |                                  |
          +----------------+-----------------+
                           |
                           v
              +--------------------------+
              | Protected app endpoints |
              | GET /locations/{code}   |
              | POST /location          |
              | GET /.../recording      |
              +--------------------------+

        separate admin/debug credential
        +-----------------------------+
        | Admin BearerToken           |
        | GET /sessions, GET /log,    |
        | upload/delete/merge         |
        +-----------------------------+
```

---

## Position payload (phone → server)

Each update from the phone includes:

- Runner identifier (server stores the authenticated member display name)
- Session code
- Latitude and longitude
- Compass heading in degrees (0–360, true north) — optional, omitted if unavailable
- Timestamp

Exact field names and payload structure to be defined during implementation.

---

## Position synchronisation

A key goal is showing all runners at the **same moment in time** on the map. The naive approach — each phone sends on a timer from whenever tracking started — means Runner A's last known position might be 45 seconds older than Runner B's, even if they're polling at the same interval.

**Strategy: clock-aligned recording intervals**

Instead of `"send every 60 seconds from now"`, each phone snaps its recording times to fixed wall-clock boundaries:

- 60-second interval → record at `17:47:00`, `17:48:00`, `17:49:00` …
- 30-second interval → record at `17:47:00`, `17:47:30`, `17:48:00` …

All phones independently align to the same slots without any coordination. iOS devices stay within ~50–200 ms of true UTC via NTP, which is negligible compared to GPS accuracy (~3–5 m) and a runner's movement over that time (~60 cm at jogging pace).

The `timestamp` field in the payload is the **capture time** (when the GPS fix was taken), not the send time. This means retried or delayed POSTs still carry the correct position timestamp. The viewer always shows where each runner *was* at the same moment, regardless of when their phone managed to upload it.

No server-side changes are needed — the server already stores and returns the timestamp from the payload.

---

## Out of scope (for now)

- Android app
- Persistent storage or run history
- Trail lines on the map (history of each runner's path)
- User accounts or login
- Push notifications
- Offline map tiles
- Public App Store or Play Store distribution
