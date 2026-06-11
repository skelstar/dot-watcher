import { useEffect, useRef, type RefObject } from 'react'
import mapboxgl from 'mapbox-gl'
import type { RunnerPosition } from './types.ts'

interface MarkerEntry {
  marker: mapboxgl.Marker
  el: HTMLElement
}

export function useRunnerMarkers(
  mapRef: RefObject<mapboxgl.Map | null>,
  sessionCode: string | null,
  serverUrl: string,
  intervalMs: number,
): void {
  const markersRef = useRef<Record<string, MarkerEntry>>({})

  useEffect(() => {
    if (!sessionCode) return

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
          const latest = positions[positions.length - 1]
          const { runnerName, latitude, longitude, heading } = latest
          seen.add(runnerName)

          const lngLat: [number, number] = [longitude, latitude]

          if (markersRef.current[runnerName]) {
            markersRef.current[runnerName].marker.setLngLat(lngLat)
            if (heading != null) {
              markersRef.current[runnerName].el.style.transform = `rotate(${heading}deg)`
            }
          } else {
            const el = createMarkerEl(runnerName, heading)
            const marker = new mapboxgl.Marker({ element: el })
              .setLngLat(lngLat)
              .addTo(map)
            markersRef.current[runnerName] = { marker, el }
          }
        }

        for (const name of Object.keys(markersRef.current)) {
          if (!seen.has(name)) {
            markersRef.current[name].marker.remove()
            delete markersRef.current[name]
          }
        }

        if (seen.size > 0) {
          const coords = Object.values(markersRef.current).map(({ marker }) => marker.getLngLat())
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

function createMarkerEl(name: string, heading: number | null): HTMLElement {
  const wrapper = document.createElement('div')
  wrapper.style.cssText = 'display:flex;flex-direction:column;align-items:center;gap:3px;'

  const dot = document.createElement('div')
  dot.style.cssText = `
    width: 14px;
    height: 14px;
    border-radius: 50%;
    background: #3b82f6;
    border: 2px solid #fff;
    box-shadow: 0 1px 4px rgba(0,0,0,0.4);
  `
  if (heading != null) {
    dot.style.borderRadius = '50% 50% 50% 0'
    dot.style.transform = `rotate(${heading}deg)`
  }

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

  wrapper.appendChild(dot)
  wrapper.appendChild(label)
  return wrapper
}
