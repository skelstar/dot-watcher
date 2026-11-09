#!/usr/bin/env python3
"""
Ro-dots spike: can Valhalla build believable trail/path loops from OpenStreetMap?

Throwaway exploration, not production code. Builds triangle loops (start -> A -> B -> start) at several bearings
from a few Wellington starting points, routes them with Valhalla's pedestrian costing, then asks trace_attributes
how much of each loop is path/track/footway versus road/sidewalk. Writes GPX files and a Leaflet viewer (loops.html)
next to this script, and prints a summary table.

Uses the PUBLIC Valhalla demo server with a handful of requests, which is fine for a spike and NOT for the service
(see .ai/plans/ro-dot-army.md, Routes). Override with VALHALLA_URL to point at a self-hosted instance.

    python ro-dots/spike/valhalla_loops.py
"""
import json
import math
import os
import time
import urllib.request
from pathlib import Path

URL = os.environ.get("VALHALLA_URL", "https://valhalla1.openstreetmap.de").rstrip("/")
OUT = Path(__file__).parent / "out"
TARGET_KM = 6.0

STARTS = {
    "mt-victoria": (-41.2956, 174.7922),
    "tinakori-hill": (-41.2690, 174.7640),
    "zealandia-karori": (-41.2905, 174.7530),
}
BEARINGS = [0, 90, 180, 270]

# Which costing profile a persona would use (plan: Routes, Phase B).
PROFILES = {
    "trail": {"use_tracks": 1.0, "use_hills": 0.8, "walkway_factor": 0.8, "sidewalk_factor": 3.0, "alley_factor": 3.0,
              "use_ferry": 0.0, "step_penalty": 60},
    "jogger": {"use_tracks": 0.5, "use_hills": 0.3, "walkway_factor": 0.9, "sidewalk_factor": 1.0,
               "use_ferry": 0.0, "step_penalty": 90},
}

EARTH = 6371000.0


def destination(lat, lon, bearing_deg, dist_m):
    b = math.radians(bearing_deg)
    d = dist_m / EARTH
    la1, lo1 = math.radians(lat), math.radians(lon)
    la2 = math.asin(math.sin(la1) * math.cos(d) + math.cos(la1) * math.sin(d) * math.cos(b))
    lo2 = lo1 + math.atan2(math.sin(b) * math.sin(d) * math.cos(la1), math.cos(d) - math.sin(la1) * math.sin(la2))
    return math.degrees(la2), math.degrees(lo2)


