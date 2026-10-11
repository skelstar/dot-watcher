# Satellite Alerts — runner notification when the phone switches to satellite

*DotWatcher iOS — implemented 2026-10-11*

## What it is

A local notification on the **runner's own phone** when `NWPath.isUltraConstrained` flips from
false to true while tracking ("On satellite — No cell signal — still sending your position, less
often."). Not sent to other session members; that would need server push (APNs) and was
explicitly out of scope. See satelite-connectivity.md for the satellite background.

## How it works

- **Trigger:** `LocationManager.handlePathChange` — the existing `enteredSatellite` transition.
  That function already bails unless `isTracking`, so alerts only fire while tracking.
- **Background:** works while the app is running under the `location` background mode
  (`allowsBackgroundLocationUpdates = true`), which keeps the `NWPathMonitor` callback alive.
  A local notification needs no network. Does *not* work if the user force-quits the app or
  tracking isn't running (nothing is posting positions then either).
- **Delivery:** `SatelliteAlerts.swift` (singleton, `UNUserNotificationCenterDelegate`).
  - Fixed notification identifier + 5-minute cooldown so cell/satellite flapping doesn't spam.
  - Delegate is installed in `DotWatcherApp.init` so the banner also shows in the foreground.
  - Permission is requested the first time tracking starts, or when the toggle is first switched
    on (`requestAuthorizationIfNeeded` is a no-op once answered).
- **Settings** (persisted in `UserDefaults` via `LocationManager`, both default **on**):
  - `satelliteAlertsEnabled` — master toggle.
  - `satelliteAlertSound` — default notification sound vs silent banner.
- **UI:** `AuthSheet` is now the **Settings** sheet when signed in (still "Account" for the
  sign-in form). New "Notifications" section: "Satellite alerts" + "Play sound" toggles, and an
  "Allow notifications in Settings" button + footer when iOS permission is denied (re-checked on
  scene-active).

## Decisions

- Reused the existing account sheet as the settings sheet rather than building a new one; split
  it later if/when more options arrive.
- Sound is an on/off toggle only. A sound *picker* would need bundled `.caf` files (<30s) via
  `UNNotificationSound(named:)` — none exist in the project yet. Easy to add as another row.
- No "left satellite" alert (could be added next to the `enteredSatellite` branch).
- Android not touched.

## Not verified

Built/tested by CI only (AGENTS.md). To test manually: Debug builds have "Force satellite mode"
in Settings — start tracking, toggle it on, expect the banner; lock the screen first to check the
background case. Watch for a ContentView type-check timeout (the menu label gained two ternaries).

## Files

`SatelliteAlerts.swift` (new), `LocationManager.swift`, `AuthSheet.swift`, `ContentView.swift`,
`DotWatcherApp.swift`, `Help.md`.
