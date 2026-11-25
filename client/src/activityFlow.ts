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
  // Drawn in place of the label, to save room; the label and time show on hover.
  icon?: string | null
}

export interface ActivityDotDto {
  userId: string
  displayName: string
  username: string
  eventCount: number
  // The other dots in the same list who are their crew (as crew is now). Absent from an older server.
  crewWith?: string[] | null
}

// ---- Crew --------------------------------------------------------------------------------------------
// Dots who are crew are shown together, and each line shows when its crew were out at the same
// time as it (both with a Go run going).

// The shown dots in crew groups: each group is everyone linked by crew, in the order the dots came.
// A dot with no crew among them is a group of one.
export function crewGroups(dots: ActivityDotDto[]): ActivityDotDto[][] {
  const byId = new Map(dots.map(d => [d.userId, d]))
  const placed = new Set<string>()
  const groups: ActivityDotDto[][] = []
  for (const dot of dots) {
    if (placed.has(dot.userId)) continue
    const group: ActivityDotDto[] = []
    const queue = [dot]
    placed.add(dot.userId)
    while (queue.length > 0) {
      const next = queue.shift()!
      group.push(next)
      for (const id of next.crewWith ?? []) {
        const mate = byId.get(id)
        if (mate && !placed.has(id)) { placed.add(id); queue.push(mate) }
      }
    }
    groups.push(group.sort((a, b) => dots.indexOf(a) - dots.indexOf(b)))
  }
  return groups
}

// When a line had a Go run going, in ms. A run still going at the end runs to `endOfLine`.
export function runIntervals(flow: Flow, endOfLine: number): { start: number; end: number }[] {
  return flow.runs.map(run => ({
    start: Date.parse(flow.stops[run.fromIndex].at),
    end: run.toIndex === null ? endOfLine : Date.parse(flow.stops[run.toIndex].at),
  }))
}

// Where two sets of intervals overlap.
export function overlaps(a: { start: number; end: number }[], b: { start: number; end: number }[]): { start: number; end: number }[] {
  const out: { start: number; end: number }[] = []
  for (const x of a)
    for (const y of b) {
      const start = Math.max(x.start, y.start)
      const end = Math.min(x.end, y.end)
      if (end > start) out.push({ start, end })
    }
  return out.sort((p, q) => p.start - q.start)
}

export interface FlowStop {
  index: number
  kind: string
  label: string
  at: string
  // e.g. "2 min 54 s" on a stopped run.
  note: string | null
  isTest: boolean
  // From the register: drawn instead of the label and time, which show on hover.
  icon: string | null
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
  // From the register: a small pill with the icon instead of a card.
  icon: string | null
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

  function addStop(event: ActivityEventDto, label: string, icon: string | null) {
    const index = stops.length
    const seconds = event.detail?.durationSeconds
    const note = event.kind === 'run_stopped' && typeof seconds === 'number' ? formatDuration(seconds) : null
    stops.push({ index, kind: event.kind, label, at: event.at, note, isTest: event.isTest === true, icon })
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
      addStop(event, label, entry?.icon ?? null)
      continue
    }

