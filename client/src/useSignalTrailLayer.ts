import { useEffect, type RefObject } from 'react'
// maplibre-gl has no default export (unlike mapbox-gl) — named imports only.
import { MapLibreMap, type GeoJSONSource } from 'maplibre-gl'
import type { RunnerPosition } from './types.ts'

const SOURCE_ID = 'signal-trail'
const LAYER_ID = 'signal-trail-dots'

// Plots every given position as a dot coloured by its self-reported isUltraConstrained flag, so
// you can see after a run where the phone was on cellular vs satellite. Pass null to hide it. Doesn't move the
// viewport — the runner markers already follow/fit, and this re-runs on every playhead tick.
export function useSignalTrailLayer(
  mapRef: RefObject<MapLibreMap | null>,
  positions: RunnerPosition[] | null,
) {
  useEffect(() => {
    const map = mapRef.current
    if (!map) return

    function apply() {
      const data: GeoJSON.FeatureCollection<GeoJSON.Point> = {
        type: 'FeatureCollection',
        features: (positions ?? []).map(p => ({
          type: 'Feature',
          properties: { satellite: p.isUltraConstrained === true, timestamp: p.timestamp },
          geometry: { type: 'Point', coordinates: [p.longitude, p.latitude] },
        })),
      }

      const source = map!.getSource(SOURCE_ID) as GeoJSONSource | undefined
      if (source) {
        source.setData(data)
        return
      }
      map!.addSource(SOURCE_ID, { type: 'geojson', data })
      map!.addLayer({
        id: LAYER_ID,
        type: 'circle',
        source: SOURCE_ID,
        paint: {
          'circle-radius': 5,
          'circle-color': ['case', ['get', 'satellite'], '#f97316', '#2563eb'],
          'circle-stroke-color': '#fff',
          'circle-stroke-width': 1.5,
        },
      })
    }

    // Re-run after every style load (initial and MapStyleToggle swaps), which wipes sources/layers.
    if (map.isStyleLoaded()) apply()
    map.on('style.load', apply)
    return () => { map.off('style.load', apply) }
  }, [mapRef, positions])
}
