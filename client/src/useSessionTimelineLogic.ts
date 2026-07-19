import type { RunnerPosition } from './types.ts'

export interface TimeRange {
  since: number
  until: number
}

// Merges a new range into an already-sorted, non-overlapping list of covered ranges, coalescing
// any ranges it touches or overlaps so the list stays sorted and non-overlapping.
export function mergeRange(ranges: TimeRange[], next: TimeRange): TimeRange[] {
  const merged: TimeRange[] = []
  let { since, until } = next
  let inserted = false

  for (const range of ranges) {
    if (range.until < since) {
      merged.push(range)
    } else if (range.since > until) {
      if (!inserted) {
        merged.push({ since, until })
        inserted = true
      }
      merged.push(range)
    } else {
      since = Math.min(since, range.since)
      until = Math.max(until, range.until)
    }
  }

  if (!inserted) merged.push({ since, until })
  return merged
}

// Whether [since, until] is fully covered by the given sorted, non-overlapping ranges.
export function isRangeCovered(ranges: TimeRange[], since: number, until: number): boolean {
  for (const range of ranges) {
    if (range.since <= since && range.until >= until) return true
  }
  return false
}

export function livePollingError(status: number): string {
  return status === 403
    ? 'No membership for this session.'
    : status === 404
    ? 'Invite not found.'
    : `Live update failed: HTTP ${status}`
}

export function shouldPollLivePositions(
  sessionId: string | null,
  accessToken: string | null,
): boolean {
  return Boolean(sessionId && accessToken)
}

export function shouldPollLivePositionsByInvite(
  inviteCode: string | null,
  accessToken: string | null,
): boolean {
  return Boolean(inviteCode && !accessToken)
}

// Normalises both camelCase (future server output) and PascalCase (legacy NDJSON).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function normalizeUpdate(obj: any): RunnerPosition {
  return {
    runnerName: obj.runnerName ?? obj.RunnerName,
    latitude: obj.latitude ?? obj.Latitude,
    longitude: obj.longitude ?? obj.Longitude,
    heading: obj.heading ?? obj.Heading ?? null,
    timestamp: obj.timestamp ?? obj.Timestamp,
  }
}

export function parseNdjson(text: string): RunnerPosition[] {
  return text
    .split('\n')
    .filter(Boolean)
    .map(line => normalizeUpdate(JSON.parse(line)))
}

// For each runner, the last known position at or before cutoffMs. Runners with no position yet
// at cutoffMs are omitted (they hadn't joined/reported in yet).
export function positionsAtCutoff(
  byRunner: Map<string, RunnerPosition[]>,
  cutoffMs: number,
): RunnerPosition[][] {
  const result: RunnerPosition[][] = []
  for (const arr of byRunner.values()) {
    const upTo = arr.filter(p => new Date(p.timestamp).getTime() <= cutoffMs)
    if (upTo.length === 0) continue
    result.push(upTo.slice(-1))
  }
  return result
}

export function mergeIntoByRunner(
  byRunner: Map<string, RunnerPosition[]>,
  updates: RunnerPosition[],
): Map<string, RunnerPosition[]> {
  const next = new Map(byRunner)
  for (const update of updates) {
    const existing = next.get(update.runnerName) ?? []
    if (existing.some(p => p.timestamp === update.timestamp)) continue
    next.set(update.runnerName, [...existing, update])
  }
  for (const [runner, arr] of next) {
    next.set(runner, [...arr].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()))
  }
  return next
}

// The most recent position timestamp across all runners, or null if nobody has reported in yet.
// Assumes each runner's array is sorted ascending by timestamp, as mergeIntoByRunner maintains.
export function latestActivityMs(byRunner: Map<string, RunnerPosition[]>): number | null {
  let latest: number | null = null
  for (const positions of byRunner.values()) {
    if (positions.length === 0) continue
    const ts = new Date(positions[positions.length - 1].timestamp).getTime()
    if (latest === null || ts > latest) latest = ts
  }
  return latest
}

// The earliest position timestamp across all runners, or null if nobody has reported in yet.
// Used as a fallback run-start when the one-shot server-side recording/meta fetch missed the
// run (e.g. the viewer loaded before the runner's very first ping, so that fetch 404'd and is
// never retried) — the scrubber can still appear once live-polled positions start arriving.
export function earliestActivityMs(byRunner: Map<string, RunnerPosition[]>): number | null {
  let earliest: number | null = null
  for (const positions of byRunner.values()) {
    if (positions.length === 0) continue
    const ts = new Date(positions[0].timestamp).getTime()
    if (earliest === null || ts < earliest) earliest = ts
  }
  return earliest
}

