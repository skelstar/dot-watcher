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
| 10 | Tap countdown → extend sheet (+30 min / +1 hr / +2 hr, + "Stop now") | Not started, blocked on service API |
| 11 | Satellite banner (title, send/refresh interval, last sent), map locked + dimmed | Not started, blocked on state |
| 12 | Battery-paused card (Send now / Auto / Stop), own dot grey, no ring | Not started, blocked on state |
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
