# Simulator

React + Vite dev tool for testing the Dot Watcher server locally, without needing a phone.

---

## Prerequisites

- Node.js 18+
- Dot Watcher server running on `http://localhost:8080` (the repo-wide local-dev default — see
  the root `.env.example`) — or configure via `.env`

---

## Running

```bash
cd tools/simulator
npm install
npm run dev
```

Opens at `http://localhost:5174`.

---

## Configuration

Copy `.env.example` to `.env` and set:

| Variable            | Description                                                              | Default                                     |
| ------------------- | ------------------------------------------------------------------------- | -------------------------------------------- |
| `VITE_SERVER_URL`   | Full URL of the Dot Watcher server — overrides the port-derived default entirely | `http://localhost:<VITE_SERVER_PORT>`         |
| `VITE_CLIENT_URL`   | URL of the main web client's dev server, for the Session tab's embedded live view | `http://localhost:5173`                       |
| `VITE_BEARER_TOKEN` | Admin bearer token, matching the server's `BearerToken` config — needed for the Session tab's old-account cleanup | — |

`VITE_SERVER_PORT` isn't set in this tool's own `.env` — it's read from the **repo root's**
`.env` (copy `../../.env.example`), shared with `start-local.ps1` and `client/`, so every local
tool agrees on which port the server is running on. Change it in one place if `8080` is already
taken on your machine.

---

## Tabs

### Session

Simulates any number of independent "phones" that each join a session over the real
auth/session/location API and move toward a shared point on the map, for exercising
multi-runner scenarios without needing physical devices.

- **Session** — creates a session (registering a throwaway organizer account under the hood),
  shows its invite code, and seeds a default set of phones (see Phones below), auto-joined to
  it. Clicking **Create session**/**New session** first pops up a map modal asking for the
  convergence point — cancelling it aborts the whole thing, nothing is created. Once confirmed:
  deletes every existing account whose username starts with `sim-` (and, since deleting a user
  also deletes any sessions it owns, their sessions too) via the admin endpoints — best-effort,
  requires `VITE_BEARER_TOKEN` — so previous test runs don't pile up server-side junk you'd
  otherwise have to clean up by hand; then creates the session and seeds phones. Otherwise
  purely local UI state, not tied to any one session — you can also point individual phones at
  an invite code from a real session created via the iOS or web app.
- **Convergence point** — shown once a session exists, with a **Change** button that reopens
  the same picker modal at any time (pre-filled with the current point).
- **Movement** — shared speed (walk/jog/run) and update-interval controls used by every phone.
- **Live client view** — embeds the real web client (`client/`) in an iframe, pointed at
  `{CLIENT_URL}/code/{inviteCode}` — the one client URL form that skips the sign-in wall, so it
  shows the actual production map rendering next to the simulator controls. This is the only
  persistent map on the page; picking a convergence or start point uses a modal instead, to
  avoid a wall of maps as more phones are added. Requires the client's own dev server running
  separately (`cd client && npm run dev`); configure a non-default URL via `VITE_CLIENT_URL`.
- **Delete sim data** — next to **+ Add phone**: after a confirm naming the server, deletes `sim-`
  accounts there and with them everything they made: sessions they own, positions, Go runs, watch
  links and their activity events. While any phone here has an account (or a session is open) it
  reads **Delete other sim-phone data** and keeps those accounts and the session's organizer, so the
  test in progress carries on; otherwise it deletes every `sim-` account, as Create session does
  first. Needs `VITE_BEARER_TOKEN`.
- **Phones** — shown as a grid of compact tiles. Creating a session seeds three
  (`SK`, `DH`, `CH`), already auto-joined; click **+ Add phone** for more. Each phone
  independently:
  - Registers its own throwaway account and joins a session by invite code (`runner` role)
  - Is labelled by its 2-character initials (same algorithm as the client's
    `initialsFor`/`InitialsBadge`, e.g. "Phone 1" → "P1"), with a marker colour hashed from its
    display name the same way the client colours runners — so a phone gets the same colour and
    initials here as it would as a real runner on the client's map
  - Picks its starting point via a **Choose**/**Change** button (same picker modal as the
    convergence point), whenever you're ready — nothing pops up automatically
  - **Start tracking** starts sending live `POST /location` updates once it has a start point and the
    session has a convergence point. On Go there's nothing to press: tracking starts by itself as soon
    as the phone is out and has a start point, as Go does in the app. There's no pause, as in the real
    app: it tracks until Stop/Leave. **Missing** below is the phone going quiet (no signal, flat battery)
  - Can be switched between **Good** (heads straight for the convergence point, heading set to
    the bearing of travel — and stops reporting heading once "arrived", matching the real
    app's behaviour when stationary), **Bad GPS** (wanders randomly instead of converging, with
    heading always omitted — this is what the client's GPS-signal-loss indicator keys off),
    **Missing** (stops sending updates entirely, so the client's data-gap "missing" indicator
    kicks in after ~60s), or **Satellite** (moves and reports heading exactly like Good — this
    only flips `isUltraConstrained: true` on the post, the same self-reported flag iOS sends from
    `NWPath.isUltraConstrained`, so it exercises the client's satellite indicator independently of
    GPS quality or posting cadence)
  - Separately, battery can be toggled between **Battery: normal** (sends nothing, matching the
    real app when the level is unavailable), **Battery: low** (sends `batteryLevel: 25`), and
    **Battery: critical** (sends `batteryLevel: 5`) — a fixed value per mode, not a drain
    simulation, just enough to exercise the client's two battery badge thresholds (see
    `.ai/plans/battery-level-reporting.md`) independently of GPS quality/satellite
  - Can be made **crew** with other phones: each phone shows a **Crew:** row with a tick box per other
    phone, from the start. A tick set before both have accounts shows "(once both apps are open)" and
    is made real as soon as both have. Ticking makes them crew the app's way (one
    makes a crew code, the other redeems it), switching Go and crew on for both accounts first; crew is
    both ways, so the other phone shows the tick too. Unticking removes them from each other's crew.
    Needs `VITE_BEARER_TOKEN`.
  - Can leave the session independently at any time
  - Has an app **Foreground/Background** switch (top right). A new phone's app is closed: switch it on
    before **Join** or **Go** (phones seeded by Create session open it themselves). Opening it the first
    time makes the phone's account, as a real user is already signed in when they open the app, so
    "Opened the app" is recorded then, not when Go or Join is pressed; the name is fixed from then on.
    Each change is reported (`POST /me/activity`, as the real app is to) for the admin Activity tab,
    where the line is dashed while the app is away; positions keep going, as they do from a real phone
    in a pocket. Once the phone is out or joined, the app goes back to the background 10 s after it was
    last used unless that box is unticked. In the background the app's own buttons (Share a watch link,
    Stop/Leave) are disabled until the switch is turned on again; the simulator's controls (start
    point, GPX, Start tracking, GPS quality, battery, Add a viewer) still work.
  - Or, instead of joining, can **Go (no session)**: like the app's Go sign, it registers an account,
    switches Go (`casual`) on for it through the admin endpoints (needs `VITE_BEARER_TOKEN`), starts a
    casual run and starts tracking straight away, posting its positions there. It needs only a start
    point (pick one before or after pressing Go): with no route or
    convergence point it walks in a straight line on a random bearing. Once out it can **Share a watch link**, then **Open
    watch link** (the real watch page, in a new tab) or **Add a viewer** (a pretend browser with its
    own id that polls every 10 s, like the watch page with its tab on screen, until **Stop a viewer**
    closes the longest-watching one; each is one viewing, with its length, on the Activity tab), and
    **Stop** ends the run (and its viewers). These make the requests the admin **Activity** tab
    draws (started, shared, someone watched, stopped; see `.ai/plans/activity-flow.md`). Creating a
    session deletes old `sim-` accounts, and their activity goes with them.
  - Renders on both its own mini map and the shared convergence map using the same visual
    language as the client's runner markers (`Arrow.tsx`): a coloured circle with a heading
    chevron, a dashed-border "missing" look for a data gap, a solid-border "stationary" look on
    arrival, and the small red "!" signal-loss badge whenever heading is null

