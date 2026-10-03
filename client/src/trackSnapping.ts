// Routing along the basemap's drawn tracks (the dotted lines on the topo map), so a missed read can
// be placed on the trail the runner was most likely on rather than on a straight line across the
// terrain. Pure geometry — the hook feeds it line coordinates queried from the map.
export type LngLat = [number, number]

interface Edge { to: number; metres: number }

export interface TrackGraph {
  coords: LngLat[]
  edges: Edge[][]
}

const M_PER_DEG_LAT = 111_320
const KEY_PRECISION = 1e5 // ~1m: merges vertices that tiles split at their borders

function metres(a: LngLat, b: LngLat): number {
  const dLat = (b[1] - a[1]) * M_PER_DEG_LAT
  const dLng = (b[0] - a[0]) * M_PER_DEG_LAT * Math.cos(((a[1] + b[1]) / 2) * Math.PI / 180)
  return Math.hypot(dLat, dLng)
}

export function buildTrackGraph(lines: LngLat[][]): TrackGraph {
  const index = new Map<string, number>()
  const coords: LngLat[] = []
  const edges: Edge[][] = []
  const vertex = (c: LngLat): number => {
    const key = `${Math.round(c[0] * KEY_PRECISION)},${Math.round(c[1] * KEY_PRECISION)}`
    let id = index.get(key)
    if (id === undefined) {
      id = coords.length
      index.set(key, id)
      coords.push(c)
      edges.push([])
    }
    return id
  }
  for (const line of lines) {
    for (let i = 1; i < line.length; i++) {
      const a = vertex(line[i - 1])
      const b = vertex(line[i])
      if (a === b) continue
      const m = metres(coords[a], coords[b])
      edges[a].push({ to: b, metres: m })
      edges[b].push({ to: a, metres: m })
    }
  }
  return { coords, edges }
}

function nearestVertex(graph: TrackGraph, p: LngLat, maxMetres: number): number | null {
  let best: number | null = null
  let bestM = maxMetres
  graph.coords.forEach((c, i) => {
    const m = metres(p, c)
    if (m <= bestM) { best = i; bestM = m }
  })
  return best
}

// Shortest path along the tracks from `from` to `to` (each snapped to the nearest track vertex
// within snapMetres). Returns the polyline including the exact endpoints, or null when either end
// is off the tracks or they aren't connected in the loaded data.
export function pathAlongTracks(graph: TrackGraph, from: LngLat, to: LngLat, snapMetres = 40): LngLat[] | null {
  const start = nearestVertex(graph, from, snapMetres)
  const end = nearestVertex(graph, to, snapMetres)
  if (start === null || end === null) return null

  const dist = new Map<number, number>([[start, 0]])
  const prev = new Map<number, number>()
  const done = new Set<number>()
  // Plain array as the queue: graphs here are a viewport's worth of track, so O(n^2) is fine.
  const open = new Set<number>([start])
  while (open.size > 0) {
    let u = -1
    let uD = Infinity
    for (const v of open) {
      const d = dist.get(v)!
      if (d < uD) { u = v; uD = d }
    }
    open.delete(u)
    if (u === end) break
    done.add(u)
    for (const e of graph.edges[u]) {
      if (done.has(e.to)) continue
      const nd = uD + e.metres
      if (nd < (dist.get(e.to) ?? Infinity)) {
        dist.set(e.to, nd)
        prev.set(e.to, u)
        open.add(e.to)
      }
    }
  }
  if (!dist.has(end)) return null

  const path: LngLat[] = []
  for (let v: number | undefined = end; v !== undefined; v = prev.get(v)) path.push(graph.coords[v])
  path.reverse()
  return [from, ...path, to]
}

export function polylineMetres(path: LngLat[]): number {
  let total = 0
  for (let i = 1; i < path.length; i++) total += metres(path[i - 1], path[i])
  return total
}

// The point `fraction` (0..1) of the way along the polyline by distance.
export function pointAlong(path: LngLat[], fraction: number): LngLat {
  const target = polylineMetres(path) * fraction
  let walked = 0
  for (let i = 1; i < path.length; i++) {
    const seg = metres(path[i - 1], path[i])
    if (walked + seg >= target && seg > 0) {
      const f = (target - walked) / seg
      return [path[i - 1][0] + (path[i][0] - path[i - 1][0]) * f, path[i - 1][1] + (path[i][1] - path[i - 1][1]) * f]
    }
    walked += seg
  }
  return path[path.length - 1]
}
