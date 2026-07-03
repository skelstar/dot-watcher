# iOS

Native Swift app that signs in with a Dot Watcher account and sends GPS positions to the server while tracking. Runners join a session, start tracking, and appear as dots on the web viewer map.

---

## Build configurations

Three Xcode build configurations control which server the app talks to:

| Configuration | Target | API URL |
| --- | --- | --- |
| **Debug** | iOS Simulator | `http://localhost:8080` |
| **Device** | Physical device (via USB) | `http://jakkuu.local:8080` |
| **Release** | TestFlight / App Store | `https://dot-watcher.skelstar.io/api` |

Switch between them by changing the active scheme in Xcode's toolbar:

- **DotWatcher** — uses the Debug configuration (simulator, localhost)
- **DotWatcher (Device)** — uses the Device configuration (physical device, Mac hostname)

The API base URL is set in `DOTWATCHER_API_BASE_URL` (build setting) and read by the app at runtime from `DotWatcherAPIBaseURL` in `Info.plist`.

### Physical device setup

When running on a physical device, the server must bind on all interfaces so the phone can reach it over WiFi:

```bash
cd server
dotnet run --urls "http://0.0.0.0:8080"
```

`Info.plist` contains an App Transport Security exception for `jakkuu.local` so plain HTTP is allowed on Device builds. The phone and Mac must be on the same WiFi network.

---

## Session flow

After signing in, the main screen leads with joining a session, since most users arrive with an invite code from someone else:

1. **Join via invite code** — `POST /session-invites/{inviteCode}/join`. Joining via the iOS app requests `runner` role; joining via the web client creates `viewer` membership.
2. **Create** — a small "Have your own session? Create one" link opens a sheet that calls `POST /sessions`, making the user the session owner. Share the generated invite code or link with other runners.

The session row (once in a session) is swipeable:
- **Swipe right** — reveals share buttons: SMS, Email, WhatsApp, Map. Each sends or opens the invite link.
- **Swipe left** — reveals a red Leave button. Owners leave without deleting the session; the session persists for other members.

---

## Auth and session contract

- The app signs in or creates an account with `POST /auth/login` or `POST /auth/register`.
- User access tokens should be stored in Keychain, not `UserDefaults`.
- Sign-out should call `POST /auth/logout` and remove the token from Keychain.
- Account deletion should call `DELETE /me`, stop tracking, clear local account state, and remove the token from Keychain.
- After sign-in, load `GET /me/sessions` to show the user's current memberships.
- The app can post locations only when the selected membership role is `owner` or `runner`.
- `POST /location` must send `Authorization: Bearer <user access token>`. The server stores the authenticated member display name and ignores any client-supplied runner name.
- `GET /locations/{sessionCode}` returns `403` for valid-looking session codes where the signed-in user is not a member.

---

## Verification boundary

GitHub Actions does not currently build or run the iOS project. iOS verification is manual because it depends on local Xcode, signing, simulator/device availability, Keychain behavior, background-location permissions, and real GPS/background execution.

Manual pre-release checks should cover sign-in/register, Keychain persistence, logout revocation, create session, join via invite, owner/runner-only posting, `401`/`403` handling, background location, clock-aligned posting, offline retry, and TestFlight packaging.

The app links to:

- Privacy Policy: `https://dot-watcher.skelstar.io/privacy`
- Terms of Use: `https://dot-watcher.skelstar.io/terms`

These pages are beta-oriented drafts and should be reviewed before public App Store release.

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
<key>NSAppTransportSecurity</key>
<dict>
    <key>NSExceptionDomains</key>
    <dict>
        <key>jakkuu.local</key>
        <dict>
            <key>NSExceptionAllowsInsecureHTTPLoads</key>
            <true/>
        </dict>
    </dict>
</dict>
<key>LSApplicationQueriesSchemes</key>
<array>
    <string>whatsapp</string>
</array>
```

The `NSAppTransportSecurity` exception allows plain HTTP to `jakkuu.local` for Device builds. The `LSApplicationQueriesSchemes` entry allows `UIApplication.canOpenURL` to check whether WhatsApp is installed before showing the WhatsApp share button.

The app requests "Always" authorisation so location continues when the screen locks mid-run.
