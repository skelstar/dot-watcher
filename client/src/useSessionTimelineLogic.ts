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
