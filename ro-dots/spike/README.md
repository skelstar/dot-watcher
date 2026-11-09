# Valhalla loop spike

Throwaway exploration for [`.ai/plans/ro-dot-army.md`](../../.ai/plans/ro-dot-army.md) (Routes, Phase B). Not part of
the service.

```
python ro-dots/spike/valhalla_loops.py        # writes ro-dots/spike/out/*.gpx, loops.geojson, loops.html
```

It builds triangle loops (start → A → B → start) at four bearings from three Wellington starts (Mt Victoria, Tinakori
Hill, Zealandia/Karori), for a "trail" and a "jogger" costing profile, resizes each toward 6 km (three tries, ±20%),
rejects ferries, and reports how much of each loop is path/track/footway versus road. It uses the **public** Valhalla
demo server (a few dozen polite requests); the service must self-host. Open `out/loops.html` (needs the files in
`out/`, served from this folder, e.g. `python -m http.server`) to see the loops on a topo map.

## Findings (run 2026-11-10)

- **Valhalla works for trails.** Its pedestrian profile uses `path`, `track`, `footway`, `steps` and `cycleway`
  edges. Good loops exist: e.g. Zealandia jogger/180 is 5.5 km and 91% off-road (59% path, 19% track), Tinakori trail/90
  is 6.3 km with 28% path and 21% track.
- **Random-bearing triangles are a weak generator.** About a third of the loops were over half residential road, and
  the profile weights (`use_tracks`, `walkway_factor`, `sidewalk_factor`) shift the mix but don't fix it.
- **Length needs a feedback loop.** The first pass (no resizing) came out at 8-40 km against a 6 km target. Resizing
  fixed it: 22 of 24 loops landed within about 0.5-1.6 km of the target.
- **Waypoints in water fail or detour.** 5 of 24 starts could not be routed (HTTP 400, no route) because a waypoint
  landed in the harbour, and unguarded loops used a ferry. A real generator must reject water/unroutable waypoints
  and any loop with a ferry edge.
- **"Road" is mostly residential streets with no mapped footway**, so it is the right thing to avoid but is not
  always avoidable in suburbs.

## What this means for the plan

1. Generate **many candidates and score them** (length error, share of path/track, no ferry, no big overlap or
   out-and-back) instead of trusting one try.
2. Prefer **waypoints taken from trail nodes** (OSM `highway=path|track` near the start, via Overpass or the
   self-hosted extract) over blind bearings.
3. **Self-host Valhalla** on a NZ extract: no rate limits, and we can tune costing per persona and pin an OSM date so
   routes are stable.
4. **No Strava heatmap.** It needs a logged-in session, its API terms forbid this use, and it is built from real
   people's tracks (plan decision 8). The open equivalent is OSM public GPS traces (CC BY-SA), which are useful as a
   check on which paths people really use but are optional.
