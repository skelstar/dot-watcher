import assert from 'node:assert/strict'
import test from 'node:test'
import { buildFlow, dayRange, formatDuration, type ActivityEventDto, type RegisterEntryDto } from './activityFlow.ts'

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
  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: 2 }])
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
  assert.deepEqual(flow.branches, [
    { fromIndex: 1, kind: 'watch_link_opened', label: 'Someone watched', count: 2, firstAt: '2026-11-19T18:34:13Z' },
  ])
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

  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: null }])
})

test('a stop whose start was before the range runs from the start of the line', () => {
  const flow = buildFlow([event('watch_link_made', '00:10:00'), event('run_stopped', '00:20:00')], REGISTER)

  assert.deepEqual(flow.runs, [{ fromIndex: 0, toIndex: 1 }])
})

test('an unknown kind is a stop labelled with its kind', () => {
  const flow = buildFlow([event('something_new', '09:00:00')], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.label), ['something_new'])
})

test('events are put in time order', () => {
  const flow = buildFlow([event('run_stopped', '18:35:07'), event('run_started', '18:32:13')], REGISTER)

  assert.deepEqual(flow.stops.map(s => s.kind), ['run_started', 'run_stopped'])
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
