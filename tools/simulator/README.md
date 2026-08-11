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
  - Starts/pauses sending live `POST /location` updates once it has a start point and the
    session has a convergence point
  - Can be switched between **Good** (heads straight for the convergence point, heading set to
    the bearing of travel — and stops reporting heading once "arrived", matching the real
    app's behaviour when stationary), **Bad GPS** (wanders randomly instead of converging, with
    heading always omitted — this is what the client's GPS-signal-loss indicator keys off),
    **Missing** (stops sending updates entirely, so the client's data-gap "missing" indicator
    kicks in after ~60s), or **Satellite** (moves and reports heading exactly like Good — this
    only flips `isUltraConstrained: true` on the post, the same self-reported flag iOS sends from
    `NWPath.isUltraConstrained`, so it exercises the client's satellite indicator independently of
    GPS quality or posting cadence)
  - Can leave the session independently at any time
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
