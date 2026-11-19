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
}

export interface FlowBranch {
  // The stop it hangs off.
  fromIndex: number
  kind: string
  label: string
  // Branches of the same kind off the same stop are drawn as one: "Someone watched ×2".
  count: number
  firstAt: string
}

export interface FlowRun {
  fromIndex: number
  // null: still going at the end of the line.
  toIndex: number | null
}

export interface Flow {
  stops: FlowStop[]
  branches: FlowBranch[]
  runs: FlowRun[]
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

  function addStop(event: ActivityEventDto, label: string) {
    const index = stops.length
    const seconds = event.detail?.durationSeconds
    const note = event.kind === 'run_stopped' && typeof seconds === 'number' ? formatDuration(seconds) : null
    stops.push({ index, kind: event.kind, label, at: event.at, note })
    stopEvents.push(event)

    if (event.kind === 'run_started') {
      const run: FlowRun = { fromIndex: index, toIndex: null }
      runs.push(run)
      if (event.runId) openRuns.set(event.runId, run)
    } else if (event.kind === 'run_stopped') {
      const run = (event.runId && openRuns.get(event.runId)) || runs.find(r => r.toIndex === null)
      if (run) {
        run.toIndex = index
        if (event.runId) openRuns.delete(event.runId)
      } else {
        // The start was before this range: the run goes from the start of the line.
        runs.push({ fromIndex: 0, toIndex: index })
      }
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
    const same = branches.find(b => b.fromIndex === fromIndex && b.kind === event.kind)
    if (same) same.count += 1
    else branches.push({ fromIndex, kind: event.kind, label, count: 1, firstAt: event.at })
  }

  return { stops, branches, runs }
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
