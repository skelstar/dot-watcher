// Turns one dot's activity events (server: GET /admin/activity) into the admin Activity tab's flow: stops
// along a line, branches hanging off them, and the stretches of line where a Go run was going. Kept apart
// from the component so the rules can be tested. See .ai/plans/activity-flow.md.

export interface ActivityEventDto {
  id: number
  at: string
  userId: string
  kind: string
  otherUserId?: string | null
  client?: string | null
  appVersion?: string | null
  runId?: string | null
  sessionId?: string | null
  detail?: Record<string, unknown> | null
  // Marked as testing by an admin; only sent when test activity is asked for.
  isTest?: boolean
}

// One entry of the activity register (server: Activity/activity-events.json, GET /admin/activity/register).
export interface RegisterEntryDto {
  kind: string
  label: string
  shows: 'line' | 'branch'
  status: 'wanted' | 'recording'
  branchFrom?: string | null
  route?: string | null
  describe?: string | null
  notes?: string | null
}

export interface ActivityDotDto {
  userId: string
  displayName: string
  username: string
  eventCount: number
}

export interface FlowStop {
  index: number
  kind: string
  label: string
  at: string
  // e.g. "2 min 54 s" on a stopped run.
  note: string | null
  isTest: boolean
}

export interface FlowBranch {
  // The stop it hangs off.
  fromIndex: number
  kind: string
  label: string
  // Branches of the same kind off the same stop are drawn as one: "Someone watched ×2".
  count: number
  firstAt: string
  // Each one's time span, in ms; `end` null while its length isn't known yet (a viewing still going).
  spans: { start: number; end: number | null }[]
  // The known lengths added up (detail.watchedSeconds on a watch viewing).
  totalSeconds: number
}

export interface FlowRun {
  fromIndex: number
  // null: still going at the end of the line.
  toIndex: number | null
  // The Go run it is, so it can be marked as test (not set on an away stretch).
  runId?: string | null
}

export interface Flow {
  stops: FlowStop[]
  branches: FlowBranch[]
  runs: FlowRun[]
  // Stretches where the app was in the background, from app_backgrounded to the next app_opened.
  away: FlowRun[]
}

