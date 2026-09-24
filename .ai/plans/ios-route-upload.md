# Add a session route (GPX) from the iOS app via a web upload page

A session member taps "Add route (GPX)" in the iOS header menu -> app opens a web page with a short-lived
token -> user picks a GPX file there -> server saves it -> app refetches and draws the route.

## Current state
- Server: `POST /sessions/{id}/route` (raw GPX body; admin token or session-member user token),
  `DELETE` (admin only), tokenless `GET /session-invites/{code}/route`
  ([SessionsController.cs](../../server/Controllers/SessionsController.cs)).
- Web client: viewer only, no credentials. Draws route via `useRouteLayer`, parses with `gpx.ts`.
  Only upload UI is the admin panel (`LoadRouteButton`).
- iOS: no route code (no upload, fetch or polyline).

## 1. Server
- `POST /sessions/{id}/route-upload-token` (any session member, `store.CanReadSession`): returns
  `{ token, expiresAt }`; iOS builds `{webBase}/route-upload/{sessionId}#token={token}`.
- Token (`Auth/RouteUploadTokenAuth.cs`): HMAC-signed, one session + route upload only, 15 min.
- `POST /sessions/{id}/route` also accepts this token (admin / member token paths unchanged).
- Not owner-restricted: any member can add/replace (owner check removed by decision).
- Optional: let member delete their route (`DELETE` via `CanManageRoute`) for "Remove route".

## 2. Web (`client/`)
- New route `/route-upload/{sessionId}` in `parseUrl` (App.tsx); read token from `location.hash`,
  then clear the hash.
- Page: file input (`.gpx`), parse with `parseGpxCoordinates`, preview on map via `useRouteLayer`,
  "Upload" -> `POST /sessions/{id}/route` with `Authorization: Bearer <token>`.
- States: expired/invalid token, invalid GPX, uploading, success ("Route added, return to the app").

## 3. iOS (`ios/DotWatcher/DotWatcher`)
- `ContentView.headerSection`: keep share icon; replace profile + help icons with an
  `ellipsis.circle` `Menu` (Account when signed in, Help, and route items).
  If only Help would be in the menu, keep the plain "?" icon.
- Route items (any session member): "Add route (GPX)…" / "Replace route (GPX)…" (route exists),
  optionally "Remove route". Tapping requests the upload link and opens it in
  `SFSafariViewController`.
- On dismiss / app foreground: refetch `GET /session-invites/{code}/route` (404 = no route);
  drives Add vs Replace label.
- `NativeMapView`: parse GPX track points (mirror `client/src/gpx.ts`), draw polyline; optional
  start/finish markers.

## Out of scope
Native file picker, "Open in DotWatcher" share-sheet import, route status row on the main screen.

## Status
Built: server (+tests), web page, iOS (menu, in-app browser, route fetch, polyline). iOS is
unbuilt/untested (no Xcode on the dev machine); route delete/remove not done.

## Verification
CI only (AGENTS.md); iOS visual check via the build-ios-simulator skill.
