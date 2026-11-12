# Valhalla loop spike

Throwaway exploration for [`.ai/plans/bot-dot-army.md`](../../.ai/plans/bot-dot-army.md) (Routes, Phase B). Not part of
the service.

```
python bot-dots/spike/valhalla_loops.py        # writes bot-dots/spike/out/*.gpx, loops.geojson, loops.html
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

---

# Spike 2: wandering instead of loops

```
python bot-dots/spike/valhalla_wander.py       # writes out/wander.geojson, wander.html, *.gpx
```

A bot-dot starts 500-3000 m from a fixed "runner" and wanders by hops for 40 simulated minutes at ~5:23 /km. Each hop
picks a target 400-1200 m away within ±100° of its current heading, routes it with **plain pedestrian costing (no
trail preference)**, and is rejected if the route strays outside the 500-3000 m band (±), is more than 2.5× the
straight line, or retraces ground it has already covered (over 40% overlap). Nine walks (3 starts × 3 seeds), run
2026-11-10:

| Result | Value |
| --- | --- |
| Distance in 40 min | 6.3-7.0 km (as designed by pace) |
| Hops per walk | 4-7 |
| Stayed in the 500-3000 m band | all nine (closest 669 m, furthest 3005 m) |
| Retraced ground (forced) | 0 times in nine walks |
| Revisited ground | 1-18% of samples (median ~6%) |
| Hop rejections | 0-8 per walk (each a wasted request, mostly "target outside the band" or "no route") |
| Path/track/footway share | 38-80%, with no preference for trails |
| **Reversals at a hop junction** | **1-3 per walk, a defect** (see below) |

**Verdict: wandering works and is simpler than loops.** No length targeting, no water problem (a target that can't be
routed is simply rejected), and the band keeps the bot-dot visible but never on top of the runner.

**To fix:**
- **U-turns at hop boundaries.** A new hop is routed from where the last one ended, so it often starts by heading
  back. Valhalla accepts a `heading` (and `heading_tolerance`) on the start location; pass the previous bearing.
  Check this first.
- **Rejected hops cost requests.** Self-hosting removes the rate limit; picking targets from the road/path graph
  rather than free space cuts the waste further.
- **Pace**: a constant pace with ±10% per hop is too regular; the movement engine needs the slow random walk and the
  stops from the plan.

**Later:** a bot-dot given a GPX trail simply replaces the hop targets with that trail's points, the rest is the same.
