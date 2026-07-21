import type { LatLon } from './types'

// Namespace-agnostic trkpt matching (handles both bare <trkpt> and prefixed <ns:trkpt>, as real
// GPX exports vary) — lat/lon only, no timestamps/heading, since route-following only needs the
// path itself. See GpxConverterPage.tsx's parseGpx for the fuller version used by the Importer
// tab, which also extracts per-point timestamps for replay pacing.
export function parseGpxTrackpoints(xml: string): LatLon[] {
  const check = new DOMParser().parseFromString(xml, 'application/xml')
  if (check.querySelector('parsererror')) throw new Error('Invalid GPX file — could not parse XML.')

  const trkptRe = /<(?:[^:>\s]+:)?trkpt\b([^>]*)>/g
  const latRe = /lat="([^"]+)"/
  const lonRe = /lon="([^"]+)"/

  const points: LatLon[] = []
  let m: RegExpExecArray | null
  while ((m = trkptRe.exec(xml)) !== null) {
    const attrs = m[1]
    const lat = parseFloat(latRe.exec(attrs)?.[1] ?? '')
    const lon = parseFloat(lonRe.exec(attrs)?.[1] ?? '')
    if (Number.isFinite(lat) && Number.isFinite(lon)) points.push({ lat, lon })
  }
  return points
}