// The more recent of two possibly-unknown timestamps. Used to combine the client's own
// live-polled activity with the server's DB-backed recording metadata, since either source can
// be the only one with data (e.g. the in-memory poll cache is empty right after a server restart).
export function maxOrNull(a: number | null, b: number | null): number | null {
  if (a === null) return b
  if (b === null) return a
  return Math.max(a, b)
}

const EARTH_RADIUS_M = 6_371_000

// Great-circle distance in metres between two lat/lng points.
export function haversineMeters(a: RunnerPosition, b: RunnerPosition): number {
  const toRad = (deg: number) => (deg * Math.PI) / 180
  const dLat = toRad(b.latitude - a.latitude)
  const dLng = toRad(b.longitude - a.longitude)
  const lat1 = toRad(a.latitude)
  const lat2 = toRad(b.latitude)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLng / 2) ** 2
  return 2 * EARTH_RADIUS_M * Math.asin(Math.sqrt(h))
}

// Above this implied speed between two consecutive pings, treat the jump as an erratic/unreliable
// GPS reading rather than real movement. 30 km/h is well above any running pace (even a fast
// trail km is ~3min/km = 20km/h) but far below what a real jump in position over one poll
// interval would need to be to be genuine — so this catches a single bad reading without
// tripping on a strong sprint.
export const GPS_JUMP_SPEED_KMH = 30

// How many consecutive plausible transitions are needed after a jump before the warning clears.
// A single good reading right after a glitch isn't enough evidence the GPS has actually
// recovered — erratic readings often bounce good/bad/good before settling.
export const GPS_JUMP_CLEAR_STREAK = 3

// Null when the pair is a plausible move; otherwise the implied speed in km/h, for the warning
// copy. Two pings at (or reported as) the same instant are treated as a jump only if they're
// also apart in space — a zero/near-zero time delta can't imply a finite speed.
export function impliedSpeedKmh(from: RunnerPosition, to: RunnerPosition): number | null {
  const dtMs = new Date(to.timestamp).getTime() - new Date(from.timestamp).getTime()
  if (dtMs <= 0) return null
  const metres = haversineMeters(from, to)
  const kmh = (metres / 1000) / (dtMs / 3_600_000)
  return kmh
}

export function isGpsJump(from: RunnerPosition, to: RunnerPosition): boolean {
  const kmh = impliedSpeedKmh(from, to)
  return kmh !== null && kmh > GPS_JUMP_SPEED_KMH
}

// Whether a runner's GPS is *currently* erratic — a jump happened recently enough that fewer
// than GPS_JUMP_CLEAR_STREAK plausible transitions have followed it since. Walks the runner's
// whole history each call rather than storing a running streak, since byRunner is already the
// source of truth and re-deriving keeps this a pure function of the data, not stateful.
function currentJumpForRunner(
  positions: RunnerPosition[],
): { timestamp: string; speedKmh: number } | null {
  let lastJump: { timestamp: string; speedKmh: number } | null = null
  let goodStreak = 0

  for (let i = 1; i < positions.length; i++) {
    const speedKmh = impliedSpeedKmh(positions[i - 1], positions[i])
    if (speedKmh !== null && speedKmh > GPS_JUMP_SPEED_KMH) {
      lastJump = { timestamp: positions[i].timestamp, speedKmh }
      goodStreak = 0
    } else {
      goodStreak++
    }
  }

  return lastJump !== null && goodStreak < GPS_JUMP_CLEAR_STREAK ? lastJump : null
}

// Whether any runner currently has erratic GPS (see currentJumpForRunner). When multiple runners
// are affected at once, returns whichever jumped most recently.
export function findLatestGpsJump(
  byRunner: Map<string, RunnerPosition[]>,
): { runnerName: string; timestamp: string; speedKmh: number } | null {
  let latest: { runnerName: string; timestamp: string; speedKmh: number } | null = null

  for (const [runnerName, positions] of byRunner) {
    const jump = currentJumpForRunner(positions)
    if (!jump) continue
    if (latest === null || new Date(jump.timestamp).getTime() > new Date(latest.timestamp).getTime()) {
      latest = { runnerName, ...jump }
    }
  }

  return latest
}