    const fromIndex = branchStop(stopEvents, event, entry.branchFrom ?? null)
    if (fromIndex === null) {
      // Nothing on the line yet to hang it off (the link was shared before this range): a stop of its own.
      addStop(event, label, entry?.icon ?? null)
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
      branches.push({
        fromIndex, kind: event.kind, label, count: 1, firstAt: event.at, spans: [span], totalSeconds: seconds ?? 0,
        icon: entry.icon ?? null,
      })
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
  labelWidth: 146,
  branchWidth: 150,
  // An event with an icon (the register's `icon`) is drawn as just the icon, or a small pill for a branch.
  iconLabelWidth: 26,
  iconBranchWidth: 96,
  // A branch card starts this far left of its stem.
  branchOffset: 20,
  rowGap: 8,
} as const

// A shared time scale: `start`..`end` (ms) is drawn `width` px wide. With `pieces`, long quiet stretches
// (no event on any line) are collapsed into narrow breaks; without, it's one straight scale.
export interface TimeScale {
  start: number
  end: number
  width: number
  pieces?: ScalePiece[]
}

// One stretch of a collapsed scale: `start`..`end` (ms) drawn from x0 to x1 (px from the scale's left).
export interface ScalePiece {
  start: number
  end: number
  x0: number
  x1: number
  collapsed: boolean
  // For a break: how long nothing happened, from the event before it to the event after.
  quietMs?: number
}

// When a quiet stretch is collapsed: at least this long, and this wide on screen. `keepPx` of ordinary
// scale is kept either side, so labels around it have room; the break itself is `breakPx` wide.
export const COLLAPSE = { minGapMs: 120_000, minGapPx: 360, keepPx: 80, breakPx: 44 } as const

export interface PlacedBranch {
  branch: FlowBranch
  x: number
  // Its row within its placement (unused on a strip).
  row: number
  // Where it's drawn. A branch with an icon is a strip just under the line (each span along it, the icon
  // at its start); one without an icon is a card below.
  placement: 'strip' | 'cards'
  // Where each of its spans (a viewing) runs along the line.
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
  // Label rows (stop labels), and rows of cards below them.
  labelRows: number
  branchRows: number
}

// Covers every event given, padded a little at each end so the first and last stops aren't on the edge.
// `width` is how wide the whole range would be drawn straight; with `collapse`, quiet stretches then
// shrink to breaks and the scale comes out narrower (its `width` says how wide).
export function timeScale(events: { at: string }[], width: number, collapse = false): TimeScale | null {
  if (events.length === 0) return null
  const times = [...new Set(events.map(e => Date.parse(e.at)))].sort((a, b) => a - b)
  let start = times[0]
  let end = times[times.length - 1]
  if (end - start < 60_000) {
    const middle = (start + end) / 2
    start = middle - 30_000
    end = middle + 30_000
  }
  const pad = (end - start) * 0.03
  start -= pad
  end += pad
  if (!collapse) return { start, end, width }

  const pxPerMs = width / (end - start)
  const keepMs = COLLAPSE.keepPx / pxPerMs
  const pieces: ScalePiece[] = []
  let cursor = start
  let x = 0
  for (let i = 1; i < times.length; i++) {
    const gap = times[i] - times[i - 1]
    const cutFrom = times[i - 1] + keepMs
    const cutTo = times[i] - keepMs
    if (gap < COLLAPSE.minGapMs || gap * pxPerMs < COLLAPSE.minGapPx || cutTo <= cutFrom) continue
    const plain = (cutFrom - cursor) * pxPerMs
    pieces.push({ start: cursor, end: cutFrom, x0: x, x1: x + plain, collapsed: false })
    x += plain
    pieces.push({ start: cutFrom, end: cutTo, x0: x, x1: x + COLLAPSE.breakPx, collapsed: true, quietMs: gap })
    x += COLLAPSE.breakPx
    cursor = cutTo
  }
  if (pieces.length === 0) return { start, end, width }
  const last = (end - cursor) * pxPerMs
  pieces.push({ start: cursor, end, x0: x, x1: x + last, collapsed: false })
  return { start, end, width: x + last, pieces }
}

// A scale `target` px wide overall: with collapsing, the straight width that comes out at about that
// once the quiet stretches have shrunk (breaks are a fixed width, so a few rounds settle it).
export function timeScaleOfWidth(events: { at: string }[], target: number, collapse: boolean): TimeScale | null {
  let scale = timeScale(events, target, collapse)
  // With a given set of breaks the drawn width is straight × (1 − quiet/range) + n × (break + 2 × keep),
  // so the straight width is solved for directly; a round or two settles which gaps are breaks.
  for (let round = 0; round < 4 && scale?.pieces && Math.abs(scale.width - target) > 1; round++) {
    const breaks = scale.pieces.filter(p => p.collapsed)
    const quietMs = breaks.reduce((sum, p) => sum + (p.quietMs ?? 0), 0)
    const fixedPx = breaks.length * (COLLAPSE.breakPx + 2 * COLLAPSE.keepPx)
    const plainShare = 1 - quietMs / (scale.end - scale.start)
    if (plainShare <= 0 || target <= fixedPx) break
    scale = timeScale(events, (target - fixedPx) / plainShare, collapse)
  }
  return scale
}

export function xForTime(scale: TimeScale, at: number): number {
  if (!scale.pieces) return LAYOUT.startX + ((at - scale.start) / (scale.end - scale.start)) * scale.width
  const piece = scale.pieces.find(p => at <= p.end) ?? scale.pieces[scale.pieces.length - 1]
  return LAYOUT.startX + piece.x0 + ((at - piece.start) / (piece.end - piece.start)) * (piece.x1 - piece.x0)
}

// The other way round from xForTime: the moment at `x` px along a line, held to the scale's range.
export function timeForX(scale: TimeScale, x: number): number {
  const along = x - LAYOUT.startX
  const share = (from: number, to: number) => (to > from ? Math.min(1, Math.max(0, (along - from) / (to - from))) : 0)
  if (!scale.pieces) return scale.start + share(0, scale.width) * (scale.end - scale.start)
  const piece = scale.pieces.find(p => along <= p.x1) ?? scale.pieces[scale.pieces.length - 1]
  return piece.start + share(piece.x0, piece.x1) * (piece.end - piece.start)
}

// A stretch of the day picked by dragging across a line, to zoom in to.
export interface TimeWindow {
  start: number
  end: number
}

// The shortest stretch a drag zooms in to, so a tiny drag doesn't blow seconds up to the whole panel.
export const MIN_WINDOW_MS = 10_000

// The window between two moments picked on a line, in either order, widened about its middle to at
// least MIN_WINDOW_MS.
export function windowBetween(a: number, b: number): TimeWindow {
  const start = Math.min(a, b)
  const end = Math.max(a, b)
  if (end - start >= MIN_WINDOW_MS) return { start, end }
  const middle = (start + end) / 2
  return { start: middle - MIN_WINDOW_MS / 2, end: middle + MIN_WINDOW_MS / 2 }
}

// Exactly the window, `width` px wide, straight.
export function windowScale(window: TimeWindow, width: number): TimeScale {
  return { start: window.start, end: window.end, width }
}

// How wide a zoomed-in line may get, so a tiny window on a long day stays within what browsers can draw.
export const MAX_ZOOM_WIDTH = 2_000_000

// Zoomed in: the whole of the events' range, straight (no breaks), at the rate that makes the window
// `viewWidth` px wide, so the window fills the panel and the rest of the day scrolls either side of it.
// Capped at MAX_ZOOM_WIDTH, so the window then comes out a little wider than asked.
export function zoomedScale(events: { at: string }[], window: TimeWindow, viewWidth: number): TimeScale {
  const day = timeScale(events, viewWidth)
  const start = Math.min(day?.start ?? window.start, window.start)
  const end = Math.max(day?.end ?? window.end, window.end)
  const width = Math.min(MAX_ZOOM_WIDTH, ((end - start) / (window.end - window.start)) * viewWidth)
  return { start, end, width }
}

// Where a collapsed scale's breaks are, in px along a line, with how long nothing happened.
export function timeBreaks(scale: TimeScale): { left: number; right: number; quietMs: number }[] {
  return (scale.pieces ?? [])
    .filter(p => p.collapsed)
    .map(p => ({ left: LAYOUT.startX + p.x0, right: LAYOUT.startX + p.x1, quietMs: p.quietMs ?? p.end - p.start }))
}
// The first row whose last item ends before `left`, taking it up to `right`.
function takeRow(rowEnds: number[], left: number, right: number): number {
  let row = rowEnds.findIndex(end => end + LAYOUT.rowGap <= left)
  if (row === -1) row = rowEnds.length
  rowEnds[row] = right
  return row
}

// How wide a stop's label, or a branch's card, is drawn.
export const labelWidthOf = (stop: FlowStop) => (stop.icon ? LAYOUT.iconLabelWidth : LAYOUT.labelWidth)
export const branchWidthOf = (branch: FlowBranch) => (branch.icon ? LAYOUT.iconBranchWidth : LAYOUT.branchWidth)

export function layoutFlow(flow: Flow, scale: TimeScale): FlowLayout {
  const { startX, branchOffset } = LAYOUT

  const rowsIn = (branches: PlacedBranch[], placement: PlacedBranch['placement']) =>
    Math.max(0, ...branches.filter(b => b.placement === placement).map(b => b.row + 1))
  // A stop with an icon is drawn on the line itself, so it takes no label row.
  const onLine = (stop: FlowStop) => stop.icon !== null

  const stopX = flow.stops.map(stop => xForTime(scale, Date.parse(stop.at)))
  const branchX = flow.branches.map(branch => xForTime(scale, Date.parse(branch.firstAt)))

  // Label rows, in time order; a row keeps what it holds, so a later label can fit a gap.
  const labelRowsTaken: { left: number; right: number }[][] = []
  const placeInRows = (left: number, right: number) => {
    let row = labelRowsTaken.findIndex(taken =>
      taken.every(t => right + LAYOUT.rowGap <= t.left || t.right + LAYOUT.rowGap <= left))
    if (row === -1) row = labelRowsTaken.push([]) - 1
    labelRowsTaken[row].push({ left, right })
    return row
  }
  const stopRow = stopX.map((x, i) => (onLine(flow.stops[i]) ? -1 : placeInRows(x, x + 12 + labelWidthOf(flow.stops[i]))))

  // A branch with an icon goes on the strip under the line; cards get rows of their own below the labels.
  const cardEnds: number[] = []
  const branches: PlacedBranch[] = flow.branches.map((branch, i) => {
    const x = branchX[i]
    const spans = branch.spans.map(span => {
      const left = xForTime(scale, span.start)
      // A length not known yet is drawn as a stub, with a fade to show it may go on.
      const right = span.end === null ? left + 24 : Math.max(xForTime(scale, span.end), left + 3)
      return { left, right, ongoing: span.end === null }
    })
    if (branch.icon !== null) return { branch, x, row: 0, placement: 'strip', spans }
    return { branch, x, row: takeRow(cardEnds, x - branchOffset, x - branchOffset + branchWidthOf(branch)), placement: 'cards', spans }
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
    width: Math.max(
      scaleRight,
      ...stopX.map((x, i) => x + 12 + labelWidthOf(flow.stops[i])),
      ...branches.filter(b => b.placement === 'cards').map(b => b.x - branchOffset + branchWidthOf(b.branch)),
      ...branches.flatMap(b => b.spans.map(s => s.right)),
    ) + 16,
    labelRows: Math.max(1, ...stopRow.map(r => r + 1)),
    branchRows: rowsIn(branches, 'cards'),
  }
}

// Gridlines for a time scale: the smallest step that keeps them at least `minGapPx` apart.
const TICK_STEPS = [10, 30, 60, 120, 300, 600, 900, 1_800, 3_600, 7_200, 10_800, 21_600].map(s => s * 1000)

export function timeTicks(scale: TimeScale, minGapPx = 90): { at: number; x: number; withSeconds: boolean }[] {
  // Collapsed: the ordinary stretches share one rate, and breaks get no gridlines.
  const plain = scale.pieces?.filter(p => !p.collapsed && p.x1 > p.x0) ?? [{ start: scale.start, end: scale.end, x0: 0, x1: scale.width }]
  if (plain.length === 0) return []
  const msPerPx = (plain[0].end - plain[0].start) / (plain[0].x1 - plain[0].x0)
  const step = TICK_STEPS.find(s => s / msPerPx >= minGapPx) ?? TICK_STEPS[TICK_STEPS.length - 1]
  const ticks = []
  for (const piece of plain)
    for (let at = Math.ceil(piece.start / step) * step; at <= piece.end; at += step)
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
