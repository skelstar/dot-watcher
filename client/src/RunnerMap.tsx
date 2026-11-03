import { useEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
// maplibre-gl has no default export (unlike mapbox-gl) — named imports only.
import { MapLibreMap, NavigationControl } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { MAP_STYLES, DEFAULT_MAP_STYLE_ID, LINZ_ATTRIBUTION, LEGAL_LINKS_ATTRIBUTION, type MapStyleId } from './map/mapStyle.ts'
import MapStyleToggle from './MapStyleToggle.tsx'
import Terrain3DToggle from './Terrain3DToggle.tsx'
import Legend from './Legend.tsx'
import LegendHelp from './LegendHelp.tsx'
import { useRunnerMarkers } from './useRunnerMarkers.ts'
import { useSimulatorRouteOverlay } from './useSimulatorRouteOverlay.ts'
import type { RunnerPosition } from './types.ts'
import type { RunnerCountdown } from './useSessionTimelineLogic.ts'

/**
 * What the map and legend show about the runners on it. Anything that can produce this (a session's
 * timeline today) gets the same dots, legend, follow-a-runner and fit-all behaviour.
 */
export interface RunnerMapData {
  /** One array per runner, as POST /location returns them. */
  positions: RunnerPosition[][] | undefined
  /** The moment the positions are shown for: the playhead in a session, the wall clock when live. */
  virtualNowMs: number
  runnersWithGpsSignalLoss: Set<string>
  runnersWithGap: Set<string>
  runnersSleeping: Set<string>
  runnersUltraConstrained: Set<string>
  runnerBatteryStatus: Map<string, 'low' | 'critical'>
  runnerCountdowns: Map<string, RunnerCountdown>
  runnerLastSeenMs: Map<string, number>
}

interface Props {
  /**
   * Owned by the caller so it can attach its own layers (route, trace) to the map. RunnerMap sets it
   * when the map exists and clears it on unmount; the caller's effects run after RunnerMap's.
   */
  mapRef: RefObject<MapLibreMap | null>
  data: RunnerMapData
  /** Overlays specific to one kind of map (account button, replay controls, toasts). */
  children?: ReactNode
}

// Tilting the map by hand (right-drag / ctrl-drag, or a two-finger vertical drag on touch)
// switches 3D terrain on once past this pitch, so a stray nudge doesn't start fetching DEM tiles.
const AUTO_3D_PITCH_DEG = 10
// ...and tilting back to (nearly) flat switches it off again.
const AUTO_FLAT_PITCH_DEG = 3

/** The full-screen map with the runner dots, legend and map controls shared by every runner map. */
export default function RunnerMap({ mapRef, data, children }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [mapStyleId, setMapStyleId] = useState<MapStyleId>(DEFAULT_MAP_STYLE_ID)

  // Imperative: swaps the live map's style in place rather than recreating the Map instance.
  // useRouteLayer/useSimulatorRouteOverlay re-add their sources/layers/images on the resulting
  // 'style.load' event; runner markers are plain DOM overlays untouched by a style change.
  function handleToggleMapStyle(next: MapStyleId) {
    setMapStyleId(next)
    mapRef.current?.setStyle(MAP_STYLES[next].url)
  }

  // Experimental 3D terrain: LINZ's 1m DEM (declared as the 'LINZ-Terrain' raster-dem source in
  // both styles) draped under the imagery, with a pitched camera. See client/plans/linz-topo-migration.md.
  const [terrain3d, setTerrain3d] = useState(false)
  const terrain3dRef = useRef(false)

  function handleToggleTerrain3d() {
    const map = mapRef.current
    const next = !terrain3d
    terrain3dRef.current = next
    setTerrain3d(next)
    if (!map) return
    if (next) {
      map.setTerrain({ source: 'LINZ-Terrain', exaggeration: 1 })
      map.easeTo({ pitch: 60, duration: 800 })
    } else {
      map.setTerrain(null)
      map.easeTo({ pitch: 0, bearing: 0, duration: 800 })
    }
  }

  useEffect(() => {
    if (!containerRef.current) return

    const map = new MapLibreMap({
      container: containerRef.current,
      style: MAP_STYLES[DEFAULT_MAP_STYLE_ID].url,
      center: [174.7762, -41.2865], // Wellington, NZ - default before any session/positions load
      zoom: 13,
      maxPitch: 85, // tilting past AUTO_3D_PITCH_DEG turns 3D terrain on; the toggle does the same
      // compact: false keeps the attribution expanded — LINZ requires it to be always visible.
      attributionControl: { compact: false, customAttribution: [LEGAL_LINKS_ATTRIBUTION, LINZ_ATTRIBUTION] },
    })

    // Compass only: the +/- buttons are dropped (pinch/scroll still zoom) and AccountButton takes the
    // top of this column, with the compass pushed below it by index.css.
    map.addControl(new NavigationControl({ showZoom: false }), 'top-right')

    // setStyle() wipes the terrain along with everything else, so re-apply it on every style load.
    map.on('style.load', () => {
      if (terrain3dRef.current) map.setTerrain({ source: 'LINZ-Terrain', exaggeration: 1 })
    })

    // Only user gestures carry an originalEvent; the toggle's own easeTo() animations don't, so
    // they can't trigger these and fight the toggle.
    map.on('pitch', e => {
      if (!e.originalEvent || terrain3dRef.current || map.getPitch() < AUTO_3D_PITCH_DEG) return
      terrain3dRef.current = true
      setTerrain3d(true)
      map.setTerrain({ source: 'LINZ-Terrain', exaggeration: 1 })
    })
    map.on('pitchend', e => {
      if (!e.originalEvent || !terrain3dRef.current || map.getPitch() > AUTO_FLAT_PITCH_DEG) return
      terrain3dRef.current = false
      setTerrain3d(false)
      map.setTerrain(null)
    })

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [mapRef])

  const { allRunners, followRunner, fitAll } = useRunnerMarkers(
    mapRef,
    data.positions,
    data.virtualNowMs,
    data.runnersWithGpsSignalLoss,
    data.runnersWithGap,
    data.runnersSleeping,
    data.runnersUltraConstrained,
  )
  useSimulatorRouteOverlay(mapRef)

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      <LegendHelp />
      <MapStyleToggle styleId={mapStyleId} onToggle={handleToggleMapStyle} />
      <Terrain3DToggle enabled={terrain3d} onToggle={handleToggleTerrain3d} />
      <Legend
        runners={allRunners}
        onRunnerClick={followRunner}
        onFitAll={fitAll}
        runnersWithGap={data.runnersWithGap}
        runnersSleeping={data.runnersSleeping}
        runnersWithGpsSignalLoss={data.runnersWithGpsSignalLoss}
        runnersUltraConstrained={data.runnersUltraConstrained}
        runnerBatteryStatus={data.runnerBatteryStatus}
        runnerCountdowns={data.runnerCountdowns}
        runnerLastSeenMs={data.runnerLastSeenMs}
      />
      {children}
    </>
  )
}
