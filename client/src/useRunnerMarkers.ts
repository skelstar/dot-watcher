import { useEffect, useRef, useState, type RefObject } from 'react'
import mapboxgl from 'mapbox-gl'
import type { RunnerPosition } from './types.ts'

interface MarkerEntry {
  marker: mapboxgl.Marker
  el: HTMLElement
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

            const existing = markersRef.current[key]
            if (existing && existing.isLatest === isLatest) {
              existing.marker.setLngLat(lngLat)
              if (isLatest && heading != null) {
                const svg = existing.el.querySelector('svg') as HTMLElement | null
                if (svg) {
                  svg.style.transform = `rotate(${heading}deg)`
                  svg.style.transformOrigin = `50% ${(2 / 24) * 100}%`
                }
              }
            } else {
              existing?.marker.remove()
              const el = createMarkerEl(runnerName, heading, isLatest)
              const offset: [number, number] = isLatest ? [0, ARROW_TIP_OFFSET] : [0, 0]
              const marker = new mapboxgl.Marker({ element: el, offset })
                .setLngLat(lngLat)
                .addTo(map)
              markersRef.current[key] = { marker, el, isLatest }
            }
          })
        }

        for (const key of Object.keys(markersRef.current)) {
          if (!seen.has(key)) {
            markersRef.current[key].marker.remove()
            delete markersRef.current[key]
          }
        }

        for (const positions of runnerGroups) {
          if (!positions.length) continue
          const latest = positions[positions.length - 1]
          latestPositionsRef.current[latest.runnerName] = [latest.longitude, latest.latitude]
        }
        updateVisibleRunners(map)


        if (seen.size > 0 && !hasLocatedRef.current) {
          hasLocatedRef.current = true
          // fit to latest positions only, not history dots
          const latestCoords = runnerGroups
            .filter(g => g.length > 0)
            .map(g => g[g.length - 1])
            .map(p => new mapboxgl.LngLat(p.longitude, p.latitude))
          const coords = latestCoords
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
    const pad = 80  // px — runner stays "visible" until this far outside the viewport
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
      for (const { marker } of Object.values(markersRef.current)) {
        marker.remove()
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


// Arrow SVG: viewBox 0 0 24 24, rendered 32×32. Tip at viewBox y=2 → (2/24)*32 = 2.67px from top.
// Center is at 16px. Offset to put tip at coordinate = center - tip = 16 - 2.67 = 13.33px.
const ARROW_SIZE = 32
const ARROW_TIP_OFFSET = ARROW_SIZE * (0.5 - 2 / 24)  // 13.33px

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

function createMarkerEl(name: string, heading: number | null, isLatest: boolean): HTMLElement {
  const colour = runnerColour(name)

  if (isLatest) {
    // Wrapper is exactly the SVG size so Mapbox anchors to the SVG center, not label center.
    // Label is absolutely positioned below so it doesn't affect the bounding box.
    const wrapper = document.createElement('div')
    wrapper.style.cssText = `position:relative;width:${ARROW_SIZE}px;height:${ARROW_SIZE}px;`

    const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    arrow.setAttribute('width', String(ARROW_SIZE))
    arrow.setAttribute('height', String(ARROW_SIZE))
    arrow.setAttribute('viewBox', '0 0 24 24')
    arrow.style.cssText = `
      display:block;
      filter: drop-shadow(0 1px 3px rgba(0,0,0,0.35));
      transform: rotate(${heading ?? 0}deg);
      transform-origin: 50% ${(2 / 24) * 100}%;
    `
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    path.setAttribute('d', 'M12 2 L20 20 L12 15 L4 20 Z')
    path.setAttribute('fill', colour)
    path.setAttribute('stroke', 'white')
    path.setAttribute('stroke-width', '1.5')
    path.setAttribute('stroke-linejoin', 'round')
    arrow.appendChild(path)
    wrapper.appendChild(arrow)

    const label = document.createElement('div')
    label.textContent = name
    label.style.cssText = `
      position: absolute;
      top: ${ARROW_SIZE + 4}px;
      left: 50%;
      transform: translateX(-50%);
      font-size: 11px;
      font-family: system-ui, sans-serif;
      font-weight: 600;
      color: #1e293b;
      background: rgba(255,255,255,0.85);
      padding: 1px 5px;
      border-radius: 4px;
      white-space: nowrap;
      box-shadow: 0 1px 3px rgba(0,0,0,0.2);
    `
    wrapper.appendChild(label)

    const crossSize = 12
    const tipY = ARROW_SIZE * 2 / 24  // px from top of wrapper to arrow tip
    const cross = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    cross.setAttribute('width', String(crossSize))
    cross.setAttribute('height', String(crossSize))
    cross.setAttribute('viewBox', `0 0 ${crossSize} ${crossSize}`)
    cross.style.cssText = `
      position: absolute;
      top: ${tipY}px;
      left: 50%;
      transform: translate(-50%, -50%);
      pointer-events: none;
    `
    const half = crossSize / 2
    for (const [x1, y1, x2, y2] of [[0, half, crossSize, half], [half, 0, half, crossSize]]) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line')
      line.setAttribute('x1', String(x1))
      line.setAttribute('y1', String(y1))
      line.setAttribute('x2', String(x2))
      line.setAttribute('y2', String(y2))
      line.setAttribute('stroke', 'red')
      line.setAttribute('stroke-width', '2')
      cross.appendChild(line)
    }
    wrapper.appendChild(cross)

    return wrapper
  } else {
    const dot = document.createElement('div')
    dot.style.cssText = `
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: ${colour};
      border: 2px solid white;
      box-shadow: 0 1px 3px rgba(0,0,0,0.35);
    `
    return dot
  }
}
