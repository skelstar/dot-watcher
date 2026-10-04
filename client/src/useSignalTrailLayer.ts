import { useEffect, useMemo, useState, type RefObject } from 'react'
// maplibre-gl has no default export (unlike mapbox-gl) — named imports only.
import { MapLibreMap, type GeoJSONSource } from 'maplibre-gl'
import type { RunnerPosition } from './types.ts'
import { buildTrackGraph, pathAlongTracks, pointAlong, polylineMetres, type LngLat, type TrackGraph } from './trackSnapping.ts'

const SOURCE_ID = 'signal-trail'
const LAYER_ID = 'signal-trail-dots'
const SAT_LAYER_ID = 'signal-trail-satellite'
const SAT_IMAGE_ID = 'signal-trail-satellite-icon'
const MISSED_LAYER_ID = 'signal-trail-missed'
const MISSED_IMAGE_ID = 'signal-trail-missed-icon'

// Satellite reads are expected every 90s. Two consecutive satellite reads further apart than that
// have (gap / 90s) - 1 slots in between with no read; this much slack stops a read that merely
// landed a bit late from counting as a miss.
const SATELLITE_INTERVAL_MS = 90_000
const MISS_TOLERANCE_MS = 15_000
// A track route between the two reads is only believed if the runner could plausibly have covered
// it in the time between them; otherwise it's probably the wrong trail and we fall back to a line.
const MAX_PLAUSIBLE_SPEED_MPS = 6
// The LINZ topo style's vector source and the layer holding its dotted tracks (foot, cycle,
// vehicle). The aerial style has neither, in which case querying fails and we use straight lines.
const BASEMAP_SOURCE_ID = 'LINZ Basemaps'

// Routing a gap is the costly part and the same gaps recur on every playhead tick, so each graph
// remembers its routes (keyed by the gap's two timestamps and runner).
const routeCache = new WeakMap<TrackGraph, Map<string, LngLat[] | null>>()

function cachedRoute(tracks: TrackGraph, key: string, from: LngLat, to: LngLat, gapMs: number): LngLat[] | null {
  let cache = routeCache.get(tracks)
  if (!cache) routeCache.set(tracks, (cache = new Map()))
  if (cache.has(key)) return cache.get(key)!
  let path = pathAlongTracks(tracks, from, to)
  if (path && polylineMetres(path) > MAX_PLAUSIBLE_SPEED_MPS * (gapMs / 1000)) path = null
  cache.set(key, path)
  return path
}

export interface MissedRead {
  latitude: number
  longitude: number
  timestamp: string
}

// Where satellite reads probably failed: for each runner, wherever a satellite read is followed by
// a gap longer than the satellite interval, the missing slots are placed evenly in time along the
// basemap's dotted track between the two recorded positions either side of the gap when one
// connects them, else along the straight line between them.
export function findMissedSatelliteReads(positions: RunnerPosition[], tracks: TrackGraph | null = null): MissedRead[] {
  const byRunner = new Map<string, RunnerPosition[]>()
  for (const p of positions) {
    const list = byRunner.get(p.runnerName)
    if (list) list.push(p)
    else byRunner.set(p.runnerName, [p])
  }
  const missed: MissedRead[] = []
  for (const list of byRunner.values()) {
    list.sort((a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp))
    for (let i = 1; i < list.length; i++) {
      const a = list[i - 1]
      const b = list[i]
      if (!a.isUltraConstrained) continue
      const aMs = Date.parse(a.timestamp)
      const gapMs = Date.parse(b.timestamp) - aMs
      const misses = Math.floor((gapMs + MISS_TOLERANCE_MS) / SATELLITE_INTERVAL_MS) - 1
      if (misses < 1) continue
      const from: LngLat = [a.longitude, a.latitude]
      const to: LngLat = [b.longitude, b.latitude]
      const path = tracks ? cachedRoute(tracks, `${a.runnerName}|${a.timestamp}|${b.timestamp}`, from, to, gapMs) : null
      const route = path ?? [from, to]
      for (let k = 1; k <= misses; k++) {
        const f = k / (misses + 1)
        const [longitude, latitude] = pointAlong(route, f)
        missed.push({
          latitude,
          longitude,
          timestamp: new Date(aMs + gapMs * f).toISOString(),
        })
      }
    }
  }
  return missed
}

