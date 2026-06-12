export interface Position {
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
}

export function compressToMinutes(positions: Position[]): Position[] {
  if (positions.length === 0) return []
  const result: Position[] = [positions[0]]
  for (let i = 1; i < positions.length; i++) {
    const lastMs = new Date(result[result.length - 1].timestamp).getTime()
    const currMs = new Date(positions[i].timestamp).getTime()
    if (currMs - lastMs >= 60_000) result.push(positions[i])
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
