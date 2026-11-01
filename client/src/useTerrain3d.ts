import { useCallback, useRef, useState, type RefObject } from 'react'
import type { MapLibreMap } from 'maplibre-gl'

// Experimental 3D terrain: LINZ's 1m DEM (declared as the 'LINZ-Terrain' raster-dem source in
// both styles) draped under the imagery, with a pitched camera. See client/plans/linz-topo-migration.md.
const TERRAIN = { source: 'LINZ-Terrain', exaggeration: 1 }

// Tilting the map by hand (right-drag / ctrl-drag, or a two-finger vertical drag on touch)
// switches 3D terrain on once past this pitch, so a stray nudge doesn't start fetching DEM tiles.
const AUTO_3D_PITCH_DEG = 10
// ...and tilting back to (nearly) flat switches it off again.
const AUTO_FLAT_PITCH_DEG = 3

/** Create the map with `maxPitch` at least this, or tilting never reaches AUTO_3D_PITCH_DEG. */
export const TERRAIN_MAX_PITCH = 85

/**
 * The 3D terrain switch for a map. Call `attach(map)` once when the map is created so tilting by hand
 * turns 3D on and off, and the terrain survives a style change; `toggle` is for the button.
 */
export function useTerrain3d(mapRef: RefObject<MapLibreMap | null>) {
  const [enabled, setEnabled] = useState(false)
  const enabledRef = useRef(false)

  const attach = useCallback((map: MapLibreMap) => {
    // setStyle() wipes the terrain along with everything else, so re-apply it on every style load.
    map.on('style.load', () => {
      if (enabledRef.current) map.setTerrain(TERRAIN)
    })

    // Only user gestures carry an originalEvent; the toggle's own easeTo() animations don't, so
    // they can't trigger these and fight the toggle.
    map.on('pitch', e => {
      if (!e.originalEvent || enabledRef.current || map.getPitch() < AUTO_3D_PITCH_DEG) return
      enabledRef.current = true
      setEnabled(true)
      map.setTerrain(TERRAIN)
    })
    map.on('pitchend', e => {
      if (!e.originalEvent || !enabledRef.current || map.getPitch() > AUTO_FLAT_PITCH_DEG) return
      enabledRef.current = false
      setEnabled(false)
      map.setTerrain(null)
    })
  }, [])

  const toggle = useCallback(() => {
    const map = mapRef.current
    const next = !enabledRef.current
    enabledRef.current = next
    setEnabled(next)
    if (!map) return
    if (next) {
      map.setTerrain(TERRAIN)
      map.easeTo({ pitch: 60, duration: 800 })
    } else {
      map.setTerrain(null)
      map.easeTo({ pitch: 0, bearing: 0, duration: 800 })
    }
  }, [mapRef])

  return { enabled, toggle, attach }
}
