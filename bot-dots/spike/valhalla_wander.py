#!/usr/bin/env python3
"""
Bot-dots spike 2: a "wandering" bot-dot instead of a fixed loop.

Throwaway exploration for .ai/plans/bot-dot-army.md. A bot-dot starts at a random point between MIN_M and MAX_M from a
fixed "runner" and then wanders by hops: pick a nearby target roughly ahead of it, route there with Valhalla's plain
pedestrian costing (no preference for trails), walk it at a running pace, and repeat for 40 simulated minutes. It
must stay inside the MIN_M..MAX_M band around the runner and avoid retracing itself. Reports, per walk: distance,
hops, how often it had to reject a hop or retrace, how close and far it got from the runner, reversals, and how much
of the ground was revisited. Writes GPX/GeoJSON and a Leaflet viewer to out/.

Uses the PUBLIC Valhalla demo server with a few dozen polite requests; the service must self-host. Override with
VALHALLA_URL.

    python bot-dots/spike/valhalla_wander.py
"""
import json
import math
import os
import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from valhalla_loops import STARTS, decode_polyline6, destination, gpx, post  # noqa: E402

OUT = Path(__file__).parent / "out"
MIN_M, MAX_M = 500.0, 3000.0     # stay in this band around the runner
HOP_MIN_M, HOP_MAX_M = 400.0, 1200.0
SIM_SECONDS, SAMPLE_S = 40 * 60, 15
PACE_MS = 3.1                    # ~5:23 /km
CELL_DEG = 0.0002                # ~22 m grid for "have I been here"
SEEDS = [1, 2, 3]
EARTH = 6371000.0


