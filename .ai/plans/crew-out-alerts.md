# Crew-out alerts: "DW: Mia has joined" while you're on a Go run

Branches: `crew-out-alerts-ios`, then `crew-out-alerts-android`. Client only: no server change.

## The bug, and why

While you're out on a Go run, nothing tells you when someone in your crew starts one. This was never built:

- iOS posts only one kind of local notification today, the satellite alert (`SatelliteAlerts.swift`).
- Android posts only the ongoing "Sharing your location" notification (`LocationTrackingService`).
- [audio-cues.md](audio-cues.md) put "your crew just started a run" out of scope. That plan was about alerts
  when you *aren't* tracking, which do need server push (APNs/FCM). While you're tracking, push isn't needed.

## What already exists

The reply to every Go `POST /location` is a `CasualLiveResponse` (`LocationsController.AddCasualLocation`),
the same thing the Go map polls from `GET /casual/live`:

- `positions`: you (`isSelf`) plus every crew member who is out and visible to you, by name, each with
  `inSession` (in a named session rather than on a Go run: the padlock).
- `nearby`: anonymous grey dots. These are never alerted on (they have no name).

The phone posts every interval with the screen locked, so the reply is a background signal we already
receive. The only work is to diff it.

- iOS today decodes the reply as `LocationPostResponse`, which drops `isSelf` and `inSession`.
- Android today decodes it into `RunnerPosition`, which also drops them.

## Decisions (proposed; confirm before building)

- **When:** only while you are tracking a Go run. Out of scope: a named session (its reply has no crew), and
  not tracking at all (needs push).
- **Who:** crew on a Go run (`inSession == false`, `isSelf == false`). Crew who are in a named session are left
  out at first, because the ask is about Go runs. Open question 1.
- **First reply after you start:** set the baseline silently. The people already out don't trigger alerts; the
  map already shows them.
- **"Newly out":** in this reply, but not in the previous successful one.
- **Re-alerts:** once per person per 30 minutes. This stops a dot that drops out for a bad-signal stretch and
  comes back from alerting twice.
- **Shape:** a local notification. Title "DW: Mia has joined" (the "DW:" names the app on a watch, which shows text but not our icon), body "Started a Go run", default sound. Tapping it opens
  the app on the Go map.
  - Several crew in one reply give one notification: "DW: Mia and Sam have joined".
- **App on screen:** still show it as a banner. The iOS delegate already returns `.banner` for satellite
  alerts, so the same `willPresent` path applies.
- **Switch:** "Crew alerts" in Settings, on by default. It asks for notification permission the first time you
  start a Go run with it on, as the satellite alert does.
- **Privacy is unchanged:** someone with `VisibleToCrew` off is never in the reply, so they never alert.

## iOS

- [x] Read `isSelf`/`inSession` from the Go post reply. Built as two optional fields on `RunnerPositionResponse`
      (nil in a session reply) rather than a second reply type, so the post path stays one.
- [x] `CrewOutAlerts.swift`: `CrewOutTracker` (the diff: `lastOut` nil until the baseline, `lastAlertedAt`,
      30 min cooldown) and `CrewOutAlerts` (feeds it from `LocationManager.post` on a successful Go post).
  - [x] Reset in `beginTracking` and `stop()`, so every run starts with a silent baseline.
- [x] Post with `UNUserNotificationCenter` (identifier `crew-out-<names>`, so repeats replace rather than stack).
      The `SatelliteAlerts` delegate already shows foreground banners.
  - [x] Tap opens the Go map: the delegate's `didReceive` checks the `crew-out` prefix and posts
        `.crewOutAlertTapped`; `CasualHost` presents the Go map if a Go run is still tracking.
- [x] Settings: "Crew alerts" switch (`LocationManager.crewOutAlertsEnabled`, `UserDefaults` key
      `crewOutAlertsEnabled`, default true). Permission is asked in `beginTracking` for a Go run when it's on.
- [x] Developer section: "Send test crew alert in 5s".
- [ ] Unit test the diff (baseline, new name, name leaves and returns inside and after 30 min, self and
      in-session ignored). The iOS project has no test target yet, so this waits for one; `CrewOutTracker`
      is pure so it can be tested as is.
- [ ] Build and check on a device (CI builds; the two-phone test is below).

## Android

- [ ] Give casual posts a `CasualLiveResponse` body: a separate `postCasualLocation` in `DotWatcherApi` or a
      lenient shared type. Don't widen `RunnerPosition`.
- [ ] `CrewOutAlerts` in `location/`: the same diff as iOS (same rules, same tests), reset when tracking starts and
      stops.
- [ ] `LocationTrackingService.postOrEnqueue`: on a successful casual post, feed the reply in.
  - [ ] Queued flushes in `flushQueue` don't alert, because they're stale.
- [ ] New channel "Crew alerts" at `IMPORTANCE_DEFAULT`, separate from the low-importance tracking channel, so
      people can mute one without the other. `POST_NOTIFICATIONS` is already requested in `MainActivity`.
- [ ] Tap opens `MainActivity` on the Go map (an intent extra).
- [ ] Settings switch and a developer test button, as on iOS.

## Testing

- [ ] Two phones, A and B, crew with each other. A starts Go and locks the phone. B starts Go: A gets "DW: B has joined"
      within one interval.
- [ ] B stops and restarts within 30 min: no second alert. After 30 min: alert.
- [ ] B turns off "visible to crew": no alert.
- [ ] A starts Go while B is already out: no alert.
- [ ] Bot dots (see bot-dot-army plan) can stand in for B if they can be crew.

Server and API: none. CI builds the clients (see AGENTS.md).

## Open questions

1. Should crew joining a *named* session also alert ("Mia is in a session")? The data is there (`inSession`).
2. Should the alert also be spoken when audio cues land ([audio-cues.md](audio-cues.md))? Probably yes, behind
   that plan's per-cue switch.
3. Do you want the same alert while you're in a *named* session? The session reply has no crew, so that needs
   the server to add crew to it (or the app to also call `GET /casual/live` from the background loop). Not part
   of this plan.
