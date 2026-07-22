export function parseGpxCoordinates(xml: string): [number, number][] {
  const check = new DOMParser().parseFromString(xml, 'application/xml')
  if (check.querySelector('parsererror')) throw new Error('Invalid GPX file — could not parse XML.')

  const trkptRe = /<(?:[^:>\s]+:)?trkpt\b([^>]*)>/g
  const latRe = /lat="([^"]+)"/
  const lonRe = /lon="([^"]+)"/

  const coordinates: [number, number][] = []
  let m: RegExpExecArray | null

  while ((m = trkptRe.exec(xml)) !== null) {
    const attrs = m[1]
    const lat = parseFloat(latRe.exec(attrs)?.[1] ?? '')
    const lon = parseFloat(lonRe.exec(attrs)?.[1] ?? '')
    if (Number.isFinite(lat) && Number.isFinite(lon)) coordinates.push([lon, lat])
  }

  return coordinates
}
