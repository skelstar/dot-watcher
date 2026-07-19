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

// How many consecutive readings with a real heading are needed after a null-heading reading
// before the warning clears. A single good reading right after a glitch isn't enough evidence
// the GPS has actually recovered — erratic readings often bounce good/bad/good before settling.
export const GPS_JUMP_CLEAR_STREAK = 3

// Whether a runner's GPS is *currently* unreliable — the device couldn't determine a heading
// (CoreLocation reports heading as null when its course confidence is too low, which tends to
// coincide with the position itself being untrustworthy) recently enough that fewer than
// GPS_JUMP_CLEAR_STREAK readings with a real heading have followed since. Walks the runner's
// whole history each call rather than storing a running streak, since byRunner is already the
// source of truth and re-deriving keeps this a pure function of the data, not stateful.
function currentSignalLossForRunner(positions: RunnerPosition[]): { timestamp: string } | null {
  let lastLoss: { timestamp: string } | null = null
  let goodStreak = 0

  for (const pos of positions) {
    if (pos.heading === null) {
      lastLoss = { timestamp: pos.timestamp }
      goodStreak = 0
    } else {
      goodStreak++
    }
  }

  return lastLoss !== null && goodStreak < GPS_JUMP_CLEAR_STREAK ? lastLoss : null
}

// Whether any runner currently has unreliable GPS (see currentSignalLossForRunner). When
// multiple runners are affected at once, returns whichever lost heading most recently.
export function findGpsSignalLoss(
  byRunner: Map<string, RunnerPosition[]>,
): { runnerName: string; timestamp: string } | null {
  let latest: { runnerName: string; timestamp: string } | null = null

  for (const [runnerName, positions] of byRunner) {
    const loss = currentSignalLossForRunner(positions)
    if (!loss) continue
    if (latest === null || new Date(loss.timestamp).getTime() > new Date(latest.timestamp).getTime()) {
      latest = { runnerName, ...loss }
    }
  }

  return latest
}
