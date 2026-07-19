import assert from 'node:assert/strict'
import test from 'node:test'
import {
  earliestActivityMs,
  findGpsSignalLoss,
  isRangeCovered,
  latestActivityMs,
  livePollingError,
  maxOrNull,
  mergeIntoByRunner,
  mergeRange,
  parseNdjson,
  positionsAtCutoff,
  shouldPollLivePositions,
  shouldPollLivePositionsByInvite,
} from './useSessionTimelineLogic.ts'

test('mergeRange inserts a disjoint range in sorted order', () => {
  const ranges = mergeRange([{ since: 0, until: 10 }], { since: 20, until: 30 })
  assert.deepEqual(ranges, [{ since: 0, until: 10 }, { since: 20, until: 30 }])
})

test('mergeRange coalesces overlapping ranges', () => {
  const ranges = mergeRange([{ since: 0, until: 10 }], { since: 5, until: 15 })
  assert.deepEqual(ranges, [{ since: 0, until: 15 }])
})

test('mergeRange coalesces adjacent/touching ranges', () => {
  const ranges = mergeRange([{ since: 0, until: 10 }], { since: 10, until: 20 })
  assert.deepEqual(ranges, [{ since: 0, until: 20 }])
})

test('mergeRange can bridge two existing ranges into one', () => {
  const ranges = mergeRange([{ since: 0, until: 10 }, { since: 30, until: 40 }], { since: 10, until: 30 })
  assert.deepEqual(ranges, [{ since: 0, until: 40 }])
})

test('isRangeCovered is true only when a single range fully contains the query', () => {
  const ranges = [{ since: 0, until: 10 }, { since: 20, until: 30 }]
  assert.equal(isRangeCovered(ranges, 2, 8), true)
  assert.equal(isRangeCovered(ranges, 0, 10), true)
  assert.equal(isRangeCovered(ranges, 5, 25), false)
  assert.equal(isRangeCovered(ranges, 40, 50), false)
})

test('shouldPollLivePositions requires a session and access token', () => {
  assert.equal(shouldPollLivePositions('SUNSET23', 'token'), true)
  assert.equal(shouldPollLivePositions(null, 'token'), false)
  assert.equal(shouldPollLivePositions('SUNSET23', null), false)
})

test('shouldPollLivePositionsByInvite requires an invite code and no access token', () => {
  assert.equal(shouldPollLivePositionsByInvite('INVITE123', null), true)
  assert.equal(shouldPollLivePositionsByInvite(null, null), false)
  assert.equal(shouldPollLivePositionsByInvite('INVITE123', 'token'), false)
})

test('livePollingError gives membership-specific copy for 403 responses', () => {
  assert.equal(livePollingError(403), 'No membership for this session.')
  assert.equal(livePollingError(404), 'Invite not found.')
  assert.equal(livePollingError(500), 'Live update failed: HTTP 500')
})

test('parseNdjson parses camelCase lines', () => {
  const line = JSON.stringify({ runnerName: 'Alice', latitude: 1, longitude: 2, heading: 90, timestamp: 't1' })
  assert.deepEqual(parseNdjson(line), [{ runnerName: 'Alice', latitude: 1, longitude: 2, heading: 90, timestamp: 't1' }])
})

test('parseNdjson normalises legacy PascalCase lines', () => {
  const line = JSON.stringify({ RunnerName: 'Bob', Latitude: 1, Longitude: 2, Timestamp: 't1' })
  assert.deepEqual(parseNdjson(line), [{ runnerName: 'Bob', latitude: 1, longitude: 2, heading: null, timestamp: 't1' }])
})

test('mergeIntoByRunner appends and sorts by timestamp, de-duping exact repeats', () => {
  const initial = new Map([['Alice', [{ runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' }]]])
  const merged = mergeIntoByRunner(initial, [
    { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:10Z' },
    { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
  ])
  assert.equal(merged.get('Alice')?.length, 2)
  assert.equal(merged.get('Alice')?.[0].timestamp, '2024-01-01T00:00:00Z')
  assert.equal(merged.get('Alice')?.[1].timestamp, '2024-01-01T00:00:10Z')
})

test('latestActivityMs is null when no runner has reported in', () => {
  assert.equal(latestActivityMs(new Map()), null)
})

test('latestActivityMs is the max last-position timestamp across all runners', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:10Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 2, longitude: 2, heading: null, timestamp: '2024-01-01T00:00:20Z' },
    ]],
  ])
  assert.equal(latestActivityMs(byRunner), new Date('2024-01-01T00:00:20Z').getTime())
})

test('earliestActivityMs is null when no runner has reported in', () => {
  assert.equal(earliestActivityMs(new Map()), null)
})

test('earliestActivityMs is the min first-position timestamp across all runners', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:10Z' },
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:20Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 2, longitude: 2, heading: null, timestamp: '2024-01-01T00:00:05Z' },
    ]],
  ])
  assert.equal(earliestActivityMs(byRunner), new Date('2024-01-01T00:00:05Z').getTime())
})

