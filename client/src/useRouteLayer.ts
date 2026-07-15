import { useEffect, type RefObject } from 'react'
import mapboxgl from 'mapbox-gl'

const SOURCE_ID = 'gpx-route'
const LAYER_ID = 'gpx-route-line'
const ARROW_LAYER_ID = 'gpx-route-arrows'
const ARROW_IMAGE_ID = 'gpx-route-arrow-icon'

// A simple right-pointing triangle, drawn at runtime so the arrow doesn't depend on whatever
// icons happen to ship in the active Mapbox style's sprite sheet (e.g. streets-v12 has no
// "triangle-11").
function makeArrowImage(): { width: number; height: number; data: Uint8Array } {
  const size = 16
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')!
  ctx.fillStyle = '#1f6feb'
  ctx.beginPath()
  ctx.moveTo(2, 3)
  ctx.lineTo(14, 8)
  ctx.lineTo(2, 13)
  ctx.closePath()
  ctx.fill()
  const { data } = ctx.getImageData(0, 0, size, size)
  return { width: size, height: size, data: new Uint8Array(data.buffer) }
}

export function useRouteLayer(mapRef: RefObject<mapboxgl.Map | null>, coordinates: [number, number][] | null) {
  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    function applyRoute() {
      const source = map!.getSource(SOURCE_ID) as mapboxgl.GeoJSONSource | undefined
      const data: GeoJSON.Feature<GeoJSON.LineString> = {
        type: 'Feature',
        properties: {},
        geometry: { type: 'LineString', coordinates: coordinates ?? [] },
      }

      if (source) {
        source.setData(data)
        return
      }

      if (!map!.hasImage(ARROW_IMAGE_ID)) map!.addImage(ARROW_IMAGE_ID, makeArrowImage())

      map!.addSource(SOURCE_ID, { type: 'geojson', data })
      map!.addLayer({
        id: LAYER_ID,
        type: 'line',
        source: SOURCE_ID,
        layout: { 'line-join': 'round', 'line-cap': 'round' },
        paint: { 'line-color': '#1f6feb', 'line-width': 3, 'line-opacity': 0.85 },
      })
      // Repeats an arrow icon along the line, auto-oriented to match its direction (symbol-placement:
      // 'line' + rotation-alignment: 'map' rotates each icon to the line's local bearing), so viewers
      // can see which way the route runs without relying on a start/end marker.
      map!.addLayer({
        id: ARROW_LAYER_ID,
        type: 'symbol',
        source: SOURCE_ID,
        layout: {
          'symbol-placement': 'line',
          'symbol-spacing': 200,
          'icon-image': ARROW_IMAGE_ID,
          'icon-size': 1,
          'icon-rotation-alignment': 'map',
          'icon-allow-overlap': true,
          'icon-ignore-placement': true,
        },
        paint: { 'icon-opacity': 0.9 },
      })
    }

    if (map.isStyleLoaded()) {
      applyRoute()
    } else {
      map.once('load', applyRoute)
      return () => { map.off('load', applyRoute) }
    }
  }, [mapRef, coordinates])

  useEffect(() => {
    const map = mapRef.current
    return () => {
      if (!map || !map.getStyle()) return
      if (map.getLayer(ARROW_LAYER_ID)) map.removeLayer(ARROW_LAYER_ID)
      if (map.getLayer(LAYER_ID)) map.removeLayer(LAYER_ID)
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
      if (map.hasImage(ARROW_IMAGE_ID)) map.removeImage(ARROW_IMAGE_ID)
    }
  }, [mapRef])
}
