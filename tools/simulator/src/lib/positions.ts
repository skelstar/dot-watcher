export interface Position {
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
}

// Reduces positions to one per minute, using the fix closest to each HH:MM:00 boundary.
export function compressToMinutes(positions: Position[]): Position[] {
  if (positions.length === 0) return []
  const byMinute = new Map<number, { pos: Position; dist: number }>()
  for (const p of positions) {
    const ms = new Date(p.timestamp).getTime()
    const nearestMs = Math.round(ms / 60_000) * 60_000
    const dist = Math.abs(ms - nearestMs)
    const existing = byMinute.get(nearestMs)
    if (!existing || dist < existing.dist) {
      byMinute.set(nearestMs, { pos: p, dist })
    }
  }
  return [...byMinute.entries()]
    .sort(([a], [b]) => a - b)
    .map(([minuteMs, { pos }]) => ({ ...pos, timestamp: new Date(minuteMs).toISOString() }))
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
