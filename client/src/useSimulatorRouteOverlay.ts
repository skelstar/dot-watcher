import { useEffect, useState, type RefObject } from 'react'
import mapboxgl from 'mapbox-gl'

const SOURCE_ID = 'simulator-routes'
const LAYER_ID = 'simulator-routes-line'

// Must match tools/simulator/src/ConvergencePage.tsx's MESSAGE_TYPE exactly — that's the only
// sender, and this is the only listener.
const MESSAGE_TYPE = 'dotwatcher-simulator-routes'

// Field is "points" (not "coordinates") to match tools/simulator/src/ConvergencePage.tsx's
// message payload exactly — [lon, lat] pairs, already GeoJSON-ordered by the sender.
type SimulatorRoute = { id: number; color: string; points: [number, number][] }

function parseMessage(data: unknown): SimulatorRoute[] | null {
  if (!data || typeof data !== 'object') return null
  const msg = data as { type?: unknown; routes?: unknown }
  if (msg.type !== MESSAGE_TYPE || !Array.isArray(msg.routes)) return null
  return msg.routes as SimulatorRoute[]
}

// Lets the Convergence simulator (tools/simulator) — which embeds this app in an iframe to
// preview live sessions while testing — draw each simulated phone's GPX route on this map in
// that phone's own colour. Entirely postMessage-driven since the simulator is a separate Vite
// dev server (different origin), so it has no same-origin DOM access into this app. Inert for
// every real user: nobody else ever posts this message, and this app is never iframed anywhere
// outside that one dev tool.
export function useSimulatorRouteOverlay(mapRef: RefObject<mapboxgl.Map | null>) {
  const [routes, setRoutes] = useState<SimulatorRoute[]>([])

  useEffect(() => {
    function handleMessage(event: MessageEvent) {
      const parsed = parseMessage(event.data)
      if (parsed) setRoutes(parsed)
    }
    window.addEventListener('message', handleMessage)
    return () => window.removeEventListener('message', handleMessage)
  }, [])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    function applyRoutes() {
      const data: GeoJSON.FeatureCollection<GeoJSON.LineString> = {
        type: 'FeatureCollection',
        features: routes
          .filter(r => r.points.length > 1)
          .map(r => ({
            type: 'Feature',
            properties: { color: r.color },
            geometry: { type: 'LineString', coordinates: r.points },
          })),
      }

      const source = map!.getSource(SOURCE_ID) as mapboxgl.GeoJSONSource | undefined
      if (source) {
        source.setData(data)
        return
      }

      map!.addSource(SOURCE_ID, { type: 'geojson', data })
      map!.addLayer({
        id: LAYER_ID,
        type: 'line',
        source: SOURCE_ID,
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': ['get', 'color'], 'line-width': 4, 'line-opacity': 0.8 },
      })
    }

    if (map.isStyleLoaded()) {
      applyRoutes()
    } else {
      map.once('load', applyRoutes)
      return () => { map.off('load', applyRoutes) }
    }
  }, [mapRef, routes])

  useEffect(() => {
    const map = mapRef.current
    return () => {
      if (!map || !map.getStyle()) return
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
    }
  }, [mapRef])
}
