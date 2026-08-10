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
    nextExpectedAt: obj.nextExpectedAt ?? obj.NextExpectedAt ?? null,
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

// Every runner currently affected by GPS signal loss (see currentSignalLossForRunner), so both
// the summary toast and each runner's own marker badge can reflect the same "currently degraded"
// state rather than each re-deriving it differently.
export function findGpsSignalLoss(byRunner: Map<string, RunnerPosition[]>): Set<string> {
  const affected = new Set<string>()
  for (const [runnerName, positions] of byRunner) {
    if (currentSignalLossForRunner(positions)) affected.add(runnerName)
  }
  return affected
}

// A position update is expected roughly every 15s (see the iOS tracking interval), so a gap much
// longer than that at the current playhead means the runner has genuinely dropped out mid-track
// rather than merely being between two normal updates. Fallback only — used for runners/positions
// with no `nextExpectedAt` (older clients, or recordings from before this field existed). See
// GRACE_MS below for runners that do report their own cadence.
export const MISSING_GAP_MS = 60_000

// Slack added on top of a runner's self-reported `nextExpectedAt` before treating them as
// genuinely gapped, rather than just running slightly behind their own stated cadence (network
// jitter, a slow satellite handshake, clock skew between phone and browser). Mirrors the
// server-side grace buffer described in .ai/plans/POST-nextExpectedAt.md ("Should be a shared
// constant so client 'due now' state and server 'not yet overdue' state agree") — the server
// doesn't expose a derived status yet (still an open item there), so this is this client's own
// value until that lands; keep the two in sync if/when the server-side buffer is added.
export const GRACE_MS = 30_000

// Runners with no location record covering the current playhead: their last known position at
// or before cutoffMs is older than expected — i.e. their position at this instant is unknown,
// not just old. Deliberately distinct from a runner who simply hasn't reported yet
// (positionsAtCutoff already omits those, since there's no "before" position at all yet).
//
// Uses each runner's own self-reported `nextExpectedAt` (+ GRACE_MS) when available, so a
// satellite runner posting every 90s isn't flagged as missing every cycle the way a single fixed
// MISSING_GAP_MS threshold would (either too tight for slow cadences or too loose for fast ones —
// see the design note in useSessionTimeline.ts above PHONE_SEND_INTERVAL_MS). Falls back to
// MISSING_GAP_MS after the last position without a `nextExpectedAt` at all.
//
// Doesn't require a later position to "prove" the runner came back — during forward playback of
// a recording, ensureCovered only ever fetches data behind the current scrub position (see
// useSessionTimeline.ts), so data confirming a runner's return is essentially never cached ahead
// of time. A runner who has genuinely stopped for good will keep reading as missing indefinitely,
// the same as a mid-track gap does until fresh data arrives — this matches how the stationary
// dot already treats "no new data" for live sessions.
export function findRunnersWithGap(
  byRunner: Map<string, RunnerPosition[]>,
  cutoffMs: number,
): Set<string> {
  const affected = new Set<string>()
  for (const [runnerName, positions] of byRunner) {
    let before: RunnerPosition | null = null
    for (const pos of positions) {
      const ts = new Date(pos.timestamp).getTime()
      if (ts > cutoffMs) break
      before = pos
    }
    if (!before) continue

    const overdueAt = before.nextExpectedAt
      ? new Date(before.nextExpectedAt).getTime() + GRACE_MS
      : new Date(before.timestamp).getTime() + MISSING_GAP_MS
    if (cutoffMs > overdueAt) affected.add(runnerName)
  }
  return affected
}

// The last known position timestamp for each runner at or before cutoffMs, in ms since epoch —
// lets the "Missing location" label say *when* the runner was last seen, not just that they are.
// Uses the same "before" walk as findRunnersWithGap, but keyed by every runner with a position at
// all (not just the ones currently missing), since the caller decides who to show it for.
export function findLastSeenMs(
  byRunner: Map<string, RunnerPosition[]>,
  cutoffMs: number,
): Map<string, number> {
  const lastSeen = new Map<string, number>()
  for (const [runnerName, positions] of byRunner) {
    let before: RunnerPosition | null = null
    for (const pos of positions) {
      const ts = new Date(pos.timestamp).getTime()
      if (ts > cutoffMs) break
      before = pos
    }
    if (before) lastSeen.set(runnerName, new Date(before.timestamp).getTime())
  }
  return lastSeen
}

// Wall-clock time of day, e.g. "10:42:13 AM" — shared by ReplayControls' scrubber readout and
// Legend's "Missing since" label so both render a timestamp the same way.
export function formatTimeOfDay(ms: number): string {
  return new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' })
}

// One of the three visual states from .ai/plans/POST-nextExpectedAt.md: counting down normally,
// within the shared grace window right at/just past nextExpectedAt, or genuinely overdue (past
// the same overdueAt threshold findRunnersWithGap uses to flag a gap).
export type CountdownStatus = 'counting-down' | 'due' | 'overdue'

