import { useEffect, useRef, useState, type RefObject } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { createElement } from 'react'
import mapboxgl from 'mapbox-gl'
import type { RunnerPosition } from './types.ts'
import Arrow, { ARROW_SIZE } from './components/Arrow.tsx'
import Dot from './components/Dot.tsx'

interface MarkerEntry {
  marker: mapboxgl.Marker
  root: Root
  isLatest: boolean
}

interface RunnerMarkersResult {
  visibleRunners: string[]
  offScreenRunners: string[]
  centerOnRunner: (name: string) => void
  fitAll: () => void
}

export function useRunnerMarkers(
  mapRef: RefObject<mapboxgl.Map | null>,
  sessionCode: string | null,
  serverUrl: string,
  accessToken: string | null,
  intervalMs: number,
  replayPositions?: RunnerPosition[][],
  replayNowMs?: number,
): RunnerMarkersResult {
  const markersRef = useRef<Record<string, MarkerEntry>>({})
  const hasLocatedRef = useRef(false)
  const latestPositionsRef = useRef<Record<string, [number, number]>>({})
  const latestMarkerRef = useRef<Record<string, { root: Root; heading: number | null; colour: string; timestamp: string }>>({})
  const virtualNowRef = useRef<number | null>(null)
  const [visibleRunners, setVisibleRunners] = useState<string[]>([])
  const [offScreenRunners, setOffScreenRunners] = useState<string[]>([])

  function applyPositions(runnerGroups: RunnerPosition[][], map: mapboxgl.Map, virtualNow?: number) {
    if (virtualNow !== undefined) virtualNowRef.current = virtualNow
    const isFirstLoad = !hasLocatedRef.current
    const seen = new Set<string>()

    for (const positions of runnerGroups) {
      if (!positions.length) continue
      const lastIndex = positions.length - 1

      positions.forEach((pos, i) => {
        const { runnerName, latitude, longitude, heading } = pos
        const key = `${runnerName}:${pos.timestamp}`
        const isLatest = i === lastIndex
        seen.add(key)

        const lngLat: [number, number] = [longitude, latitude]
        const colour = runnerColour(runnerName)

        const existing = markersRef.current[key]
        if (existing && existing.isLatest === isLatest) {
          existing.marker.setLngLat(lngLat)
          if (isLatest) {
            latestMarkerRef.current[runnerName] = { root: existing.root, heading, colour, timestamp: pos.timestamp }
          }
        } else {
          existing?.marker.remove()
          existing?.root.unmount()

          const el = document.createElement('div')
          const root = createRoot(el)
          if (isLatest) {
            latestMarkerRef.current[runnerName] = { root, heading, colour, timestamp: pos.timestamp }
          } else {
            root.render(createElement(Dot, { colour }))
          }
          const marker = new mapboxgl.Marker({ element: el, offset: [0, 0] })
            .setLngLat(lngLat)
            .addTo(map)
          markersRef.current[key] = { marker, root, isLatest }
        }
      })
    }

    for (const key of Object.keys(markersRef.current)) {
      if (!seen.has(key)) {
        markersRef.current[key].marker.remove()
        markersRef.current[key].root.unmount()
        delete markersRef.current[key]
      }
    }

    if (seen.size === 0) hasLocatedRef.current = false

    for (const positions of runnerGroups) {
      if (!positions.length) continue
      const latest = positions[positions.length - 1]
      latestPositionsRef.current[latest.runnerName] = [latest.longitude, latest.latitude]
    }
    updateVisibleRunners(map)
    recluster(map)

    if (seen.size > 0 && isFirstLoad) {
      hasLocatedRef.current = true
      const latestCoords = runnerGroups
        .filter(g => g.length > 0)
        .map(g => g[g.length - 1])
        .map(p => new mapboxgl.LngLat(p.longitude, p.latitude))
      if (latestCoords.length === 1) {
        map.easeTo({ center: latestCoords[0], zoom: 15 })
      } else {
        const bounds = latestCoords.reduce(
          (b, c) => b.extend(c),
          new mapboxgl.LngLatBounds(latestCoords[0], latestCoords[0]),
        )
        map.fitBounds(bounds, { padding: 80, maxZoom: 16 })
      }
    }
  }

  // Live polling effect — skipped when replayPositions is provided
  useEffect(() => {
    if (!sessionCode || !accessToken || replayPositions !== undefined) return
    hasLocatedRef.current = false

    let cancelled = false

    async function fetchAndUpdate() {
      try {
        const res = await fetch(`${serverUrl}/locations/${sessionCode}`, {
          headers: { 'Authorization': `Bearer ${accessToken}` },
        })
        if (!res.ok || cancelled) return
        const runnerGroups: RunnerPosition[][] = await res.json()

        const map = mapRef.current
        if (!map || cancelled) return

        applyPositions(runnerGroups, map)
      } catch {
        // network errors are silent — we'll retry on the next interval
      }
    }

    fetchAndUpdate()
    const id = setInterval(fetchAndUpdate, intervalMs)
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [sessionCode, serverUrl, accessToken, intervalMs, mapRef, replayPositions]) // eslint-disable-line react-hooks/exhaustive-deps

  // Replay effect — runs when replayPositions changes
  useEffect(() => {
    if (replayPositions === undefined) return
    const map = mapRef.current
    if (!map) return
    applyPositions(replayPositions, map, replayNowMs)
  }, [replayPositions, replayNowMs]) // eslint-disable-line react-hooks/exhaustive-deps

  function recluster(map: mapboxgl.Map) {
    const runners = Object.entries(latestPositionsRef.current)
    if (runners.length === 0) return

    const THRESHOLD_PX = 60
    const assigned = new Set<string>()
    const labels = new Map<string, string>()

    for (const [name, lngLat] of runners) {
      if (assigned.has(name)) continue
      const p = map.project(new mapboxgl.LngLat(lngLat[0], lngLat[1]))
      const cluster = [name]
      assigned.add(name)

      for (const [otherName, otherLngLat] of runners) {
        if (assigned.has(otherName)) continue
        const q = map.project(new mapboxgl.LngLat(otherLngLat[0], otherLngLat[1]))
        if (Math.hypot(q.x - p.x, q.y - p.y) <= THRESHOLD_PX) {
          cluster.push(otherName)
          assigned.add(otherName)
        }
      }

      labels.set(cluster[0], cluster.join(', '))
      for (let i = 1; i < cluster.length; i++) labels.set(cluster[i], '')
    }

    const now = virtualNowRef.current ?? Date.now()
    const stationaryRunners = new Set<string>()

    for (const [name, info] of Object.entries(latestMarkerRef.current)) {
      const label = labels.get(name) ?? name
      const stationary = now - new Date(info.timestamp).getTime() > 45_000
      if (stationary) stationaryRunners.add(name)
      info.root.render(createElement(Arrow, { name, heading: info.heading, colour: info.colour, label, stationary }))
    }

    for (const [key, entry] of Object.entries(markersRef.current)) {
      if (entry.isLatest) continue
      const runnerName = key.substring(0, key.indexOf(':'))
      entry.root.render(createElement(Dot, { colour: runnerColour(runnerName), hidden: stationaryRunners.has(runnerName) }))
    }
  }

  function updateVisibleRunners(map: mapboxgl.Map) {
    const all = Object.entries(latestPositionsRef.current)
    const visible = all.filter(([, pos]) => isInView(map, pos)).map(([name]) => name)
    const offScreen = all.filter(([, pos]) => !isInView(map, pos)).map(([name]) => name)
    setVisibleRunners(prev =>
      prev.length === visible.length && prev.every((r, i) => r === visible[i]) ? prev : visible
    )
    setOffScreenRunners(prev =>
      prev.length === offScreen.length && prev.every((r, i) => r === offScreen[i]) ? prev : offScreen
    )
  }

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const onMove = () => updateVisibleRunners(map)
    const onMoveEnd = () => recluster(map)
    map.on('move', onMove)
    map.on('moveend', onMoveEnd)
    return () => {
      map.off('move', onMove)
      map.off('moveend', onMoveEnd)
    }
  }, [mapRef.current]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    return () => {
      for (const { marker, root } of Object.values(markersRef.current)) {
        marker.remove()
        root.unmount()
      }
      markersRef.current = {}
    }
  }, [])

  function centerOnRunner(name: string) {
    const pos = latestPositionsRef.current[name]
    const map = mapRef.current
    if (!pos || !map) return
    map.easeTo({ center: pos, zoom: Math.max(map.getZoom(), 15) })
  }

  function fitAll() {
    const map = mapRef.current
    const coords = Object.values(latestPositionsRef.current)
    if (!map || coords.length === 0) return
    if (coords.length === 1) {
      map.easeTo({ center: coords[0], zoom: 15 })
    } else {
      const bounds = coords.reduce(
        (b, c) => b.extend(c),
        new mapboxgl.LngLatBounds(coords[0], coords[0]),
      )
      map.fitBounds(bounds, { padding: 80, maxZoom: 16 })
    }
  }

  return { visibleRunners, offScreenRunners, centerOnRunner, fitAll }
}

export { ARROW_SIZE }

const COLOUR_PALETTE = [
  '#2563eb', // blue
  '#dc2626', // red
  '#16a34a', // green
  '#d97706', // amber
  '#9333ea', // purple (clearly distinct from blue)
  '#db2777', // pink
  '#0891b2', // teal
  '#ea580c', // orange
]

function isInView(map: mapboxgl.Map, [lng, lat]: [number, number]): boolean {
  const { offsetWidth, offsetHeight } = map.getContainer()
  const pad = 80
  const p = map.project(new mapboxgl.LngLat(lng, lat))
  return p.x >= -pad && p.y >= -pad && p.x <= offsetWidth + pad && p.y <= offsetHeight + pad
}

function nameHash(name: string): number {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) >>> 0
  return h
}

export function runnerColour(name: string): string {
  return COLOUR_PALETTE[nameHash(name) % COLOUR_PALETTE.length]
}