test('maxOrNull returns the larger value when both are known', () => {
  assert.equal(maxOrNull(10, 20), 20)
  assert.equal(maxOrNull(20, 10), 20)
})

test('maxOrNull falls back to whichever side is known when the other is null', () => {
  assert.equal(maxOrNull(null, 20), 20)
  assert.equal(maxOrNull(10, null), 10)
  assert.equal(maxOrNull(null, null), null)
})

test('findGpsSignalLoss returns null when every reading has a real heading', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: 91, timestamp: '2024-01-01T00:00:15Z' },
    ]],
  ])
  assert.equal(findGpsSignalLoss(byRunner), null)
})

test('findGpsSignalLoss flags a runner whose latest reading has a null heading', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
  ])
  const loss = findGpsSignalLoss(byRunner)
  assert.equal(loss?.runnerName, 'Alice')
  assert.equal(loss?.timestamp, '2024-01-01T00:00:15Z')
})

test('findGpsSignalLoss reports whichever currently-affected runner lost heading most recently', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      // Alice lost heading at 00:00:15.
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 0, longitude: 0, heading: 180, timestamp: '2024-01-01T00:00:00Z' },
      // Bob lost heading more recently (00:01:00) - this one should win.
      { runnerName: 'Bob', latitude: 0.0001, longitude: 0, heading: null, timestamp: '2024-01-01T00:01:00Z' },
    ]],
  ])
  const loss = findGpsSignalLoss(byRunner)
  assert.equal(loss?.runnerName, 'Bob')
  assert.equal(loss?.timestamp, '2024-01-01T00:01:00Z')
})

test('findGpsSignalLoss stays flagged until GPS_JUMP_CLEAR_STREAK readings with a real heading follow', () => {
  const base = { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762 }
  const positions = [
    { ...base, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
    // Heading lost here...
    { ...base, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    // ...one reading with a real heading isn't enough to clear it...
    { ...base, heading: 91, timestamp: '2024-01-01T00:00:30Z' },
  ]
  assert.notEqual(findGpsSignalLoss(new Map([['Alice', positions]])), null)

  // ...nor two...
  const twoGood = [...positions, { ...base, heading: 92, timestamp: '2024-01-01T00:00:45Z' }]
  assert.notEqual(findGpsSignalLoss(new Map([['Alice', twoGood]])), null)

  // ...but a third consecutive reading with a real heading clears it.
  const threeGood = [...twoGood, { ...base, heading: 93, timestamp: '2024-01-01T00:01:00Z' }]
  assert.equal(findGpsSignalLoss(new Map([['Alice', threeGood]])), null)
})

test('findGpsSignalLoss resets the clear-streak if heading is lost again before it fully clears', () => {
  const base = { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762 }
  const positions = [
    { ...base, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
    { ...base, heading: null, timestamp: '2024-01-01T00:00:15Z' }, // lost
    { ...base, heading: 91, timestamp: '2024-01-01T00:00:30Z' }, // good (streak 1)
    { ...base, heading: 92, timestamp: '2024-01-01T00:00:45Z' }, // good (streak 2)
    // Heading lost again before streak reaches 3 - resets the streak, so it's still flagged.
    { ...base, heading: null, timestamp: '2024-01-01T00:01:00Z' },
  ]
  const loss = findGpsSignalLoss(new Map([['Alice', positions]]))
  assert.notEqual(loss, null)
  assert.equal(loss?.timestamp, '2024-01-01T00:01:00Z')
})

test('positionsAtCutoff returns the last position at or before cutoff, omitting runners with none yet', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:10Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 2, longitude: 2, heading: null, timestamp: '2024-01-01T00:00:20Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:00:10Z').getTime()
  const result = positionsAtCutoff(byRunner, cutoff)
  assert.equal(result.length, 1)
  assert.equal(result[0][0].runnerName, 'Alice')
  assert.equal(result[0][0].timestamp, '2024-01-01T00:00:10Z')
})
