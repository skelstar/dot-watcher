# Plan: Mapbox terrain tiles on the iOS map

## Goal

Swap the iOS app's map imagery for Mapbox's `outdoors` style (contour lines, trail/terrain
shading) — visuals only. Not adopting the Mapbox SDK, not touching pins/camera/follow logic
conceptually, not changing the web client (it already uses Mapbox via `mapbox-gl`).

## Key finding — this changes the effort estimate

`NativeMapView.swift` currently uses SwiftUI's native `Map(position:)` API (iOS 17+), not
`MKMapView` directly. **SwiftUI's `Map` does not support custom tile overlays
(`MKTileOverlay`)** — that's a UIKit-only MapKit feature that was never ported to the new
SwiftUI API, and `MapProxy` (from `MapReader`) doesn't expose the underlying `MKMapView` either.

Confirmed via: [MKTileOverlay docs](https://developer.apple.com/documentation/mapkit/mktileoverlay),
[SwiftUI MapKit missing features](https://medium.com/@gerdcastan/swiftui-mapkit-ios-17-the-missing-features-4b08fa42ee9f).

So "just add an overlay" isn't a small bolt-on here — it requires dropping `NativeMapView` back
to a `UIViewRepresentable`-wrapped `MKMapView`. That's a real rewrite, not a tweak, so the gap
between "cheap tile overlay" and "adopt the real Mapbox SDK" is smaller than it first looked.
Worth re-confirming this is still the wanted tradeoff before starting (see Alternatives below).

## Recommended approach: `UIViewRepresentable` + `MKMapView` + `MKTileOverlay`

Keeps everything else (annotations, camera math, follow logic) conceptually the same, just moves
it from declarative SwiftUI `Map` to an imperative `MKMapView` driven by a `Coordinator`.

1. **Token plumbing** — add `MapboxAccessToken` to `Info.plist` as `$(MAPBOX_ACCESS_TOKEN)`,
   matching the existing `DotWatcherAPIBaseURL`-style injection from build settings/xcconfig.
   Reuse the same Mapbox account/token the web client uses (`VITE_MAPBOX_TOKEN`), just needs to
   be a token valid for mobile use (no domain-restriction that's web-only).

2. **Replace `NativeMapView`'s map surface**:
   - New `UIViewRepresentable` wrapping `MKMapView`, `makeUIView` creates the map and adds
     `MKTileOverlay(urlTemplate: "https://api.mapbox.com/styles/v1/mapbox/outdoors-v12/tiles/256/{z}/{x}/{y}@2x?access_token=...")`
     via `mapView.addOverlay(_:level: .aboveLabels)`.
   - `Coordinator: NSObject, MKMapViewDelegate` implements
     `mapView(_:rendererFor:) -> MKOverlayRenderer` returning `MKTileOverlayRenderer` for the
     tile overlay.
   - Decide tile size/retina (`@2x`) and whether to set `canReplaceMapContent = true` so the
     overlay fully replaces Apple's base imagery rather than sitting on top of it.

3. **Port annotation rendering** — `Annotation("", coordinate:) { RunnerMapPin(...) }` (SwiftUI
   content) becomes an `MKAnnotation` + `MKAnnotationView` pair. `RunnerMapPin.swift` is a plain
   SwiftUI `View` ([RunnerMapPin.swift:14](ios/DotWatcher/DotWatcher/RunnerMapPin.swift#L14)), so
   it can likely be reused almost unchanged via
   `annotationView.contentConfiguration = UIHostingConfiguration { RunnerMapPin(...) }`
   (iOS 16+) instead of a full custom `MKAnnotationView` subclass — worth confirming this reuse
   works smoothly before assuming it's free.

4. **Port camera logic** — `fitCamera()`, `followCameraIfNeeded()`, and the `visibleRegion(...)`
   sheet-clipping math (lines 122–179 today) are already `MKCoordinateRegion`/`MKCoordinateSpan`
   based, so the math itself doesn't change — it just moves from
   `@State cameraPosition` + `withAnimation` to `mapView.setRegion(_:animated:)` calls inside the
   `Coordinator`/representable.

5. **Port the per-second re-center timer** — `TimelineView(.periodic(from:.now, by: 1))` becomes
   a `Timer`/`DispatchSourceTimer` owned by the `Coordinator`, driving the same
   `followCameraIfNeeded()` logic.

6. **Attribution** — Mapbox's ToS requires visible attribution + logo when displaying their
   tiles. Add a small attribution overlay/label to the map view (a fixed corner badge is
   standard) — easy to forget, cheap to add if planned for up front.

7. **Verify on-device**: pins render/rotate correctly, follow/fit-all still animate, drag-sheet
   visible-fraction math still frames pins correctly, tile loading/caching behaves reasonably
   offline-ish (trail use is often patchy signal — check `MKTileOverlay`'s built-in disk caching
   is enough, no extra work planned for this unless testing shows it's not).

## Alternatives considered

- **Full Mapbox Maps SDK** (own annotation/camera/viewport model, vector styles, 3D). Rejected
  as the default choice — bigger dependency (SPM package, binary size), a second full API to
  learn, and steers away from Apple-native camera/annotation code the app already has. Given the
  finding above (the "cheap" route is already a real rewrite), this is worth a second look if
  during implementation the `MKMapView` port turns out uglier than expected — but start with the
  `MKTileOverlay` route since it changes strictly less (imagery only, not interaction model).

## Open questions to resolve before starting

- Confirm the Mapbox token used by the web client permits mobile/app usage, or get a
  separate one scoped for the iOS bundle ID.
- Pick the exact style: `outdoors-v12` vs `outdoors-v11` vs a custom style tuned for trail
  running specifically — worth a quick visual comparison first.
- Attribution placement — where does a small Mapbox logo/link fit without covering the
  `RunnerLegendRow` or `FitAllButton`.
- Whether `MKTileOverlay`'s default caching is sufficient for typical trail-run connectivity, or
  whether pre-fetching/caching the session's route bounding box ahead of time is worth adding
  later (out of scope for this plan, just flagging it as a likely follow-up ask).

## Rough effort

Moderate, not small — mainly because of the `Map` → `MKMapView` conversion, not the tile overlay
itself. Roughly: 1 session to get the `UIViewRepresentable` + tile overlay + attribution working,
1 session to port annotations/camera/follow behavior over without regressing existing behavior,
plus on-device verification time (this can't be meaningfully checked in Simulator alone — tile
imagery differences matter, but more importantly camera/follow behavior should be checked on a
real device with real GPS movement, same as any other location-feature change here).