export function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds} s`
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  if (minutes < 60) return rest ? `${minutes} min ${rest} s` : `${minutes} min`
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return mins ? `${hours} h ${mins} min` : `${hours} h`
}

// Events in time order for one dot. A kind the register doesn't know is drawn as a stop with its raw kind.
export function buildFlow(events: ActivityEventDto[], register: RegisterEntryDto[]): Flow {
  const byKind = new Map(register.map(entry => [entry.kind, entry]))
  const sorted = [...events].sort((a, b) => Date.parse(a.at) - Date.parse(b.at) || a.id - b.id)

  const stops: FlowStop[] = []
  const stopEvents: ActivityEventDto[] = []
  const branches: FlowBranch[] = []
  const runs: FlowRun[] = []
  const openRuns = new Map<string, FlowRun>()
  const away: FlowRun[] = []

  function addStop(event: ActivityEventDto, label: string) {
    const index = stops.length
    const seconds = event.detail?.durationSeconds
    const note = event.kind === 'run_stopped' && typeof seconds === 'number' ? formatDuration(seconds) : null
    stops.push({ index, kind: event.kind, label, at: event.at, note, isTest: event.isTest === true })
    stopEvents.push(event)

    if (event.kind === 'run_started') {
      const run: FlowRun = { fromIndex: index, toIndex: null, runId: event.runId ?? null }
      runs.push(run)
      if (event.runId) openRuns.set(event.runId, run)
    } else if (event.kind === 'run_stopped') {
      const run = (event.runId && openRuns.get(event.runId)) || runs.find(r => r.toIndex === null)
      if (run) {
        run.toIndex = index
        if (event.runId) openRuns.delete(event.runId)
      } else {
        // The start was before this range: the run goes from the start of the line.
        runs.push({ fromIndex: 0, toIndex: index, runId: event.runId ?? null })
      }
    } else if (event.kind === 'app_backgrounded') {
      if (!away.some(a => a.toIndex === null)) away.push({ fromIndex: index, toIndex: null })
    } else if (event.kind === 'app_opened') {
      const open = away.find(a => a.toIndex === null)
      if (open) open.toIndex = index
    }
  }

  for (const event of sorted) {
    const entry = byKind.get(event.kind)
    const label = entry?.label ?? event.kind
    if (entry?.shows !== 'branch') {
      addStop(event, label)
      continue
    }

    const fromIndex = branchStop(stopEvents, event, entry.branchFrom ?? null)
    if (fromIndex === null) {
      // Nothing on the line yet to hang it off (the link was shared before this range): a stop of its own.
      addStop(event, label)
      continue
    }
    const watched = event.detail?.watchedSeconds
    const seconds = typeof watched === 'number' ? watched : null
    const start = Date.parse(event.at)
    const span = { start, end: seconds === null ? null : start + seconds * 1000 }
    const same = branches.find(b => b.fromIndex === fromIndex && b.kind === event.kind)
    if (same) {
      same.count += 1
      same.spans.push(span)
      same.totalSeconds += seconds ?? 0
    } else {
      branches.push({ fromIndex, kind: event.kind, label, count: 1, firstAt: event.at, spans: [span], totalSeconds: seconds ?? 0 })
    }
  }

  return { stops, branches, runs, away }
}

// The latest stop of the branch's `branchFrom` kind (on the same run, when both have one), else the latest
// stop before it, else null.
function branchStop(stopEvents: ActivityEventDto[], event: ActivityEventDto, branchFrom: string | null): number | null {
  for (let i = stopEvents.length - 1; i >= 0; i--) {
    const stop = stopEvents[i]
    if (stop.kind !== branchFrom) continue
    if (event.runId && stop.runId && event.runId !== stop.runId) continue
    return i
  }
  return stopEvents.length > 0 ? stopEvents.length - 1 : null
}

// ---- Layout --------------------------------------------------------------------------------------
// Where each part of a flow goes, in px from the left of its line. "order": stops evenly spaced, as a
// story. "time": every line shares one time scale, so lines can be compared; labels and branches that
// would overlap drop to a lower row.

export const LAYOUT = {
  startX: 24,
  spacing: 170,
  labelWidth: 146,
  branchWidth: 150,
  // A branch card starts this far left of its stem.
  branchOffset: 20,
  rowGap: 8,
} as const

// A shared time scale: `start`..`end` (ms) is drawn `width` px wide.
export interface TimeScale {
  start: number
  end: number
  width: number
}

export interface PlacedBranch {
  branch: FlowBranch
  x: number
  row: number
  // By time: where each of its spans (a viewing) runs along the line. Empty in order.
  spans: { left: number; right: number; ongoing: boolean }[]
}

export interface FlowLayout {
  stopX: number[]
  // Label row per stop: 0 sits just under the line.
  stopRow: number[]
  branches: PlacedBranch[]
  runs: { left: number; right: number }[]
  away: { left: number; right: number }[]
  lineLeft: number
  lineRight: number
  width: number
  labelRows: number
  branchRows: number
}

// Covers every event given, padded a little at each end so the first and last stops aren't on the edge.
export function timeScale(events: { at: string }[], width: number): TimeScale | null {
  if (events.length === 0) return null
  const times = events.map(e => Date.parse(e.at))
  let start = Math.min(...times)
  let end = Math.max(...times)
  if (end - start < 60_000) {
    const middle = (start + end) / 2
    start = middle - 30_000
    end = middle + 30_000
  }
  const pad = (end - start) * 0.03
  return { start: start - pad, end: end + pad, width }
}

export function xForTime(scale: TimeScale, at: number): number {
  return LAYOUT.startX + ((at - scale.start) / (scale.end - scale.start)) * scale.width
}

// The other way: the moment at x px along a line, for picking a stretch by dragging.
export function timeForX(scale: TimeScale, x: number): number {
  return scale.start + ((x - LAYOUT.startX) / scale.width) * (scale.end - scale.start)
}

// The first row whose last item ends before `left`, taking it up to `right`.
function takeRow(rowEnds: number[], left: number, right: number): number {
  let row = rowEnds.findIndex(end => end + LAYOUT.rowGap <= left)
  if (row === -1) row = rowEnds.length
  rowEnds[row] = right
  return row
}

export function layoutFlow(flow: Flow, scale: TimeScale | null): FlowLayout {
  const { startX, spacing, labelWidth, branchWidth, branchOffset } = LAYOUT

  if (scale === null) {
    const stopX = flow.stops.map(stop => startX + stop.index * spacing)
    const last = stopX.length > 0 ? stopX[stopX.length - 1] : startX
    const perStop = new Map<number, number>()
    const branches = flow.branches.map(branch => {
      const row = perStop.get(branch.fromIndex) ?? 0
      perStop.set(branch.fromIndex, row + 1)
      return { branch, x: stopX[branch.fromIndex], row, spans: [] }
    })
    return {
      stopX,
      stopRow: stopX.map(() => 0),
      branches,
      runs: flow.runs.map(run => ({
        left: stopX[run.fromIndex],
        right: run.toIndex === null ? last + spacing / 2 : stopX[run.toIndex],
      })),
      away: flow.away.map(span => ({
        left: stopX[span.fromIndex],
        right: span.toIndex === null ? last + spacing / 2 : stopX[span.toIndex],
      })),
      lineLeft: startX,
      lineRight: last,
      width: last + spacing,
      labelRows: 1,
      branchRows: Math.max(0, ...branches.map(b => b.row + 1)),
    }
  }

  const stopX = flow.stops.map(stop => xForTime(scale, Date.parse(stop.at)))
  const labelEnds: number[] = []
  const stopRow = stopX.map(x => takeRow(labelEnds, x, x + 12 + labelWidth))
  const branchEnds: number[] = []
  const branches = flow.branches.map(branch => {
    const x = xForTime(scale, Date.parse(branch.firstAt))
    const spans = branch.spans.map(span => {
      const left = xForTime(scale, span.start)
      // A length not known yet is drawn as a stub, with a fade to show it may go on.
      const right = span.end === null ? left + 24 : Math.max(xForTime(scale, span.end), left + 3)
      return { left, right, ongoing: span.end === null }
    })
    return { branch, x, row: takeRow(branchEnds, x - branchOffset, x - branchOffset + branchWidth), spans }
  })
  const scaleRight = startX + scale.width
  const lineLeft = stopX.length > 0 ? stopX[0] : startX
  const lineRight = stopX.length > 0 ? stopX[stopX.length - 1] : startX
  return {
    stopX,
    stopRow,
    branches,
    runs: flow.runs.map(run => ({
      // A run whose start was before the range begins at the left edge.
      left: run.fromIndex === 0 && flow.stops[0]?.kind !== 'run_started' ? startX : stopX[run.fromIndex],
      right: run.toIndex === null ? scaleRight : stopX[run.toIndex],
    })),
    away: flow.away.map(span => ({
      left: stopX[span.fromIndex],
      right: span.toIndex === null ? scaleRight : stopX[span.toIndex],
    })),
    lineLeft,
    lineRight,
    width: Math.max(scaleRight, ...stopX.map(x => x + 12 + labelWidth), ...branches.map(b => b.x - branchOffset + branchWidth)) + 16,
    labelRows: Math.max(1, ...stopRow.map(r => r + 1)),
    branchRows: Math.max(0, ...branches.map(b => b.row + 1)),
  }
}

// Gridlines for a time scale: the smallest step that keeps them at least `minGapPx` apart.
const TICK_STEPS = [10, 30, 60, 120, 300, 600, 900, 1_800, 3_600, 7_200, 10_800, 21_600].map(s => s * 1000)

export function timeTicks(scale: TimeScale, minGapPx = 90): { at: number; x: number; withSeconds: boolean }[] {
  const msPerPx = (scale.end - scale.start) / scale.width
  const step = TICK_STEPS.find(s => s / msPerPx >= minGapPx) ?? TICK_STEPS[TICK_STEPS.length - 1]
  const ticks = []
  for (let at = Math.ceil(scale.start / step) * step; at <= scale.end; at += step)
    ticks.push({ at, x: xForTime(scale, at), withSeconds: step < 60_000 })
  return ticks
}

// The local start and end of a yyyy-mm-dd day, as instants for the server.
export function dayRange(day: string): { from: string; to: string } {
  const [year, month, date] = day.split('-').map(Number)
  const from = new Date(year, month - 1, date)
  const to = new Date(year, month - 1, date + 1)
  return { from: from.toISOString(), to: to.toISOString() }
}

export function todayString(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}
