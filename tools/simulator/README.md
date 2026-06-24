# Simulator

React + Vite dev tool for testing the Dot Watcher server locally. Lets you replay GPS position data without a phone.

---

## Prerequisites

- Node.js 18+
- Dot Watcher server running on `http://localhost:5000` (or configure via `.env`)

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

| Variable            | Description                            | Default                  |
| ------------------- | -------------------------------------- | ------------------------ |
| `VITE_SERVER_URL`   | URL of the Dot Watcher server          | `http://localhost:5000`  |
| `VITE_SESSION_CODE` | Session code shared with the map viewer | —                       |
| `VITE_BEARER_TOKEN` | Bearer token matching the server config | —                       |

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
