# One tracking map screen (Go and named sessions)

Branches: `one-map-screen-ios`, then `one-map-screen-android`. Client-only: no server or API change.
Best after [unify-position-model](unify-position-model.md), but not blocked by it (see Order).

## Why

Each app has two full-screen maps for while you're sharing: one for Go (casual) and one for a named
session. They show the same thing (dots on the LINZ map, a timer, a rail of buttons, Hold to stop), but
each screen keeps its own copy of the camera, fit/follow, route and stop logic. Fixes have to land twice,
and the copies have already drifted:

- Fit-to-route after adding a GPX file had to be written in both iOS screens.
- Android's Go map lets you tap a dot to follow that runner; iOS's Go map can't.
- iOS's Go map has GPX routes; Android's Go map doesn't.

## Today

| | iOS | Android |
|---|---|---|
| Go map | `CasualMapScreen` in `CasualViews.swift` | `ui/map/CasualMapScreen.kt` |
| Session map | `SessionMapScreen.swift` | `ui/map/LiveMapScreen.kt` |
| Already shared | `LinzMapView` (`LinzMap.swift`), `RunnerMapPin`, `MapChrome.swift` (timer pill, rail button, `HoldToStopPill`, `BatteryPausedCard`) | `MapScreen.kt` (the map, camera, fit/follow), `RunnerPins.kt`, `RailButton`, `HoldToStopPill`, `BatteryPausedCard`, `ExtendSessionSheet` |
| Copied in both screens | pins, per-second timeline, `fitLocked` / `fitAll` / `frameEveryone`, route picker + `frameRoute`, extend sheet, bottom bar with Stop | fit/follow state (`fitAll`, `followedRunner`, `fitAllTick`), rail layout, bottom bar with Stop, extend sheet, polling |

Android already has the shared map; iOS shares only the pieces, so most of the work is on iOS.

## What genuinely differs

| | Go | Named session |
|---|---|---|
| Dots | crew, plus anonymous grey nearby dots and the session padlock | session members |
| Fit everyone | includes the grey dots | runners only |
| Data | polls `GET /casual/live` | `GET /locations/{id}` |
| Routes | everyone's routes (`casual.routeLines`) | the session's route |
| Rail | Share a watch link, Who can see me, Route, Settings | Share invite, Open web map (Android), Invite code (Android), Route (iOS), nearby organiser controls |
| Bottom left | "2 crew out" chip, opens the crew sheet | participants row, tap to follow, hold to block |
| Stop | `casual.stop()` | `location.stop()` / `onStop` |

None of this needs a separate screen: it is what goes in the dots, the rail and the bottom-left slot.

## Target

One screen shell, fed by one of two sources.

**The shell owns:** the map, the per-second tick, fit-everyone with its lock, following a runner,
framing a just-added route, the timer pill and extend sheet, the rail's shared buttons (Fit all,
Route), the bottom bar's Stop pill or battery card, and the route file picker.

**A source supplies:** the pins and grey dots, the route lines, the join range, how to add or remove a
route, what Stop does, and its own rail buttons, bottom-left view and sheets.

iOS sketch:

```swift
struct TrackingMapScreen<Rail: View, BottomLeading: View>: View {
    var location: LocationManager
    var pins: [MapPin]               // one pin type for both sources
    var nearby: [MapPin]             // grey dots; empty for a session
    var routes: [[CLLocationCoordinate2D]]
    var joinRange: LinzJoinRange?
    var route: RouteActions?         // add / remove; nil hides the Route button
    var onStop: () -> Void
    @ViewBuilder var rail: () -> Rail
    @ViewBuilder var bottomLeading: () -> BottomLeading
}
```

`CasualMapScreen` and `SessionMapScreen` stay as thin wrappers: they build the inputs and attach
their own sheets. Android follows the same split: a `TrackingMapScaffold` around the existing
`MapScreen`, with `CasualMapScreen.kt` and `LiveMapScreen.kt` reduced to feeds.

## Steps

### iOS (`one-map-screen-ios`)

- [ ] One `MapPin` type (id, display name, coordinate, heading, self, locked, timestamp) and one
      `markers(at:)` that draws runners and grey dots, replacing the two private `Pin` structs.
- [ ] `TrackingMapScreen` with the shell's parts, moved from `SessionMapScreen` (it has the superset:
      follow, frame route, fit lock).
- [ ] Move `SessionMapScreen` onto it. Participants row, block, nearby organiser controls and share
      consent stay in the wrapper.
- [ ] Move `CasualMapScreen` onto it. Count chip, Who can see me, Share and Settings stay in the wrapper;
      polling `casual.loadLive()` stays in the wrapper's `.task`.
- [ ] Go gains follow-a-runner from the shell (tap a dot), matching Android.
- [ ] Delete the copied code from both screens.

### Android (`one-map-screen-android`)

- [ ] `TrackingMapScaffold` around `MapScreen`: fit/follow state, timer pill, extend sheet, rail with
      Fit all and slots, bottom bar with `HoldToStopPill` or `BatteryPausedCard`.
- [ ] Move `LiveMapScreen` onto it; then `CasualMapScreen`.
- [ ] GPX routes for Go come for free once the shell has the Route button and a source supplies
      `RouteActions` (today "not started" in [android-app.md](android-app.md)); the casual route
      endpoints already exist.

### Both

- [ ] Device check on each platform, for both a Go run and a named session:
      dots and grey dots, fit all, follow and let go, drag stops auto-fit, add/replace/remove a route
      frames it, extend, Hold to stop, battery-paused card, the screen-specific sheets.

## Order

Unifying the position model first means both sources hand the shell the same position type, so
`MapPin` is built in one place. If this goes first instead, each wrapper maps its own positions into
`MapPin`, and the unify plan later deletes that mapping. Either order works; doing unify first is less
throwaway.

## Open questions

- Should the session map's rail match Go's order and icons on both platforms? Today Android's session
  rail has Open web map and Invite code, iOS's doesn't.
- Should fit-everyone on Go keep including the grey dots? (Kept for now: they're why you opened the map.)