def post(path, body):
    req = urllib.request.Request(URL + path, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def decode_polyline6(s):
    pts, i, lat, lon = [], 0, 0, 0
    while i < len(s):
        for which in (0, 1):
            shift = result = 0
            while True:
                b = ord(s[i]) - 63
                i += 1
                result |= (b & 0x1F) << shift
                shift += 5
                if b < 0x20:
                    break
            d = ~(result >> 1) if result & 1 else result >> 1
            if which == 0:
                lat += d
            else:
                lon += d
        pts.append((lat / 1e6, lon / 1e6))
    return pts


def loop(start, bearing, profile, r):
    # r is the triangle's side in metres; the caller resizes it between attempts.
    a = destination(*start, bearing, r)
    b = destination(*start, bearing + 60, r)
    locs = [{"lat": p[0], "lon": p[1]} for p in (start, a, b, start)]
    # "through" lets Valhalla pass the waypoint without a U-turn at it.
    for l in locs[1:3]:
        l["type"] = "through"
    route = post("/route", {"locations": locs, "costing": "pedestrian",
                            "costing_options": {"pedestrian": PROFILES[profile]}, "directions_type": "none"})
    leg_shapes = [decode_polyline6(leg["shape"]) for leg in route["trip"]["legs"]]
    shape = [p for leg in leg_shapes for p in leg]
    summary = route["trip"]["summary"]

    attrs = post("/trace_attributes", {
        "shape": [{"lat": p[0], "lon": p[1]} for p in shape[:: max(1, len(shape) // 800)]],
        "costing": "pedestrian", "shape_match": "map_snap",
        "filters": {"attributes": ["edge.use", "edge.length", "edge.surface"], "action": "include"}})
    by_use, by_surface = {}, {}
    for e in attrs.get("edges", []):
        by_use[e.get("use", "?")] = by_use.get(e.get("use", "?"), 0) + e.get("length", 0)
        by_surface[e.get("surface", "?")] = by_surface.get(e.get("surface", "?"), 0) + e.get("length", 0)
    return shape, summary, by_use, by_surface, route["trip"]["summary"]["length"]


def gpx(name, shape):
    pts = "".join(f'<trkpt lat="{la:.6f}" lon="{lo:.6f}"/>' for la, lo in shape)
    return (f'<?xml version="1.0"?><gpx version="1.1" creator="ro-dots-spike" xmlns="http://www.topografix.com/GPX/1/1">'
            f"<trk><name>{name}</name><trkseg>{pts}</trkseg></trk></gpx>")


def main():
    OUT.mkdir(exist_ok=True)
    rows, features = [], []
    for sname, start in STARTS.items():
        for profile in PROFILES:
            for bearing in BEARINGS:
                key = f"{sname}-{profile}-{bearing}"
                # Real routes are 1.5-2x longer than the straight triangle, so resize toward the target
                # (up to 3 tries, within +-20%), and reject anything that uses a ferry.
                r, best, err = TARGET_KM * 1000 / 3 / 1.6, None, None
                for _ in range(3):
                    try:
                        result = loop(start, bearing, profile, r)
                    except Exception as ex:  # a spike: note the failure and keep going
                        err = ex
                        break
                    time.sleep(1.2)  # be polite to the public server
                    km_now = result[1]["length"]
                    if "ferry" not in result[2]:
                        if best is None or abs(km_now - TARGET_KM) < abs(best[1]["length"] - TARGET_KM):
                            best = result
                    if abs(km_now - TARGET_KM) / TARGET_KM <= 0.2 and "ferry" not in result[2]:
                        break
                    r *= max(0.4, min(1.5, TARGET_KM / max(km_now, 0.5)))
                if best is None:
                    rows.append((key, None, None, None, f"REJECTED ({'ERROR ' + str(err) if err else 'ferry / no valid loop'})"))
                    continue
                shape, summary, by_use, by_surface, _ = best
                km = summary["length"]
                total = sum(by_use.values()) or 1
                offroad = sum(v for k, v in by_use.items() if k in ("path", "footway", "track", "steps", "bridleway", "cycleway", "mountain_bike"))
                road = sum(v for k, v in by_use.items() if k in ("road", "driveway", "alley", "sidewalk", "living_street", "service_road", "turn_channel", "ramp"))
                (OUT / f"{key}.gpx").write_text(gpx(key, shape), encoding="utf-8")
                features.append({"type": "Feature", "properties": {"name": key, "profile": profile},
                                 "geometry": {"type": "LineString", "coordinates": [[lo, la] for la, lo in shape]}})
                top = ", ".join(f"{k} {v / total:.0%}" for k, v in sorted(by_use.items(), key=lambda kv: -kv[1])[:4])
                rows.append((key, km, offroad / total, road / total, top))
    print(f"{'loop':36} {'km':>5} {'path%':>6} {'road%':>6}  breakdown")
    for key, km, off, road, top in rows:
        if km is None:
            print(f"{key:36} {top}")
        else:
            print(f"{key:36} {km:5.1f} {off:6.0%} {road:6.0%}  {top}")
    (OUT / "loops.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": features}), encoding="utf-8")
    html = """<!doctype html><meta charset=utf-8><title>Ro-dot loops</title>
<link rel=stylesheet href=https://unpkg.com/leaflet@1.9.4/dist/leaflet.css><style>html,body,#m{height:100%;margin:0}</style>
<div id=m></div><script src=https://unpkg.com/leaflet@1.9.4/dist/leaflet.js></script><script>
const map=L.map('m').setView([-41.285,174.77],12);
L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png',{maxZoom:17,attribution:'OpenTopoMap, OSM'}).addTo(map);
fetch('loops.geojson').then(r=>r.json()).then(g=>L.geoJSON(g,{style:f=>({color:f.properties.profile=='trail'?'#d33':'#26c',weight:3}),
 onEachFeature:(f,l)=>l.bindTooltip(f.properties.name)}).addTo(map));</script>"""
    (OUT / "loops.html").write_text(html, encoding="utf-8")


if __name__ == "__main__":
    main()
