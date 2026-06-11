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
}

export function useRunnerMarkers(
  mapRef: RefObject<mapboxgl.Map | null>,
  sessionCode: string | null,
  serverUrl: string,
  intervalMs: number,
): RunnerMarkersResult {
  const markersRef = useRef<Record<string, MarkerEntry>>({})
  const hasLocatedRef = useRef(false)
  const latestPositionsRef = useRef<Record<string, [number, number]>>({})
  const [visibleRunners, setVisibleRunners] = useState<string[]>([])
  const [offScreenRunners, setOffScreenRunners] = useState<string[]>([])

  useEffect(() => {
    if (!sessionCode) return
    hasLocatedRef.current = false

    let cancelled = false

    async function fetchAndUpdate() {
      try {
        const res = await fetch(`${serverUrl}/locations/${sessionCode}`)
        if (!res.ok || cancelled) return
        const runnerGroups: RunnerPosition[][] = await res.json()

        const map = mapRef.current
        if (!map || cancelled) return

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
                existing.root.render(createElement(Arrow, { name: runnerName, heading, colour }))
              }
            } else {
              existing?.marker.remove()
              existing?.root.unmount()

              const el = document.createElement('div')
              const root = createRoot(el)
              if (isLatest) {
                root.render(createElement(Arrow, { name: runnerName, heading, colour }))
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

        if (seen.size > 0 && !hasLocatedRef.current) {
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
  }, [sessionCode, serverUrl, intervalMs, mapRef])

  function updateVisibleRunners(map: mapboxgl.Map) {
    const { offsetWidth, offsetHeight } = map.getContainer()
    const pad = 80
    const inView = ([lng, lat]: [number, number]) => {
      const p = map.project(new mapboxgl.LngLat(lng, lat))
      return p.x >= -pad && p.y >= -pad && p.x <= offsetWidth + pad && p.y <= offsetHeight + pad
    }
    const all = Object.entries(latestPositionsRef.current)
    const visible = all.filter(([, pos]) => inView(pos)).map(([name]) => name)
    const offScreen = all.filter(([, pos]) => !inView(pos)).map(([name]) => name)
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
    map.on('move', onMove)
    return () => { map.off('move', onMove) }
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

  return { visibleRunners, offScreenRunners, centerOnRunner }
}

export { ARROW_SIZE }

const COLOUR_PALETTE = [
  '#3b82f6', // blue
  '#ef4444', // red
  '#22c55e', // green
  '#f59e0b', // amber
  '#8b5cf6', // purple
  '#ec4899', // pink
  '#06b6d4', // cyan
  '#f97316', // orange
]

function nameHash(name: string): number {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) >>> 0
  return h
}

export function runnerColour(name: string): string {
  return COLOUR_PALETTE[nameHash(name) % COLOUR_PALETTE.length]
}