def dist(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * EARTH * math.asin(math.sqrt(h))


def bearing(a, b):
    la1, la2, dlo = math.radians(a[0]), math.radians(b[0]), math.radians(b[1] - a[1])
    y = math.sin(dlo) * math.cos(la2)
    x = math.cos(la1) * math.sin(la2) - math.sin(la1) * math.cos(la2) * math.cos(dlo)
    return (math.degrees(math.atan2(y, x)) + 360) % 360


def angle_diff(a, b):
    d = abs(a - b) % 360
    return 360 - d if d > 180 else d


def cell(p):
    return (round(p[0] / CELL_DEG), round(p[1] / CELL_DEG))


def route(a, b):
    r = post("/route", {"locations": [{"lat": a[0], "lon": a[1]}, {"lat": b[0], "lon": b[1]}],
                        "costing": "pedestrian", "directions_type": "none"})
    return decode_polyline6(r["trip"]["legs"][0]["shape"]), r["trip"]["summary"]["length"] * 1000


def head_of(shape, start=True):
    seg = shape[:6] if start else shape[-6:]
    return bearing(seg[0], seg[-1]) if len(seg) > 1 else 0.0


def walk_along(shape, speed, carry):
    """Positions every SAMPLE_S seconds along the shape; carry is the leftover metres into the next hop."""
    out, step, acc = [], speed * SAMPLE_S, carry
    for a, b in zip(shape, shape[1:]):
        seg = dist(a, b)
        while acc <= seg and seg > 0:
            f = acc / seg
            out.append((a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f))
            acc += step
        acc -= seg
    return out, acc


def one_walk(runner, rng):
    # Start somewhere in the band, on routable ground (retry: sea and private land fail to route).
    for _ in range(8):
        d = rng.uniform(MIN_M, MAX_M)
        start = destination(*runner, rng.uniform(0, 360), d)
        heading = rng.uniform(0, 360)
        path, visited, stats = [start], set(), {"hops": 0, "rejected": 0, "retraces": 0, "reversals": 0}
        pos, carry, t, prev_end = start, 0.0, 0.0, None
        samples = []
        failures_in_row = 0
        while t < SIM_SECONDS:
            best = None
            for _try in range(10):
                target = destination(*pos, (heading + rng.uniform(-100, 100)) % 360, rng.uniform(HOP_MIN_M, HOP_MAX_M))
                if not MIN_M <= dist(runner, target) <= MAX_M:
                    stats["rejected"] += 1
                    continue
                try:
                    shape, length = route(pos, target)
                except Exception:
                    stats["rejected"] += 1
                    time.sleep(0.6)
                    continue
                time.sleep(1.1)
                if length > 2.5 * dist(pos, target) + 200 or len(shape) < 2:
                    stats["rejected"] += 1
                    continue
                dists = [dist(runner, p) for p in shape]
                if min(dists) < MIN_M * 0.8 or max(dists) > MAX_M * 1.15:
                    stats["rejected"] += 1
                    continue
                overlap = sum(cell(p) in visited for p in shape[3:]) / max(1, len(shape) - 3)
                cand = (overlap, shape, length)
                if best is None or overlap < best[0]:
                    best = cand
                if overlap <= 0.4:
                    break
            if best is None:
                failures_in_row += 1
                heading = (heading + 180) % 360  # boxed in: turn around
                if failures_in_row > 3:
                    break
                continue
            failures_in_row = 0
            overlap, shape, length = best
            if overlap > 0.4:
                stats["retraces"] += 1
            if prev_end is not None and angle_diff(prev_end, head_of(shape, True)) > 135:
                stats["reversals"] += 1
            speed = PACE_MS * rng.uniform(0.9, 1.1)
            pts, carry = walk_along(shape, speed, carry)
            for p in pts:
                samples.append(p)
                visited.add(cell(p))
                t += SAMPLE_S
                if t >= SIM_SECONDS:
                    break
            path.extend(shape)
            stats["hops"] += 1
            prev_end = head_of(shape, False)
            heading = prev_end
            pos = shape[-1]
        if stats["hops"] >= 3:
            return path, samples, stats
    return None, None, None


def main():
    OUT.mkdir(exist_ok=True)
    feats, rows = [], []
    for sname, runner in STARTS.items():
        for seed in SEEDS:
            rng = random.Random(seed)
            key = f"{sname}-wander-{seed}"
            path, samples, st = one_walk(runner, rng)
            if path is None:
                rows.append((key, "FAILED: no routable start / boxed in"))
                continue
            dists = [dist(runner, p) for p in samples]
            cells = [cell(p) for p in samples]
            revisit = 1 - len(set(cells)) / max(1, len(cells))
            km = sum(dist(a, b) for a, b in zip(samples, samples[1:])) / 1000
            # path vs road, over the whole walk
            shp = path[:: max(1, len(path) // 700)]
            by_use = {}
            try:
                attrs = post("/trace_attributes", {"shape": [{"lat": p[0], "lon": p[1]} for p in shp],
                                                   "costing": "pedestrian", "shape_match": "map_snap",
                                                   "filters": {"attributes": ["edge.use", "edge.length"], "action": "include"}})
                for e in attrs.get("edges", []):
                    by_use[e.get("use", "?")] = by_use.get(e.get("use", "?"), 0) + e.get("length", 0)
                time.sleep(1.1)
            except Exception:
                pass
            total = sum(by_use.values()) or 1
            trailish = sum(v for k, v in by_use.items() if k in ("path", "footway", "track", "steps", "bridleway", "cycleway"))
            rows.append((key, f"{km:5.1f} km  hops {st['hops']:2}  rej {st['rejected']:2}  retrace {st['retraces']}  "
                              f"reversals {st['reversals']}  revisit {revisit:4.0%}  "
                              f"dist from runner {min(dists):4.0f}-{max(dists):4.0f} m  path/track/footway {trailish / total:3.0%}"))
            (OUT / f"{key}.gpx").write_text(gpx(key, samples), encoding="utf-8")
            feats.append({"type": "Feature", "properties": {"name": key},
                          "geometry": {"type": "LineString", "coordinates": [[lo, la] for la, lo in samples]}})
        feats.append({"type": "Feature", "properties": {"name": f"runner {sname}", "runner": True, "min": MIN_M, "max": MAX_M},
                      "geometry": {"type": "Point", "coordinates": [runner[1], runner[0]]}})
    for key, line in rows:
        print(f"{key:30} {line}")
    (OUT / "wander.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": feats}), encoding="utf-8")
    html = """<!doctype html><meta charset=utf-8><title>Bot-dot wander</title>
<link rel=stylesheet href=https://unpkg.com/leaflet@1.9.4/dist/leaflet.css><style>html,body,#m{height:100%;margin:0}</style>
<div id=m></div><script src=https://unpkg.com/leaflet@1.9.4/dist/leaflet.js></script><script>
const map=L.map('m').setView([-41.285,174.77],12);
L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:18,attribution:'OSM'}).addTo(map);
fetch('wander.geojson').then(r=>r.json()).then(g=>{
 L.geoJSON(g,{filter:f=>!f.properties.runner,style:()=>({color:'#d33',weight:3}),onEachFeature:(f,l)=>l.bindTooltip(f.properties.name)}).addTo(map);
 g.features.filter(f=>f.properties.runner).forEach(f=>{const c=[f.geometry.coordinates[1],f.geometry.coordinates[0]];
  L.circleMarker(c,{radius:6,color:'#000'}).addTo(map).bindTooltip(f.properties.name);
  L.circle(c,{radius:f.properties.min,color:'#888',fill:false,dashArray:'4'}).addTo(map);
  L.circle(c,{radius:f.properties.max,color:'#888',fill:false,dashArray:'4'}).addTo(map);});});</script>"""
    (OUT / "wander.html").write_text(html, encoding="utf-8")


if __name__ == "__main__":
    main()
