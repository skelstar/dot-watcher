// LINZ Basemaps styles. Coverage is New Zealand only — outside NZ the map renders blank. The key
// is embedded in the browser bundle (like a Mapbox public token before it): treat it as public,
// don't reuse it for anything else. See client/plans/linz-topo-migration.md.
const key = import.meta.env.VITE_LINZ_API_KEY as string

export type MapStyleId = 'topo' | 'aerial'

interface MapStyleOption {
  url: string
  // Shown on the toggle button as the style you'd switch *to* from this one.
  label: string
}

// 'topo' is LINZ's contour/trail-oriented vector topographic map — good for off-road routes, but
// it draws roads simply with no building footprints, so it reads poorly for urban routes. 'aerial'
// is LINZ's real aerial imagery with a road/label overlay ("hybrid"), which shows actual streets
// and buildings and reads much better in town. Neither is a generic "streets" style — LINZ doesn't
// offer one; these are the two genuinely different options in their catalogue.
export const MAP_STYLES: Record<MapStyleId, MapStyleOption> = {
  topo: {
    url: `https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=${key}`,
    label: 'Topo',
  },
  aerial: {
    url: `https://basemaps.linz.govt.nz/v1/styles/aerialhybrid.json?api=${key}`,
    label: 'Aerial',
  },
}

export const DEFAULT_MAP_STYLE_ID: MapStyleId = 'topo'

export function otherMapStyleId(id: MapStyleId): MapStyleId {
  return id === 'topo' ? 'aerial' : 'topo'
}

// Neither LINZ style declares its own top-level attribution (topo has none at all; aerial's
// "LINZ Basemaps" label source does, but its imagery source doesn't), so this is supplied
// explicitly via the Map's `attributionControl.customAttribution` option. When the aerial style
// is active, MapLibre's AttributionControl also folds in that style's own per-source attribution
// text automatically — expect to see both LINZ notices together in that case, which is redundant
// but not wrong, and safer than trying to suppress LINZ's own attribution text.
export const LINZ_ATTRIBUTION =
  '© <a href="https://www.linz.govt.nz/linz-copyright" target="_blank" rel="noopener">LINZ CC BY 4.0</a> · ' +
  '<a href="https://www.linz.govt.nz/data/linz-data/linz-basemaps/data-attribution" target="_blank" rel="noopener">Basemaps data attribution</a>'
