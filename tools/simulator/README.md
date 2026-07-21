# Simulator

React + Vite dev tool for testing the Dot Watcher server locally. Lets you replay GPS position data without a phone.

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
| `VITE_SESSION_CODE` | Session code shared with the map viewer                                  | —                                            |
| `VITE_BEARER_TOKEN` | Bearer token matching the server config                                  | —                                            |

`VITE_SERVER_PORT` isn't set in this tool's own `.env` — it's read from the **repo root's**
`.env` (copy `../../.env.example`), shared with `start-local.ps1` and `client/`, so every local
tool agrees on which port the server is running on. Change it in one place if `8080` is already
taken on your machine.

---

## Tabs

### Stepper

Replays pre-loaded position data for two runners (Sean and David) in a step-by-step table. Each row represents one position update; click a checkbox to POST it to the server. Rows must be submitted in order.

- Position data is loaded from `src/positions.json` (Sean) and `data/routes/` (David).
- **Reset** clears the session on the server and resets the UI.

### Location

Sets the simulated GPS location of a booted iOS simulator by clicking on a map.

- Lists all **booted simulators** via `xcrun simctl list devices`.
- Select a simulator from the dropdown, then **click anywhere on the map** to teleport it to that location.
- Uses [OpenStreetMap](https://www.openstreetmap.org/) tiles via Leaflet — no API key required.

**First-time setup** — the app must have location permission on the simulator. Grant it without a prompt via:

```bash
xcrun simctl privacy <udid> grant location io.skelstar.DotWatcher
```

Or reset all permissions so the app re-prompts on next launch:

```bash
xcrun simctl privacy <udid> reset all io.skelstar.DotWatcher
```

---

### Convergence

Simulates any number of independent "phones" that each join a session over the real
auth/session/location API (not the admin bearer token) and move toward a shared point on the
map, for exercising multi-runner scenarios without needing physical devices.

- **Session** — creates a session (registering a throwaway organizer account under the hood)
  and shows its invite code. Purely local UI state, not tied to any one session — you can also
  point individual phones at an invite code from a real session created via the iOS or web app.
- **Convergence point** — click the map (centered on Wellington, NZ) to choose where phones
  with "Good" GPS head towards.
- **Movement** — shared speed (walk/jog/run) and update-interval controls used by every phone.
- **Live client view** — embeds the real web client (`client/`) in an iframe, pointed at
  `{CLIENT_URL}/code/{inviteCode}` — the one client URL form that skips the sign-in wall, so it
  shows the actual production map rendering next to the simulator controls. Requires the
  client's own dev server running separately (`cd client && npm run dev`); configure a
  non-default URL via `VITE_CLIENT_URL`.
- **Phones** — shown as a grid of compact tiles. Click **+ Add phone** to add one. Each phone
  independently:
  - Registers its own throwaway account and joins a session by invite code (`runner` role)
  - Is labelled by its 2-character initials (same algorithm as the client's
    `initialsFor`/`InitialsBadge`, e.g. "Phone 1" → "P1"), with a marker colour hashed from its
    display name the same way the client colours runners — so a phone gets the same colour and
    initials here as it would as a real runner on the client's map
  - Picks its own starting point on a mini map
  - Starts/pauses sending live `POST /location` updates once it has a start point and the
    session has a convergence point
  - Can be switched between **Good** (heads straight for the convergence point, heading set to
    the bearing of travel — and stops reporting heading once "arrived", matching the real
    app's behaviour when stationary), **Bad GPS** (wanders randomly instead of converging, with
    heading always omitted — this is what the client's GPS-signal-loss indicator keys off), or
    **Missing** (stops sending updates entirely, so the client's data-gap "missing" indicator
    kicks in after ~60s)
  - Can leave the session independently at any time
  - Renders on both its own mini map and the shared convergence map using the same visual
    language as the client's runner markers (`Arrow.tsx`): a coloured circle with a heading
    chevron, a dashed-border "missing" look for a data gap, a solid-border "stationary" look on
    arrival, and the small red "!" signal-loss badge whenever heading is null

---

### GPX Converter

Converts a GPX file into the JSON position format used by the simulator and server.

- **Drag and drop** a `.gpx` file onto the drop zone (or use the file picker).
- Points are **compressed to one-per-minute** intervals when the file contains real timestamps.
- Files **without timestamps** (route files rather than recorded activities) show a notice with configurable start time and interval controls to generate timestamps.
- The converted JSON is **automatically saved** to `data/routes/<filename>.json` via a Vite dev-server middleware.
  - If the file already exists you are asked whether to replace it.
- **Copy JSON** and **Download JSON** buttons are also available for manual export.

#### Output format

Each point in the exported JSON matches the format expected by `POST /location`:

```json
[
  {
    "latitude": -41.284898,
    "longitude": 174.756829,
    "heading": 90.0,
    "timestamp": "2026-06-07T04:08:43.000Z"
  }
]
```

Headings are computed automatically from bearing between consecutive points. The first point always has `heading: null`.

---

## Shared library — `src/lib/positions.ts`

Exports reusable utilities for position data manipulation:

| Export              | Description                                                                 |
| ------------------- | --------------------------------------------------------------------------- |
| `Position`          | TypeScript interface: `{ latitude, longitude, heading, timestamp }`         |
| `compressToMinutes` | Thins an array of positions to one per minute (by timestamp)                |
| `computeBearing`    | Returns compass bearing (0–360°) between two lat/lon pairs                  |

Import in any simulator page:

```ts
import { type Position, compressToMinutes, computeBearing } from './lib/positions'
```

---

## Dev-server middleware

All endpoints are registered in `vite.config.ts` and are dev-only — not included in production builds.

### `POST /api/save-route`

Writes a JSON file to `data/routes/`. Returns `409` if the file exists and `force` is `false`.

```json
{ "filename": "my-route.json", "content": "...", "force": false }
```

### `GET /api/simulators`

Returns all booted simulators as `[{ udid, name, runtime }]` by calling `xcrun simctl list devices --json`.

### `POST /api/simulators/:udid/location`

Teleports a simulator to the given coordinates via `xcrun simctl location <udid> set <lat>,<lon>`.

```json
{ "lat": 37.7749, "lon": -122.4194 }
```