// The dotted tracks currently loaded in the basemap's vector tiles, as a routable graph. Only
// covers tiles the map has loaded, so it grows as the viewer pans/zooms (see 'moveend' below).
function loadTracks(map: MapLibreMap | null): TrackGraph | null {
  if (!map) return null
  try {
    const lines: LngLat[][] = []
    for (const f of map.querySourceFeatures(BASEMAP_SOURCE_ID, {
      sourceLayer: 'streets',
      filter: ['==', ['get', 'kind'], 'track'],
    })) {
      if (f.geometry.type === 'LineString') lines.push(f.geometry.coordinates as LngLat[])
      else if (f.geometry.type === 'MultiLineString') lines.push(...(f.geometry.coordinates as LngLat[][]))
    }
    return lines.length > 0 ? buildTrackGraph(lines) : null
  } catch {
    return null // source absent (e.g. aerial style) or not loaded yet
  }
}

// A white X in a red circle, same 16px footprint as the satellite badge.
function makeMissedImage(): { image: { width: number; height: number; data: Uint8Array }; pixelRatio: number } {
  const ratio = 2
  const css = 20
  const canvas = document.createElement('canvas')
  canvas.width = css * ratio
  canvas.height = css * ratio
  const ctx = canvas.getContext('2d')!
  ctx.scale(ratio, ratio)
  const c = css / 2
  ctx.shadowColor = 'rgba(0,0,0,0.4)'
  ctx.shadowBlur = 2
  ctx.shadowOffsetY = 1
  ctx.beginPath()
  ctx.arc(c, c, 8, 0, Math.PI * 2)
  ctx.fillStyle = '#ffffff'
  ctx.fill()
  ctx.shadowColor = 'transparent'
  ctx.beginPath()
  ctx.arc(c, c, 6.5, 0, Math.PI * 2)
  ctx.fillStyle = '#dc2626'
  ctx.fill()
  ctx.strokeStyle = '#ffffff'
  ctx.lineWidth = 1.8
  ctx.lineCap = 'round'
  ctx.beginPath()
  ctx.moveTo(c - 2.8, c - 2.8)
  ctx.lineTo(c + 2.8, c + 2.8)
  ctx.moveTo(c + 2.8, c - 2.8)
  ctx.lineTo(c - 2.8, c + 2.8)
  ctx.stroke()
  const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height)
  return { image: { width: canvas.width, height: canvas.height, data: new Uint8Array(data.buffer) }, pixelRatio: ratio }
}

// The runner dot's satellite badge (Arrow.tsx SatelliteBadge: 16px #ffe88e circle, 1.5px white
// border, drop shadow, black SatelliteDishIcon glyph at size-5), drawn to a canvas since a map
// layer can't render DOM/SVG. Drawn at 2x and registered with pixelRatio 2 to stay crisp.
function makeSatelliteImage(): { image: { width: number; height: number; data: Uint8Array }; pixelRatio: number } {
  const ratio = 2
  const css = 20 // 16px badge plus room for the shadow
  const canvas = document.createElement('canvas')
  canvas.width = css * ratio
  canvas.height = css * ratio
  const ctx = canvas.getContext('2d')!
  ctx.scale(ratio, ratio)
  const c = css / 2
  ctx.shadowColor = 'rgba(0,0,0,0.4)'
  ctx.shadowBlur = 2
  ctx.shadowOffsetY = 1
  ctx.beginPath()
  ctx.arc(c, c, 8, 0, Math.PI * 2)
  ctx.fillStyle = '#ffffff'
  ctx.fill()
  ctx.shadowColor = 'transparent'
  ctx.beginPath()
  ctx.arc(c, c, 6.5, 0, Math.PI * 2) // 8px radius minus the 1.5px border
  ctx.fillStyle = '#ffe88e'
  ctx.fill()
  // Glyph: 11px (size - 5) square, 24-unit viewBox, stroke 2 units.
  const glyph = 11
  ctx.save()
  ctx.translate(c - glyph / 2, c - glyph / 2)
  ctx.scale(glyph / 24, glyph / 24)
  ctx.strokeStyle = '#000000'
  ctx.lineWidth = 2
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  for (const d of [
    'M4 10a7.31 7.31 0 0 0 10 10Z',
    'm9 15 3-3',
    'M17 13a6 6 0 0 0-6-6',
    'M21 13A10 10 0 0 0 11 3',
  ]) ctx.stroke(new Path2D(d))
  ctx.restore()
  const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height)
  return { image: { width: canvas.width, height: canvas.height, data: new Uint8Array(data.buffer) }, pixelRatio: ratio }
}

