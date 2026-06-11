import { useEffect, useRef, type RefObject } from 'react'
import mapboxgl from 'mapbox-gl'
import type { RunnerPosition } from './types.ts'

interface MarkerEntry {
  marker: mapboxgl.Marker
  el: HTMLElement
  isLatest: boolean
}

export function useRunnerMarkers(
  mapRef: RefObject<mapboxgl.Map | null>,
  sessionCode: string | null,
  serverUrl: string,
  intervalMs: number,
): void {
  const markersRef = useRef<Record<string, MarkerEntry>>({})
  const hasLocatedRef = useRef(false)

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
                const svg = existing.el.querySelector('svg')
                if (svg) (svg as HTMLElement).style.transform = `rotate(${heading}deg)`
              }
            } else {
              existing?.marker.remove()
              const el = createMarkerEl(runnerName, heading, isLatest)
              const marker = new mapboxgl.Marker({ element: el })
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

  useEffect(() => {
    return () => {
      for (const { marker } of Object.values(markersRef.current)) {
        marker.remove()
      }
      markersRef.current = {}
    }
  }, [])
}

function createMarkerEl(name: string, heading: number | null, isLatest: boolean): HTMLElement {
  const wrapper = document.createElement('div')
  wrapper.style.cssText = 'display:flex;flex-direction:column;align-items:center;gap:4px;'

  if (isLatest) {
    const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    arrow.setAttribute('width', '32')
    arrow.setAttribute('height', '32')
    arrow.setAttribute('viewBox', '0 0 24 24')
    arrow.style.cssText = `
      filter: drop-shadow(0 1px 3px rgba(0,0,0,0.35));
      transform: rotate(${heading ?? 0}deg);
    `
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    path.setAttribute('d', 'M12 2 L20 20 L12 15 L4 20 Z')
    path.setAttribute('fill', '#3b82f6')
    path.setAttribute('stroke', 'white')
    path.setAttribute('stroke-width', '1.5')
    path.setAttribute('stroke-linejoin', 'round')
    arrow.appendChild(path)
    wrapper.appendChild(arrow)
  } else {
    const dot = document.createElement('div')
    dot.style.cssText = `
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: #3b82f6;
      border: 2px solid white;
      box-shadow: 0 1px 3px rgba(0,0,0,0.35);
    `
    wrapper.appendChild(dot)
  }

  if (isLatest) {
    const label = document.createElement('div')
    label.textContent = name
    label.style.cssText = `
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
  }

  return wrapper
}
