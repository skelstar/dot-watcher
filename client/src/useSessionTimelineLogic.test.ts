import assert from 'node:assert/strict'
import test from 'node:test'
import {
  earliestActivityMs,
  findLatestGpsJump,
  haversineMeters,
  impliedSpeedKmh,
  isGpsJump,
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

test('haversineMeters is ~0 for the same point and matches a known distance', () => {
  const wellington = { runnerName: 'A', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: 't0' }
  assert.ok(haversineMeters(wellington, wellington) < 1)

  // Wellington to Auckland is ~495km as the crow flies.
  const auckland = { runnerName: 'A', latitude: -36.8485, longitude: 174.7633, heading: null, timestamp: 't1' }
  const metres = haversineMeters(wellington, auckland)
  assert.ok(metres > 490_000 && metres < 500_000, `expected ~495km, got ${metres / 1000}km`)
})

test('impliedSpeedKmh is null for a non-positive time delta, otherwise distance over time', () => {
  const a = { runnerName: 'A', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:10Z' }
  const bSameInstant = { runnerName: 'A', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:10Z' }
  const bEarlier = { runnerName: 'A', latitude: 1, longitude: 1, heading: null, timestamp: '2024-01-01T00:00:00Z' }
  assert.equal(impliedSpeedKmh(a, bSameInstant), null)
  assert.equal(impliedSpeedKmh(a, bEarlier), null)

  // 1km in 60 seconds = 60 km/h.
  const start = { runnerName: 'A', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' }
  const oneKmNorth = { runnerName: 'A', latitude: 0.008993, longitude: 0, heading: null, timestamp: '2024-01-01T00:01:00Z' }
  const kmh = impliedSpeedKmh(start, oneKmNorth)!
  assert.ok(kmh > 55 && kmh < 65, `expected ~60km/h, got ${kmh}`)
})

test('isGpsJump flags implausible speed and passes a plausible move', () => {
  const start = { runnerName: 'A', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:00Z' }
  // A brisk 12 km/h jog for 15 seconds covers ~50m - plausible.
  const jogStep = { runnerName: 'A', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' }
  assert.equal(isGpsJump(start, jogStep), false)

  // Wellington to Auckland (~495km) in 15 seconds implies ~119,000 km/h - a GPS glitch, not a run.
  const auckland = { runnerName: 'A', latitude: -36.8485, longitude: 174.7633, heading: null, timestamp: '2024-01-01T00:00:15Z' }
  assert.equal(isGpsJump(start, auckland), true)
})

test('findLatestGpsJump returns null when every runner only makes plausible moves', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      { runnerName: 'Alice', latitude: -41.28605, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
  ])
  assert.equal(findLatestGpsJump(byRunner), null)
})

test('findLatestGpsJump reports whichever currently-glitching runner glitched most recently', () => {
  const byRunner = new Map([
    ['Alice', [
      { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      // Alice is currently glitching, as of 00:00:15.
      { runnerName: 'Alice', latitude: -36.8485, longitude: 174.7633, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    ]],
    ['Bob', [
      { runnerName: 'Bob', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:00:00Z' },
      // Bob is also currently glitching, more recently (00:01:00) - this one should win.
      { runnerName: 'Bob', latitude: 10, longitude: 10, heading: null, timestamp: '2024-01-01T00:01:00Z' },
    ]],
  ])
  const jump = findLatestGpsJump(byRunner)
  assert.equal(jump?.runnerName, 'Bob')
  assert.equal(jump?.timestamp, '2024-01-01T00:01:00Z')
})

test('findLatestGpsJump stays flagged until GPS_JUMP_CLEAR_STREAK plausible readings follow the jump', () => {
  const glitchPoint = { runnerName: 'Alice', latitude: -36.8485, longitude: 174.7633 }
  const positions = [
    { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:00Z' },
    // A glitch here...
    { ...glitchPoint, heading: null, timestamp: '2024-01-01T00:00:15Z' },
    // ...one plausible reading isn't enough to clear it...
    { ...glitchPoint, latitude: -36.84845, heading: null, timestamp: '2024-01-01T00:00:30Z' },
  ]
  assert.notEqual(findLatestGpsJump(new Map([['Alice', positions]])), null)

  // ...nor two...
  const twoGood = [...positions, { ...glitchPoint, latitude: -36.8484, heading: null, timestamp: '2024-01-01T00:00:45Z' }]
  assert.notEqual(findLatestGpsJump(new Map([['Alice', twoGood]])), null)

  // ...but a third consecutive plausible reading clears it.
  const threeGood = [...twoGood, { ...glitchPoint, latitude: -36.84835, heading: null, timestamp: '2024-01-01T00:01:00Z' }]
  assert.equal(findLatestGpsJump(new Map([['Alice', threeGood]])), null)
})

test('findLatestGpsJump resets the clear-streak if another jump happens before it fully clears', () => {
  const positions = [
    { runnerName: 'Alice', latitude: -41.2865, longitude: 174.7762, heading: null, timestamp: '2024-01-01T00:00:00Z' },
    { runnerName: 'Alice', latitude: -36.8485, longitude: 174.7633, heading: null, timestamp: '2024-01-01T00:00:15Z' }, // jump 1
    { runnerName: 'Alice', latitude: -36.84845, longitude: 174.7633, heading: null, timestamp: '2024-01-01T00:00:30Z' }, // good (streak 1)
    { runnerName: 'Alice', latitude: -36.8484, longitude: 174.7633, heading: null, timestamp: '2024-01-01T00:00:45Z' }, // good (streak 2)
    // Another jump before streak reaches 3 - resets the streak, so it's still flagged.
    { runnerName: 'Alice', latitude: 0, longitude: 0, heading: null, timestamp: '2024-01-01T00:01:00Z' },
  ]
  const jump = findLatestGpsJump(new Map([['Alice', positions]]))
  assert.notEqual(jump, null)
  assert.equal(jump?.timestamp, '2024-01-01T00:01:00Z')
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
