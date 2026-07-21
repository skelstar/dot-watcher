import { computeBearing } from './positions'
import type { LatLon } from './types'

export { computeBearing }

const EARTH_RADIUS_M = 6_371_000

// A "good" phone within this distance of the convergence point is considered arrived and
// holds position instead of continuing to step towards it.
export const ARRIVE_METERS = 8

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

export type RouteProgress = { segmentIndex: number; distanceIntoSegment: number }

export const ROUTE_START_PROGRESS: RouteProgress = { segmentIndex: 0, distanceIntoSegment: 0 }

// Walks a phone `stepMeters` further along `route` from wherever `progress` left off last tick,
// the route equivalent of the single-target "step towards the convergence point" logic in
// PhoneSimulator's tick(). Multiple ticks' worth of distance can cross several short segments in
// one call, same as a single tick can already overshoot short gaps between GPX trackpoints.
export function advanceAlongRoute(
  route: LatLon[],
  progress: RouteProgress,
  stepMeters: number,
): { position: LatLon; heading: number | null; progress: RouteProgress; arrived: boolean } {
  const lastIndex = route.length - 1
  if (lastIndex <= 0) {
    return { position: route[0], heading: null, progress: ROUTE_START_PROGRESS, arrived: true }
  }

  let segmentIndex = Math.min(progress.segmentIndex, lastIndex - 1)
  let distanceIntoSegment = progress.distanceIntoSegment
  let remaining = stepMeters

  while (remaining > 0 && segmentIndex < lastIndex) {
    const a = route[segmentIndex]
    const b = route[segmentIndex + 1]
    const segLen = distanceMeters(a.lat, a.lon, b.lat, b.lon)
    const toGo = Math.max(segLen - distanceIntoSegment, 0)

    if (segLen === 0 || remaining < toGo) {
      distanceIntoSegment += remaining
      remaining = 0
    } else {
      remaining -= toGo
      segmentIndex += 1
      distanceIntoSegment = 0
    }
  }

  const arrived = segmentIndex >= lastIndex
  if (arrived) {
    return { position: route[lastIndex], heading: null, progress: ROUTE_START_PROGRESS, arrived: true }
  }

  const a = route[segmentIndex]
  const b = route[segmentIndex + 1]
  const bearing = computeBearing(a.lat, a.lon, b.lat, b.lon)
  const position = destinationPoint(a.lat, a.lon, bearing, distanceIntoSegment)

  return { position, heading: bearing, progress: { segmentIndex, distanceIntoSegment }, arrived: false }
}
