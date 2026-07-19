import assert from 'node:assert/strict'
import test from 'node:test'
import {
  earliestActivityMs,
  findGpsSignalLoss,
  findRunnersWithGap,
  findSleepingRunners,
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

test('findGpsSignalLoss returns an empty set when every reading has a real heading', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: 91, timestamp: '2024-01-01T00:00:15Z' },
    ]],
  ])
  assert.deepEqual(findGpsSignalLoss(byRunner), new Set())
})

test('findGpsSignalLoss flags a runner whose latest reading has a null heading', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
  ])
  assert.deepEqual(findGpsSignalLoss(byRunner), new Set(['Alice']))
})

test('findGpsSignalLoss includes every currently-affected runner, not just one', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 0, longitude: 0, heading: 180, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Bob', latitude: 0.0001, longitude: 0, heading: null, timestamp: '2024-01-01T00:01:00Z' },
    ]],
    ['Carol', [
      { runnerName: 'Carol', latitude: 10, longitude: 10, heading: 45, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Carol', latitude: 10.0001, longitude: 10, heading: 46, timestamp: '2024-01-01T00:01:00Z' },
    ]],
  ])
  assert.deepEqual(findGpsSignalLoss(byRunner), new Set(['Alice', 'Bob']))
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
  assert.deepEqual(findGpsSignalLoss(new Map([['Alice', positions]])), new Set(['Alice']))

  // ...nor two...
  const twoGood = [...positions, { ...base, heading: 92, timestamp: '2024-01-01T00:00:45Z' }]
  assert.deepEqual(findGpsSignalLoss(new Map([['Alice', twoGood]])), new Set(['Alice']))

  // ...but a third consecutive reading with a real heading clears it.
  const threeGood = [...twoGood, { ...base, heading: 93, timestamp: '2024-01-01T00:01:00Z' }]
  assert.deepEqual(findGpsSignalLoss(new Map([['Alice', threeGood]])), new Set())
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
  assert.deepEqual(findGpsSignalLoss(new Map([['Alice', positions]])), new Set(['Alice']))
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

test('findRunnersWithGap flags a runner with no position within the gap window at cutoff', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      // Gap of 5 minutes before the next report — well past MISSING_GAP_MS.
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:05:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:02:00Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set(['Alice']))
})

test('findRunnersWithGap does not flag a runner reporting normally at cutoff', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:15Z' },
      { runnerName: 'Alice', latitude: 2, longitude: 2, heading: null, timestamp: '2024-01-01T00:00:30Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:00:20Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set())
})

test('findRunnersWithGap does not flag a runner who simply has not reported in yet', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:10:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:00:00Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set())
})

test('findRunnersWithGap flags a runner who has stopped reporting, same as a mid-track gap', () => {
  // No later position exists to "prove" this is temporary rather than permanent — but during
  // forward playback of a recording there's essentially never a later position cached yet
  // regardless (see useSessionTimeline.ts's ensureCovered, which only fetches behind the
  // playhead), so this can't be required. A runner who stopped for good reads as missing
  // indefinitely, same as one who returns after a long gap — the marker just reflects "no
  // data right now" either way.
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:10:00Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set(['Alice']))
})

test('findRunnersWithGap only flags runners actually affected, alongside unaffected ones', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:05:00Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:01:45Z' },
      { runnerName: 'Bob', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:02:15Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:02:00Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set(['Alice']))
})

test('findSleepingRunners flags a runner whose recent reports show negligible movement', () => {
  const byRunner = new Map([
    ['Alice', [
      // ~1m of jitter around the same spot, all within the sleeping window.
      { runnerName: 'Alice', latitude: -41.28650, longitude: 174.77620, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28651, longitude: 174.77620, heading: 90, timestamp: '2024-01-01T00:00:30Z' },
      { runnerName: 'Alice', latitude: -41.28650, longitude: 174.77621, heading: 90, timestamp: '2024-01-01T00:01:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:01:00Z').getTime()
  assert.deepEqual(findSleepingRunners(byRunner, cutoff), new Set(['Alice']))
})

test('findSleepingRunners does not flag a runner making steady progress', () => {
  const byRunner = new Map([
    ['Alice', [
      // ~100m between each report — clearly moving.
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.2856, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:30Z' },
      { runnerName: 'Alice', latitude: -41.2847, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:01:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:01:00Z').getTime()
  assert.deepEqual(findSleepingRunners(byRunner, cutoff), new Set())
})

test('findSleepingRunners does not flag a runner with too few recent reports to judge', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:00:00Z').getTime()
  assert.deepEqual(findSleepingRunners(byRunner, cutoff), new Set())
})

test('findSleepingRunners only judges movement within the recent window, ignoring older history', () => {
  const byRunner = new Map([
    ['Alice', [
      // Moved a lot long ago, but has been stationary for the last SLEEPING_WINDOW_MS.
      { runnerName: 'Alice', latitude: -41.30, longitude: 174.70, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28650, longitude: 174.77620, heading: 90, timestamp: '2024-01-01T00:10:00Z' },
      { runnerName: 'Alice', latitude: -41.28651, longitude: 174.77620, heading: 90, timestamp: '2024-01-01T00:10:30Z' },
      { runnerName: 'Alice', latitude: -41.28650, longitude: 174.77621, heading: 90, timestamp: '2024-01-01T00:11:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:11:00Z').getTime()
  assert.deepEqual(findSleepingRunners(byRunner, cutoff), new Set(['Alice']))
})

test('findSleepingRunners is independent of a runner who is currently missing (no data to judge)', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:00:00Z' },
      // Big gap, then reappears — Alice is "missing" at the cutoff below, not "sleeping".
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: 90, timestamp: '2024-01-01T00:05:00Z' },
    ]],
  ])
  const cutoff = new Date('2024-01-01T00:02:00Z').getTime()
  assert.deepEqual(findRunnersWithGap(byRunner, cutoff), new Set(['Alice']))
  // Only one position exists at/before cutoff, so there's nothing to compare it against.
  assert.deepEqual(findSleepingRunners(byRunner, cutoff), new Set())
})
