# Dot Chaser

Real-time GPS tracking for running groups. Runners share their position during a group run, race, or solo outing. Supporters and fellow runners can watch everyone's location on a live map in a browser.

---

## Repo structure

```
dot-chaser/
  server/      .NET Core minimal API
  client/      React web app (Mapbox)
  ios/         Swift iOS app
  android/     Kotlin Android app (not started)
  README.md
```

---

## How it works

1. Each runner opens the iOS app, enters their name and a session code, and starts tracking
2. The app sends GPS coordinates and compass heading to the server at a configurable interval (default 60s)
3. The server stores the latest positions for all runners in that session
4. Anyone with the session code opens the web viewer in a browser
5. The web viewer polls the server every 10–15 seconds and renders all runners as directional markers on a Mapbox map

---

## Components

### server

A lightweight .NET Core minimal API.

**Responsibilities**

- Receive position updates from phone apps (`POST /location`)
- Store position history for all runners in memory (full history, not just latest)
- Serve current runner positions to the web viewer (`GET /positions/{sessionCode}`)
- No geometry or bearing logic — that is handled by the client

**Auth**

- POST endpoint requires a bearer token in the `Authorization` header (used by phone apps only)
- GET endpoint requires a valid session code in the URL path (used by web viewers)

**Hosting**

- Runs on Tatooine (home server, k3s cluster)
- Exposed publicly via Cloudflare Tunnel
- HTTPS handled by Cloudflare

**Data**

- In-memory storage only (no database)
- Stores full position history per runner per session
- Designed for up to 20 concurrent runners
- Session data can be cleared between runs

---

### client

A React web app using Mapbox GL JS.

**Behaviour**

- Full-screen Mapbox map, responsive and touch-friendly
- On first load, prompts for a session code if not present in the URL
- Session code can be embedded in the URL for easy sharing (e.g. `https://dot-chaser.yourdomain.com/SESSIONCODE`)
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

- Runner enters their display name
- Runner enters the session code
- Runner starts/stops tracking manually

**Auth**

- Sends a bearer token in the `Authorization` header on every POST

**Distribution**

- Distributed via TestFlight (not the App Store)

---

## Auth model

| Actor      | Method                         | Auth                                   |
| ---------- | ------------------------------ | -------------------------------------- |
| Phone app  | `POST /location`               | Bearer token in `Authorization` header |
| Web viewer | `GET /positions/{sessionCode}` | Session code in URL path               |

The bearer token (for phone apps) and the session code (for viewers) are separate credentials. The session code is safe to share publicly — it only grants read access to positions.

---

## Position payload (phone → server)

Each update from the phone includes:

- Runner identifier (name or ID set in the app)
- Session code
- Latitude and longitude
- Compass heading in degrees (0–360, true north) — optional, omitted if unavailable
- Timestamp

Exact field names and payload structure to be defined during implementation.

---

## Out of scope (for now)

- Android app
- Persistent storage or run history
- Trail lines on the map (history of each runner's path)
- User accounts or login
- Push notifications
- Offline map tiles
- Public App Store or Play Store distribution