export interface RunnerCountdown {
  status: CountdownStatus
  remainingMs: number // ms until nextExpectedAt; negative once past it (0 while 'counting-down')
  intervalMs: number // this runner's own reporting cadence (nextExpectedAt - timestamp) — lets a
  // ring-style display compute elapsed fraction the same way iOS's PostCountdownRing does
}

// Per-runner self-reported countdown to their next expected post, for runners whose current
// cadence is slower than normal (e.g. on satellite) — see findRunnersWithGap for why a runner's
// own nextExpectedAt is trusted over one fixed assumption. Deliberately omits runners on normal
// cadence: a 15s countdown resets near-instantly and is pure visual noise (per the plan's UI
// note — a live countdown makes sense as a reserved/list-view detail, not on every dot). Also
// omits runners with no nextExpectedAt at all (older clients) — there's nothing to count down to.
export function findAdaptiveCountdowns(
  byRunner: Map<string, RunnerPosition[]>,
  cutoffMs: number,
  normalIntervalMs: number,
): Map<string, RunnerCountdown> {
  const countdowns = new Map<string, RunnerCountdown>()
  for (const [runnerName, positions] of byRunner) {
    let before: RunnerPosition | null = null
    for (const pos of positions) {
      const ts = new Date(pos.timestamp).getTime()
      if (ts > cutoffMs) break
      before = pos
    }
    if (!before?.nextExpectedAt) continue

    const timestampMs = new Date(before.timestamp).getTime()
    const nextExpectedAtMs = new Date(before.nextExpectedAt).getTime()
    const intervalMs = nextExpectedAtMs - timestampMs
    if (intervalMs <= normalIntervalMs) continue // normal cadence — skip

    const remainingMs = nextExpectedAtMs - cutoffMs
    const status: CountdownStatus =
      remainingMs > 0 ? 'counting-down' : cutoffMs <= nextExpectedAtMs + GRACE_MS ? 'due' : 'overdue'
    countdowns.set(runnerName, { status, remainingMs, intervalMs })
  }
  return countdowns
}

// Below this, a countdown reads as plain seconds ("42s"); at or above, as "M:SS" (e.g. "2:29") —
// matches this runner's own cadence being long enough that seconds alone stop being the natural
// unit (satellite's 90s cadence is the case this exists for). Used by Legend's countdown ring.
export const MINUTE_FORMAT_THRESHOLD_MS = 90_000

// "42s" below MINUTE_FORMAT_THRESHOLD_MS, "M:SS" at or above it (e.g. "2:29").
export function formatCountdownSeconds(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000))
  if (remainingMs < MINUTE_FORMAT_THRESHOLD_MS) return `${totalSeconds}s`
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${minutes}:${String(seconds).padStart(2, '0')}`
}

const EARTH_RADIUS_M = 6_371_000

// Great-circle distance between two lat/lon points, in metres.
function haversineMetres(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }): number {
  const toRad = (deg: number) => (deg * Math.PI) / 180
  const dLat = toRad(b.latitude - a.latitude)
  const dLon = toRad(b.longitude - a.longitude)
  const lat1 = toRad(a.latitude)
  const lat2 = toRad(b.latitude)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) ** 2
  return 2 * EARTH_RADIUS_M * Math.asin(Math.sqrt(h))
}

// Window to look back over, and the displacement threshold within it, to call a runner
// "sleeping". With reports every ~15s, a 30s window usually holds only 2-3 points, so the
// distance threshold is raised above the 8m used for a longer window — otherwise a single noisy
// GPS fix could flip a genuinely-stationary runner in and out of the sleeping state.
export const SLEEPING_WINDOW_MS = 30_000
export const SLEEPING_DISTANCE_M = 15

// Runners who are actively reporting (they have a position at or before cutoffMs) but have
// barely moved over the last SLEEPING_WINDOW_MS of reports — e.g. waiting at an aid station.
// Distance-based rather than time-since-update based, so it stays independent of "missing"
// (findRunnersWithGap), which is purely about absence of data, not presence of motion.
export function findSleepingRunners(
  byRunner: Map<string, RunnerPosition[]>,
  cutoffMs: number,
): Set<string> {
  const affected = new Set<string>()
  for (const [runnerName, positions] of byRunner) {
    const upTo = positions.filter(p => new Date(p.timestamp).getTime() <= cutoffMs)
    if (upTo.length === 0) continue
    const latest = upTo[upTo.length - 1]
    const windowStart = new Date(latest.timestamp).getTime() - SLEEPING_WINDOW_MS
    const inWindow = upTo.filter(p => new Date(p.timestamp).getTime() >= windowStart)
    if (inWindow.length < 2) continue // not enough reports yet to judge movement

    let maxDistance = 0
    for (const p of inWindow) {
      maxDistance = Math.max(maxDistance, haversineMetres(latest, p))
    }
    if (maxDistance <= SLEEPING_DISTANCE_M) affected.add(runnerName)
  }
  return affected
}
