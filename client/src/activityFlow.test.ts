import assert from 'node:assert/strict'
import test from 'node:test'
import {
  LAYOUT,
  buildFlow,
  dayRange,
  formatDuration,
  layoutFlow,
  timeForX,
  timeScale,
  timeTicks,
  xForTime,
  type ActivityEventDto,
  type RegisterEntryDto,
} from './activityFlow.ts'

const REGISTER: RegisterEntryDto[] = [
  { kind: 'run_started', label: 'Started a Go run', shows: 'line', status: 'recording' },
  { kind: 'run_stopped', label: 'Stopped the run', shows: 'line', status: 'recording' },
  { kind: 'watch_link_made', label: 'Shared a watch link', shows: 'line', status: 'recording' },
  { kind: 'watch_link_opened', label: 'Someone watched', shows: 'branch', status: 'recording', branchFrom: 'watch_link_made' },
]

let nextId = 1
function event(kind: string, at: string, overrides: Partial<ActivityEventDto> = {}): ActivityEventDto {
  return { id: nextId++, at: `2026-11-19T${at}Z`, userId: 'alice', kind, runId: 'run-1', ...overrides }
}

test('a Go run is three stops, with the run going from start to stop', () => {
  const flow = buildFlow([
    event('run_started', '18:32:13'),
    event('watch_link_made', '18:33:45'),
    event('run_stopped', '18:35:07', { detail: { durationSeconds: 174 } }),
  ], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.label), ['Started a Go run', 'Shared a watch link', 'Stopped the run'])
  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: 2, runId: 'run-1' }])
  assert.equal(flow.stops[2].note, '2 min 54 s')
  assert.deepEqual(flow.branches, [])
})

test('watch opens hang off the watch link they belong to, counted', () => {
  const flow = buildFlow([
    event('run_started', '18:32:13'),
    event('watch_link_made', '18:33:45'),
    event('watch_link_opened', '18:34:13'),
    event('watch_link_opened', '18:34:40'),
    event('run_stopped', '18:35:07'),
  ], REGISTER)

  assert.equal(flow.stops.length, 3)
  assert.equal(flow.branches.length, 1)
  assert.equal(flow.branches[0].fromIndex, 1)
  assert.equal(flow.branches[0].count, 2)
  assert.equal(flow.branches[0].firstAt, '2026-11-19T18:34:13Z')
})

test('watch viewings add up their lengths, and one still going has no end yet', () => {
  const flow = buildFlow([
    event('watch_link_made', '18:33:45'),
    event('watch_link_opened', '18:34:00', { detail: { watchedSeconds: 120 } }),
    event('watch_link_opened', '18:40:00', { detail: { watchedSeconds: 30 } }),
    event('watch_link_opened', '18:45:00'),
  ], REGISTER)

  const branch = flow.branches[0]
  assert.equal(branch.totalSeconds, 150)
  assert.equal(branch.spans[0].end! - branch.spans[0].start, 120_000)
  assert.equal(branch.spans[2].end, null)

  const scale = timeScale([{ at: '2026-11-19T18:30:00Z' }, { at: '2026-11-19T18:50:00Z' }], 1000)!
  const placed = layoutFlow(flow, scale).branches[0]
  assert.equal(placed.spans.length, 3)
  assert.ok(placed.spans[0].right > placed.spans[0].left)
  assert.equal(placed.spans[2].ongoing, true)
})

test('a branch picks the watch link of its own run', () => {
  const flow = buildFlow([
    event('run_started', '10:00:00', { runId: 'a' }),
    event('watch_link_made', '10:01:00', { runId: 'a' }),
    event('run_stopped', '10:10:00', { runId: 'a' }),
    event('run_started', '11:00:00', { runId: 'b' }),
    event('watch_link_opened', '11:05:00', { runId: 'a' }),
  ], REGISTER)

  assert.equal(flow.branches[0].fromIndex, 1)
})

test('a branch with nothing to hang off becomes a stop', () => {
  const flow = buildFlow([event('watch_link_opened', '08:00:00')], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.kind), ['watch_link_opened'])
  assert.deepEqual(flow.branches, [])
})

test('a run still going stays open to the end of the line', () => {
  const flow = buildFlow([event('run_started', '18:32:13'), event('watch_link_made', '18:33:45')], REGISTER)

  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: null, runId: 'run-1' }])
})

test('a stop whose start was before the range runs from the start of the line', () => {
  const flow = buildFlow([event('watch_link_made', '00:10:00'), event('run_stopped', '00:20:00')], REGISTER)

  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: 1, runId: 'run-1' }])
})

test('an unknown kind is a stop labelled with its kind', () => {
  const flow = buildFlow([event('something_new', '09:00:00')], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.label), ['something_new'])
})

test('events are put in time order', () => {
  const flow = buildFlow([event('run_stopped', '18:35:07'), event('run_started', '18:32:13')], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.kind), ['run_started', 'run_stopped'])
})

