import { computeBearing } from './positions'

export { computeBearing }

const EARTH_RADIUS_M = 6_371_000

export function distanceMeters(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLat = toRad(lat2 - lat1)
  const dLon = toRad(lon2 - lon1)
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2
  return 2 * EARTH_RADIUS_M * Math.asin(Math.sqrt(a))
}

// Given a start point, a compass bearing, and a distance, returns the destination lat/lon
// (standard spherical "destination point given distance and bearing" formula).
export function destinationPoint(lat: number, lon: number, bearingDeg: number, distanceM: number): { lat: number; lon: number } {
  const toRad = (d: number) => (d * Math.PI) / 180
  const toDeg = (r: number) => (r * 180) / Math.PI
  const delta = distanceM / EARTH_RADIUS_M
  const theta = toRad(bearingDeg)
  const phi1 = toRad(lat)
  const lambda1 = toRad(lon)

  const phi2 = Math.asin(
    Math.sin(phi1) * Math.cos(delta) + Math.cos(phi1) * Math.sin(delta) * Math.cos(theta)
  )
  const lambda2 = lambda1 + Math.atan2(
    Math.sin(theta) * Math.sin(delta) * Math.cos(phi1),
    Math.cos(delta) - Math.sin(phi1) * Math.sin(phi2)
  )

  return { lat: toDeg(phi2), lon: ((toDeg(lambda2) + 540) % 360) - 180 }
}
