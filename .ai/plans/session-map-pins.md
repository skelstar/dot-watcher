# Map pins: runners drop a message pin on the map, everyone in the session can see it

A runner presses and holds on the map in the app, enters a message, and a pin is placed there. Other
session members see the pin on the web client and in the app; tapping a pin shows the message. Runners
get a local notification when a new pin appears near them.

## Decisions
- Only runners can create pins (in the app). Web viewers can read only.
- Pins live in their own table keyed by session id. No expiry; they are deleted with the session.
- No server push (APNs/FCM). New-pin alerts are local notifications fired from the existing background
  location poll, so they only work while the runner is tracking. Real push is a possible later add-on.
- Delete: the pin's creator or the session owner, from the pin's detail view (not press-hold on the map).
- Message is plain text only, max 140 chars. Cap of 50 pins per session.

## 1. Server
- Table `session_pins` (`SessionStore.cs`, `CREATE TABLE IF NOT EXISTS`): `id`, `session_id`,
  `user_id`, `display_name`, `lat`, `lon`, `message`, `created_at`. Index on `session_id`.
- `POST /sessions/{id}/pins` (user token, role `runner`, not left): body `{ lat, lon, message }`.
  Validate lat/lon range, trim message, reject empty or >140 chars, reject at the 50-pin cap.
- `GET /sessions/{id}/pins` (member token, `CanReadSession`) and tokenless
  `GET /session-invites/{code}/pins` for the web viewer (mirrors the route and locations endpoints).
- `DELETE /sessions/{id}/pins/{pinId}`: creator or session owner only.
- Blocked users' pins are hidden from users who blocked them, same as the existing block behaviour.
- Rate limit creation (reuse the `AuthAttemptLimiter` pattern or a small per-user limiter).
- Integration tests under `tests/`: create/list/delete, viewer forbidden, cap, validation, delete
  permissions.

## 2. Web (`client/`)
- `usePinLayer.ts` (pattern: `useRouteLayer`): poll `GET /session-invites/{code}/pins` on a slow
  interval (about 15 s), render a symbol layer.
- Click a pin: MapLibre popup with message, author and relative time. Render as text, never HTML.
- Read-only for now: no create or delete in the browser.

## 3. iOS (`ios/DotWatcher/DotWatcher`)
- `NativeMapView`: long-press gesture (runners only) -> dialog with a text field and a character
  counter -> `POST`. Annotation view for pins; tap shows a sheet with message, author, time and a
  Delete button when allowed (creator or owner).
- Fetch pins alongside the existing refresh and on foreground.
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