// Plots every given position as a dot coloured by its self-reported isUltraConstrained flag, so
// you can see after a run where the phone was on cellular vs satellite, plus a red X where a
// satellite read probably failed (see findMissedSatelliteReads). Pass null to hide it. Doesn't move the
// viewport — the runner markers already follow/fit, and this re-runs on every playhead tick.
export function useSignalTrailLayer(
  mapRef: RefObject<MapLibreMap | null>,
  positions: RunnerPosition[] | null,
) {
  // Bumped whenever the map's loaded tiles (and so its track geometry) may have changed.
  const [tracksVersion, setTracksVersion] = useState(0)
  const missed = useMemo(
    () => findMissedSatelliteReads(positions ?? [], loadTracks(mapRef.current)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [positions, tracksVersion],
  )

  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    function apply() {
      const data: GeoJSON.FeatureCollection<GeoJSON.Point> = {
        type: 'FeatureCollection',
        features: [
          ...(positions ?? []).map((p): GeoJSON.Feature<GeoJSON.Point> => ({
            type: 'Feature',
            properties: { kind: p.isUltraConstrained === true ? 'satellite' : 'normal', timestamp: p.timestamp },
            geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] },
          })),
          ...missed.map((m): GeoJSON.Feature<GeoJSON.Point> => ({
            type: 'Feature',
            properties: { kind: 'missed', timestamp: m.timestamp },
            geometry: { type: 'Point', coordinates: [m.longitude, m.latitude] },
          })),
        ],
      }

      const source = map!.getSource(SOURCE_ID) as GeoJSONSource | undefined
      if (source) {
        source.setData(data)
        return
      }
      if (!map!.hasImage(SAT_IMAGE_ID)) {
        const { image, pixelRatio } = makeSatelliteImage()
        map!.addImage(SAT_IMAGE_ID, image, { pixelRatio })
      }
      if (!map!.hasImage(MISSED_IMAGE_ID)) {
        const { image, pixelRatio } = makeMissedImage()
        map!.addImage(MISSED_IMAGE_ID, image, { pixelRatio })
      }
      map!.addSource(SOURCE_ID, { type: 'geojson', data })
      map!.addLayer({
        id: LAYER_ID,
        type: 'circle',
        source: SOURCE_ID,
        filter: ['==', ['get', 'kind'], 'normal'],
        paint: {
          'circle-radius': 5,
          'circle-color': '#2563eb',
          'circle-stroke-color': '#fff',
          'circle-stroke-width': 1.5,
        },
      })
      map!.addLayer({
        id: SAT_LAYER_ID,
        type: 'symbol',
        source: SOURCE_ID,
        filter: ['==', ['get', 'kind'], 'satellite'],
        layout: { 'icon-image': SAT_IMAGE_ID, 'icon-allow-overlap': true, 'icon-ignore-placement': true },
      })
      map!.addLayer({
        id: MISSED_LAYER_ID,
        type: 'symbol',
        source: SOURCE_ID,
        filter: ['==', ['get', 'kind'], 'missed'],
        layout: { 'icon-image': MISSED_IMAGE_ID, 'icon-allow-overlap': true, 'icon-ignore-placement': true },
      })
    }

    // Re-run after every style load (initial and MapStyleToggle swaps), which wipes sources/layers.
    if (map.isStyleLoaded()) apply()
    map.on('style.load', apply)
    // Re-route the missed-read markers once the tiles carrying the tracks have loaded or changed.
    const reroute = () => setTracksVersion(v => v + 1)
    map.on('moveend', reroute)
    map.once('idle', reroute)
    return () => {
      map.off('style.load', apply)
      map.off('moveend', reroute)
      map.off('idle', reroute)
    }
  }, [mapRef, positions, missed])
}
