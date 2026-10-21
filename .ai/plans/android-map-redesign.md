# Android in-session map redesign

Source design: "In-Session Map Redesign v2" artifact — https://claude.ai/artifact/VqPHo7oPWo7SNaXVosb8RL
(v1, superseded: https://claude.ai/artifact/PTLgNzq52wnXqHyftefiUk). The design is labelled iOS; this plan
ports it to Android. Branch: `android-milestone-4-design`.

## Status

| # | Item | Status |
|---|------|--------|
| 1 | Full-screen map, floating controls (no TopAppBar / FAB) | Done, uncommitted, not compiled |
| 2 | Wordmark with initials as the "o", top-left | Done |
| 3 | Auto-stop countdown circle (green → amber + flashing in last 30 min) | Done |
| 4 | Right rail: Share, Invite code dialog, ⋯ menu with Leave | Done |
| 5 | Round Stop pill, bottom-right, while sharing | Done |
| 6 | Fit-all button on the rail | Done |
| 7 | Tap a dot to follow; drag > 10% of visible width releases follow / fit-all | Done |
| 8 | Own-dot ring that empties toward the next position send | Done (15 s cadence, 1 step/s; the white border of the dot is the countdown) |
| 9 | Marker badges: satellite, warning + faded pin (stale), pause (own dot) | Satellite + stale/disconnected done; pause badge blocked on battery-pause state |
| 10 | Tap countdown → extend sheet (+30 min / +1 hr / +2 hr, + "Stop now") | Done (service `extend()`; `totalDuration` now in `TrackingState`; durations are placeholders) |
| 11 | Satellite banner (title, send/refresh interval, last sent), map locked + dimmed | Not started — see "Groundwork for 11 and 12" |
| 12 | Battery-paused card (Send now / Auto / Stop), own dot grey with low-battery badge, no ring | Done. Android now also reports `batteryLevel` on every post. Test: emulator ⋯ → Battery → level ≤10, "Not charging" |
| 13 | Ending-soon pill ("Auto-stops in N min") | Done |
| 14 | Replace text-glyph icons with vector icons | Done (design SVG paths drawn on Canvas) |
| 15 | Globe button (open web map) on the rail — in the design, missed earlier | Done |

## Next steps

1. **Compile check** — `./gradlew :app:compileDebugKotlin` from `android/` (CI also does this on PR).
2. **Map interaction (6, 7)** in `MapScreen.kt`. Today it calls `fitToPositions` on every poll, which
   fights follow. Needs: fit-all state, follow target, a camera-idle compare of centre vs. aimed point
   (threshold 0.1 × visible longitude span), and ignoring our own animated re-centring. Fit-all target
   must account for any half-open sheet (iOS `visibleFraction`).
3. **Markers (8, 9)** — replace the arrow/dot bitmaps with a composed icon (initials, ring, badge).
   Own dot needs next-send time from the service.
4. **Service API (10)** — `LocationTrackingService` has no extend call and `TrackingState.Tracking`
   doesn't carry the chosen duration (the countdown arc currently uses "remaining at first show").
5. **State plumbing (11, 12)** — need ultra-constrained (satellite) and critical-battery-pause state
   exposed to the UI. See `.ai/plans/satellite-alerts.md`, `critical-battery-pause.md`,
   `persist-isUltraConstrained.md`, `battery-level-reporting.md`.

## Design decisions worth keeping

- Wordmark has no background, thin white halo for legibility.
- No bottom dock: banners/sheets come up from the bottom edge and cover Stop, so each carries its own Stop.
- Zoom must never release follow (it changes the span, not the centre).
- Satellite mode: positions keep refreshing at the slow interval, map locked, follow off, taps on dots do nothing.
- Extend durations (+30 min / +1 hr / +2 hr) are placeholders in the design — confirm before building.

## Groundwork for 11 and 12 (planned 2026-10-16, not started)

Both banners need Android to *know* a state first. Neither is UI-first work. Suggested order: 12
(battery) before 11 (satellite), because battery needs no new platform capability and is verifiable
on the emulator.

### 12 — Battery-paused card

iOS behaviour (see `critical-battery-pause.md`): at `batteryLevel <= 10` **and** not charging **and**
not already overridden this session, stop location updates and skip the post; show a persistent card
with **Send now** (one-shot, does *not* resume), **Auto** (explicit override, sticky until next
`start`) and Stop. Auto-resumes when charging. No server change.

Android work:
1. **Read battery.** Sticky `ACTION_BATTERY_CHANGED` broadcast gives level, scale and plugged/charging
   state with no permission. Add `BatteryReader` in `location/`.
2. **Report it.** `LocationUpdate` has no `batteryLevel`; server already accepts it
   (`battery-level-reporting.md`). Add the field to `LocationUpdate`/`QueuedCapture` (bump the
   persisted queue schema carefully: default null keeps old entries readable).
3. **Pause in the service.** New `TrackingState.Tracking.pausedForBattery`, plus
   `batteryOverride` flag reset in `startTracking`. At the top of each loop pass: if critical, not
   charging, not overridden → `locationTracker.stopHeadingUpdates()`-style stop of location updates,
   set paused, skip capture. Resume when charging or on override.
4. **Actions.** `LocationTrackingService.sendNow(context)` (one capture + post, stays paused) and
   `resumeAutomatic(context)` (sets override), same intent pattern as `extend()`.
5. **UI.** Card over the bottom edge with its own Stop (the mock's rule); own dot grey with pause
   badge and **no** countdown ring (pin spec: new `PinState`/flag; the ring already keys off
   `ringStep`, so pass null while paused). Hide the countdown-ring while paused.

Open questions: threshold 10 is copied from iOS and the web client — keep in sync. Does Android's
fused location provider need `removeLocationUpdates` or is `captureOnce` already one-shot? (It is
one-shot per call; the heading listener is the only continuous work, so the saving on Android is
smaller than on iOS. Worth measuring before promising a battery benefit.)

### 11 — Satellite banner

iOS behaviour: `NWPath.isUltraConstrained` → slower cadence (`ultraConstrainedInterval`), banner,
alert notification (`satellite-alerts.md`, PR #175 unverified), non-essential traffic suppressed.

Android work — **the platform capability is the unknown**:
1. **Detect.** *Spike result (2026-10-16, read from the installed SDK jars with `javap`):*
   - `NetworkCapabilities.TRANSPORT_SATELLITE` (= 10) exists from **API 35** (present in
     `android-35` and `android-36`).
   - `NetworkCapabilities.NET_CAPABILITY_NOT_BANDWIDTH_CONSTRAINED` (= 37) exists only from
     **API 36** (absent from `android-35`) — the analogue of iOS's `isUltraConstrained`: the
     *absence* of this capability means a bandwidth-constrained path.
   - `SatelliteManager` exists too, but is about the radio's own satellite state, not the data path.
   Plan: register `ConnectivityManager.registerDefaultNetworkCallback` and compute
   `constrained = hasTransport(TRANSPORT_SATELLITE) || !hasCapability(NOT_BANDWIDTH_CONSTRAINED)`,
   each guarded by `Build.VERSION.SDK_INT` (minSdk is 26, so below 35 it is always `false`). Beware
   that "no NOT_BANDWIDTH_CONSTRAINED" must only count on API 36+, otherwise every older device would
   read as constrained. Expose as `isUltraConstrained: StateFlow<Boolean>`. **Still unverified on a
   real satellite connection** (emulator can't), so keep a debug override.
2. **Treat as unverified until a real device shows it.** If detection can't be tested, ship the
   plumbing behind the same flag the server already persists and don't claim satellite support in
   release notes.
3. **Cadence.** Mirror iOS: slower post interval while constrained (the service already has a single
   `POST_INTERVAL_SECONDS`; make it a function of state). Ring and countdown must follow it.
4. **Report it.** Add `isUltraConstrained` to `LocationUpdate` (server field exists), so other
   runners see the badge we already render.
5. **UI.** Full-width amber banner at top, map dimmed and locked, three lines: title, send/refresh
   interval, last sent. Follow off, dot taps ignored, fit-all held on (mock). Needs a "last
   successful post" timestamp in `TrackingState`.

### Shared prerequisites

- `TrackingState.Tracking` grows `lastSentAt`, `pausedForBattery`, `isUltraConstrained`.
- A debug override (like iOS's `debugForceUltraConstrained`) so both states can be exercised on the
  emulator and TestFlight-equivalent builds.
