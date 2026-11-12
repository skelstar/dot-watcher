# Map pins: runners drop a message pin on the map

A runner presses and holds on the map in the app, enters a message, and a pin is placed there. In a named
session everyone in it sees the pin; on a Go (casual) run the runner's crew and anyone with their watch
link do. Tapping a pin shows the message. Runners get a local notification when a new pin appears near
them.

## Decisions
- Only runners can create pins (in the app). Web viewers can read only.
- Named session: pins live in `session_pins`, keyed by session. No expiry; deleted with the session.
- Go run (2026-11-12): pins live in `casual_run_pins`, on the runner's own open run. Seen by the same
  people as that run's route: the runner, their visible crew, and anyone with the run's watch link.
  Deleted when the run stops (and by the casual purge for abandoned runs). Only the runner drops pins
  on their run and only they delete them.
- One press-and-hold and one pin display in the shared iOS map shell (`TrackingMapScreen`, see
  [one-map-screen.md](one-map-screen.md)), so Go and sessions get pins together.
- No server push (APNs/FCM). New-pin alerts are local notifications fired from the existing background
  location poll, so they only work while the runner is tracking. Real push is a possible later add-on.
- Delete: the pin's creator or the session owner, from the pin's detail view (not press-hold on the map).
- Message is plain text only, max 140 chars. Cap of 50 pins per session, and 50 per Go run.
- No extra rate limiter for now: the 50-pin cap bounds it. Revisit if abused.

## 1. Server (branch `map-pins-server`)
- [x] Tables `session_pins` (`id`, `session_id`, `user_id`, `display_name`, `lat`, `lon`, `message`,
  `created_at`) and `casual_run_pins` (`id`, `run_id`, `lat`, `lon`, `message`, `created_at`), both
  cascading with their parent. `SessionStore.MapPins.cs`, `Models/MapPinModels.cs`.
- [x] The pin shape is `MapMessagePin { id, author, latitude, longitude, message, createdAt, isMine,
  canDelete }`; `isMine` and `canDelete` are worked out for the viewer, so clients need no user ids.
- [x] `POST /sessions/{id}/pins` (role `runner`), body `{ latitude, longitude, message }`; `GET
  /sessions/{id}/pins` (member); tokenless `GET /session-invites/{code}/pins`; `DELETE
  /sessions/{id}/pins/{pinId}` (author or owner). Blocked users' pins are hidden both ways.
- [x] `POST /casual/pins` (409 when not out or full), `DELETE /casual/pins/{pinId}`. Go pins come back
  inline in `GET /casual/live` (and the POST /location casual response) as `pins`, and in the watch
  view (`GET /watch/{token}`) as `pins`, so no extra polling.
- [x] Integration tests: `tests/DotWatcher.Server.Tests/MapPinTests.cs`.
- [ ] CI green.

## 2. Web (`client/`)
- `usePinLayer.ts` (pattern: `useRouteLayer`): poll `GET /session-invites/{code}/pins` on a slow
  interval (about 15 s), render a symbol layer. The Go watch page reads `pins` from the watch view it
  already polls.
- Click a pin: MapLibre popup with message, author and relative time. Render as text, never HTML.
- Read-only for now: no create or delete in the browser.

## 3. iOS (`ios/DotWatcher/DotWatcher`, on top of `one-map-screen-ios`)
- `LinzMapView` (MapLibre; `NativeMapView` is gone): long-press gesture -> dialog with a text field
  and a character counter -> `POST`, from `TrackingMapScreen` so both maps get it. Pins as markers;
  tap shows a sheet with message, author, time and a Delete button when `canDelete`.
- Go: pins arrive in `casual.loadLive()`. Session: fetch `GET /sessions/{id}/pins` alongside the
  existing refresh and on foreground.
- Local notifications: `UNUserNotificationCenter` permission requested the first time a runner starts
  tracking. On each poll, diff the pin ids against the last seen set; for a new pin not created by the
  user and within ~1 km of the runner's current location, post a notification (sound plus haptic).
  Persist the seen ids so a relaunch does not re-notify.

## 4. Android (follow-up)
Same as iOS; notification posted from `LocationTrackingService`.

## 5. Later / out of scope
- Snap to route: on create, snap to the nearest point on the loaded route if within ~30 m (Turf
  `nearestPointOnLine` or the Swift equivalent). Store the snapped coordinates; no server change.
- Pin categories/icons (water, hazard, cheering point).
- Real push notifications for viewers or runners not currently tracking.
- Creating pins from the web client (would need right-click or a "drop pin" button).

## Open questions
- Notification radius: 1 km proposed. Should it be configurable?
- Should pins created before a runner joined notify them, or only pins that appear while tracking?
  Proposed: only new ones while tracking.

## Verification
CI only (AGENTS.md); iOS visual check via the build-ios-simulator skill.
