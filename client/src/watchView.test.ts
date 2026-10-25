import assert from 'node:assert/strict'
import test from 'node:test'
import { lastSeenText, statusLine, watchState, type WatchViewDto } from './watchView.ts'

const NOW = Date.parse('2026-10-25T09:00:00Z')

function view(overrides: Partial<WatchViewDto> = {}): WatchViewDto {
  return {
    label: 'AL',
    ended: false,
    live: true,
    position: {
      latitude: -41.17,
      longitude: 174.77,
      heading: 90,
      timestamp: '2026-10-25T08:59:55Z',
      receivedAt: '2026-10-25T08:59:55Z',
    },
    ...overrides,
  }
}

test('an ended run is ended, whatever else the server says', () => {
  assert.deepEqual(watchState(view({ ended: true, position: null, live: false }), NOW), { kind: 'ended' })
})

test('a run with no position yet is waiting', () => {
  assert.deepEqual(watchState(view({ position: null, live: false }), NOW), { kind: 'waiting' })
})

test('a live runner is live', () => {
  assert.deepEqual(watchState(view(), NOW), { kind: 'live' })
})

test('a runner the server has stopped hearing from is stale, measured from when the server last heard', () => {
  const stale = view({
    live: false,
    position: {
      latitude: -41.17,
      longitude: 174.77,
      heading: null,
      timestamp: '2026-10-25T08:00:00Z', // the phone's clock is wrong; it is ignored
      receivedAt: '2026-10-25T08:47:00Z',
    },
  })
  assert.deepEqual(watchState(stale, NOW), { kind: 'stale', lastSeenMs: 13 * 60 * 1000 })
})

test('last seen reads naturally at each scale', () => {
  assert.equal(lastSeenText(0), 'just now')
  assert.equal(lastSeenText(9_000), 'just now')
  assert.equal(lastSeenText(42_000), '42 s ago')
  assert.equal(lastSeenText(3 * 60_000), '3 min ago')
  assert.equal(lastSeenText(60 * 60_000), '1 hr ago')
  assert.equal(lastSeenText((2 * 60 + 5) * 60_000), '2 hr 5 min ago')
  assert.equal(lastSeenText(-5_000), 'just now')
})

test('the status line says what is happening', () => {
  assert.equal(statusLine({ kind: 'ended' }, NOW, null), 'This run has ended')
  assert.equal(statusLine({ kind: 'waiting' }, NOW, null), 'Waiting for the first position…')
  assert.equal(statusLine({ kind: 'live' }, NOW, '2026-10-25T08:59:30Z'), 'Live · updated 30 s ago')
  assert.equal(
    statusLine({ kind: 'stale', lastSeenMs: 13 * 60_000 }, NOW, '2026-10-25T08:47:00Z'),
    'Not updating · last seen 13 min ago',
  )
})