---

### GPX Converter (Importer)

Converts one or more GPX files into a combined session recording — for producing NDJSON test
data to upload as a session recording, rather than for live simulation (see Session above
for that).

- Add a **runner name** and **GPX file** per slot (drag-and-drop or file picker); **+ Add runner** adds more slots.
- A shared **session code** and replay **interval** (15s/30s/1min/5min) apply to every slot.
- Points are **compressed to the chosen interval** and thinned to at least 10m apart when the file contains real timestamps.
- Files **without timestamps** (route files rather than recorded activities) get one generated per point from a configurable start time (plus an optional 12h shift).
- **Download NDJSON** merges every named, non-empty slot into one time-sorted `{code}.ndjson` file — the format expected by `POST /sessions/{sessionId}/recording`.

---

## Shared library — `src/lib/positions.ts`

Exports reusable utilities for position data manipulation:

| Export               | Description                                                                 |
| -------------------- | --------------------------------------------------------------------------- |
| `Position`           | TypeScript interface: `{ latitude, longitude, heading, timestamp }`         |
| `compressToInterval`  | Thins an array of positions to one per interval (by timestamp)              |
| `filterByDistance`    | Drops consecutive positions closer together than a minimum distance         |
| `computeBearing`      | Returns compass bearing (0–360°) between two lat/lon pairs                  |

Import in any simulator page:

```ts
import { type Position, compressToInterval, filterByDistance, computeBearing } from './lib/positions'
```

---

## Dev-server middleware

All endpoints are registered in `vite.config.ts` and are dev-only — not included in production builds.

### `POST /api/save-route`

Writes a JSON file to `data/routes/`. Returns `409` if the file exists and `force` is `false`.

```json
{ "filename": "my-route.json", "content": "...", "force": false }
```
