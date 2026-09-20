// LINZ Basemaps topographic vector style. Coverage is New Zealand only — outside NZ the map
// renders blank. The key is embedded in the browser bundle (like a Mapbox public token before
// it): treat it as public, don't reuse it for anything else. See client/plans/linz-topo-migration.md.
const key = import.meta.env.VITE_LINZ_API_KEY as string

export const LINZ_TOPO_STYLE = `https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=${key}`

export const LINZ_ATTRIBUTION =
  '© <a href="https://www.linz.govt.nz/linz-copyright" target="_blank" rel="noopener">LINZ CC BY 4.0</a> · ' +
  '<a href="https://www.linz.govt.nz/data/linz-data/linz-basemaps/data-attribution" target="_blank" rel="noopener">Basemaps data attribution</a>'