test('in order, stops are evenly spaced and branches sit under their stop', () => {
  const flow = buildFlow([
    event('run_started', '18:32:13'),
    event('watch_link_made', '18:33:45'),
    event('watch_link_opened', '18:34:13'),
    event('run_stopped', '18:35:07'),
  ], REGISTER)

  const layout = layoutFlow(flow, null)

  assert.deepEqual(layout.stopX, [LAYOUT.startX, LAYOUT.startX + LAYOUT.spacing, LAYOUT.startX + 2 * LAYOUT.spacing])
  assert.deepEqual(layout.stopRow, [0, 0, 0])
  assert.equal(layout.branches[0].x, layout.stopX[1])
  assert.deepEqual(layout.runs, [{ left: layout.stopX[0], right: layout.stopX[2] }])
})

test('by time, two lines on one scale put the same moment at the same x', () => {
  const a = [event('run_started', '09:00:00', { userId: 'a' }), event('run_stopped', '09:10:00', { userId: 'a' })]
  const b = [event('run_started', '09:05:00', { userId: 'b' }), event('run_stopped', '09:10:00', { userId: 'b' })]
  const scale = timeScale([...a, ...b], 1000)!

  const layoutA = layoutFlow(buildFlow(a, REGISTER), scale)
  const layoutB = layoutFlow(buildFlow(b, REGISTER), scale)

  assert.equal(layoutA.stopX[1], layoutB.stopX[1])
  assert.ok(layoutA.stopX[0] < layoutB.stopX[0])
  // 09:05 is halfway between 09:00 and 09:10.
  assert.ok(Math.abs(layoutB.stopX[0] - (layoutA.stopX[0] + layoutA.stopX[1]) / 2) < 0.001)
})

test('by time, labels and branches too close together drop a row', () => {
  const flow = buildFlow([
    event('run_started', '09:00:00'),
    event('watch_link_made', '09:00:05'),
    event('watch_link_opened', '09:00:06'),
    event('run_stopped', '09:30:00'),
  ], REGISTER)
  const scale = timeScale([{ at: '2026-11-19T09:00:00Z' }, { at: '2026-11-19T09:30:00Z' }], 1000)!

  const layout = layoutFlow(flow, scale)

  assert.deepEqual(layout.stopRow, [0, 1, 0])
  assert.equal(layout.labelRows, 2)
  assert.equal(layout.branches[0].row, 0)
})

test('a time scale over a single moment still spans a minute', () => {
  const scale = timeScale([{ at: '2026-11-19T09:00:00Z' }], 1000)!

  assert.ok(scale.end - scale.start >= 60_000)
})

test('gridlines are spaced out and use seconds only for short steps', () => {
  const scale = { start: Date.parse('2026-11-19T09:00:00Z'), end: Date.parse('2026-11-19T10:00:00Z'), width: 1000 }

  const ticks = timeTicks(scale)

  assert.ok(ticks.length >= 2)
  for (let i = 1; i < ticks.length; i++) assert.ok(ticks[i].x - ticks[i - 1].x >= 90)
  assert.ok(ticks.every(t => !t.withSeconds))
})

test('the app being away runs from put away to opened again', () => {
  const flow = buildFlow([
    event('app_opened', '09:00:00'),
    event('run_started', '09:00:10'),
    event('app_backgrounded', '09:00:20'),
    event('app_opened', '09:10:00'),
    event('app_backgrounded', '09:20:00'),
  ], REGISTER)

  assert.deepEqual(flow.away, [{ fromIndex: 2, toIndex: 3 }, { fromIndex: 4, toIndex: null }])
  assert.equal(layoutFlow(flow, null).away.length, 2)
})

test('a run knows its id, and stops know they are test', () => {
  const flow = buildFlow([
    event('run_started', '09:00:00', { runId: 'r1', isTest: true }),
    event('run_stopped', '09:10:00', { runId: 'r1', isTest: true }),
  ], REGISTER)

  assert.equal(flow.runs[0].runId, 'r1')
  assert.deepEqual(flow.stops.map(s => s.isTest), [true, true])
})

test('a time at x and the x of a time agree', () => {
  const scale = { start: Date.parse('2026-11-19T09:00:00Z'), end: Date.parse('2026-11-19T10:00:00Z'), width: 1000 }
  const at = Date.parse('2026-11-19T09:20:00Z')

  assert.ok(Math.abs(timeForX(scale, xForTime(scale, at)) - at) < 1)
})

test('durations read naturally', () => {
  assert.equal(formatDuration(42), '42 s')
  assert.equal(formatDuration(174), '2 min 54 s')
  assert.equal(formatDuration(600), '10 min')
  assert.equal(formatDuration(3_900), '1 h 5 min')
})

test('a day is one local day long', () => {
  const { from, to } = dayRange('2026-11-19')

  assert.equal(new Date(from).getDate(), 19)
  assert.equal(new Date(from).getHours(), 0)
  assert.equal(Date.parse(to) - Date.parse(from) >= 23 * 3_600_000, true)
})
