# iOS

Native Swift app that signs in with a Dot Watcher account and sends GPS positions to the server every 15 seconds while tracking.

---

## Auth and session contract

- The app signs in or creates an account with `POST /auth/login` or `POST /auth/register`.
- User access tokens should be stored in Keychain, not `UserDefaults`.
- Sign-out should call `POST /auth/logout` and remove the token from Keychain.
- The app should load `GET /me/sessions` after sign-in and let the user select an existing membership, create a session with `POST /sessions`, or join from an invite code with `POST /session-invites/{inviteCode}/join`.
- Invite joins create `viewer` membership for new members and preserve existing roles. The app can post locations only when the selected membership role is `owner` or `runner`.
- Owners can manage members with `GET /sessions/{sessionCode}/members` and `POST /sessions/{sessionCode}/members/{userId}/role`; invite codes are not role grants.
- `POST /location` must send `Authorization: Bearer <user access token>`. The server stores the authenticated member display name and ignores any client-supplied runner name for identity.
- `GET /locations/{sessionCode}` and recording download return `403` for valid-looking session codes where the signed-in user is not a member.

## API endpoint configuration

The API base URL is configured through the `DOTWATCHER_API_BASE_URL` Xcode build setting, exposed to the app as `DotWatcherAPIBaseURL` in `Info.plist`.

Production builds should use:

```text
https://dot-watcher.skelstar.io/api
```

The app rejects non-HTTPS API URLs outside local debug development. Debug builds may use `http://localhost`, `http://127.0.0.1`, or `http://[::1]` for local server testing.

## Verification boundary

GitHub Actions does not currently build or run the iOS project. iOS verification is manual for now because it depends on local Xcode, signing, simulator/device availability, Keychain behavior, background-location permissions, and real GPS/background execution.

Manual pre-release checks should cover sign-in/register, Keychain persistence, logout revocation, create session, join invite, owner/runner-only posting, `401`/`403` handling, background location, clock-aligned posting, offline retry, and TestFlight packaging.

---

## Position recording strategy

Phones must not record on a simple repeating timer from app launch — if Runner A starts tracking at `17:46:03` and Runner B at `17:46:47`, their positions are always ~44 seconds apart even though they share the same interval.

**Use clock-aligned intervals instead.** On each tick, snap to the next wall-clock boundary:

```swift
let now = Date().timeIntervalSince1970
let delay = ((floor(now / interval) + 1) * interval) - now
try? await Task.sleep(for: .seconds(delay))
```

This means all phones independently fire at `17:47:00`, `17:47:03`, etc. iOS NTP sync keeps clocks within ~50–200 ms of each other — at jogging pace that's under 60 cm of position error, well within GPS accuracy.

**The `timestamp` in the POST payload is the GPS capture time, not the send time.** Record it when `CLLocation` is read, before the network request. This way retried or delayed POSTs still carry the correct position, and the viewer always shows a coherent snapshot of where everyone was at the same moment.

---

## Location manager configuration

```swift
clManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters
clManager.distanceFilter = 10.0          // no new fix if stationary
clManager.activityType = .fitness
clManager.pausesLocationUpdatesAutomatically = false  // critical for ultras
clManager.allowsBackgroundLocationUpdates = true
clManager.showsBackgroundLocationIndicator = true     // blue bar — required by Apple
```

**Why these settings:**

- **`NearestTenMeters`** — sufficient accuracy for a dot on a map; the GPS chip works less hard than `kCLLocationAccuracyBest`, saving battery over a multi-hour run.
- **`distanceFilter = 10.0`** — suppresses redundant fixes when the runner is standing still (aid station, toilet stop). The clock-aligned POST still fires on schedule; it just re-uses the last known position.
- **`activityType = .fitness`** — tells iOS this is a workout, preventing aggressive power-saving suspension of location updates.
- **`pausesLocationUpdatesAutomatically = false`** — iOS will otherwise silently stop updates during slow movement. Essential for ultra events where a runner may walk for long stretches.
- **`allowsBackgroundLocationUpdates = true`** — keeps the app posting when the screen is off. Requires `UIBackgroundModes: location` in `Info.plist`.
- **`showsBackgroundLocationIndicator = true`** — displays the blue status bar pill while background location is active. Required by Apple when using background location.

---

## Required Info.plist keys

```xml
<key>UIBackgroundModes</key>
<array>
    <string>location</string>
</array>
<key>NSLocationWhenInUseUsageDescription</key>
<string>DotWatcher uses your location to track your position during a run.</string>
<key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
<string>DotWatcher uses your location in the background to track your position during a run.</string>
```

The app requests "Always" authorisation so location continues when the screen locks mid-run.
