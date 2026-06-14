export interface Position {
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
}

// Reduces positions to one per interval slot, using the fix closest to each boundary.
export function compressToInterval(positions: Position[], intervalSec: number): Position[] {
  if (positions.length === 0) return []
  const intervalMs = intervalSec * 1000
  const bySlot = new Map<number, { pos: Position; dist: number }>()
  for (const p of positions) {
    const ms = new Date(p.timestamp).getTime()
    const nearestMs = Math.round(ms / intervalMs) * intervalMs
    const dist = Math.abs(ms - nearestMs)
    const existing = bySlot.get(nearestMs)
    if (!existing || dist < existing.dist) {
      bySlot.set(nearestMs, { pos: p, dist })
    }
  }
  return [...bySlot.entries()]
    .sort(([a], [b]) => a - b)
    .map(([slotMs, { pos }]) => ({ ...pos, timestamp: new Date(slotMs).toISOString() }))
}

function haversineMeters(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const R = 6_371_000
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLat = toRad(lat2 - lat1)
  const dLon = toRad(lon2 - lon1)
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2
  return 2 * R * Math.asin(Math.sqrt(a))
}

// Removes consecutive positions where the runner hasn't moved at least minMeters.
export function filterByDistance(positions: Position[], minMeters: number): Position[] {
  if (positions.length === 0) return []
  const result: Position[] = [positions[0]]
  for (let i = 1; i < positions.length; i++) {
    const prev = result[result.length - 1]
    if (haversineMeters(prev.latitude, prev.longitude, positions[i].latitude, positions[i].longitude) >= minMeters) {
      result.push(positions[i])
    }
  }
  return result
}

export function computeBearing(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLon = toRad(lon2 - lon1)
  const y = Math.sin(dLon) * Math.cos(toRad(lat2))
  const x =
    Math.cos(toRad(lat1)) * Math.sin(toRad(lat2)) -
    Math.sin(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.cos(dLon)
  return ((Math.atan2(y, x) * 180) / Math.PI + 360) % 360
}
