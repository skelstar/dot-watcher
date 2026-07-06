import assert from 'node:assert/strict'
import test from 'node:test'
import { isSessionLive, LIVE_STALE_MS } from './sessionLiveness.ts'

test('isSessionLive is false when there is no recorded activity', () => {
  assert.equal(isSessionLive(null, 1_000_000, LIVE_STALE_MS), false)
})

test('isSessionLive is true when the latest activity is within the stale window', () => {
  const now = 1_000_000
  assert.equal(isSessionLive(now - 1000, now, LIVE_STALE_MS), true)
})

test('isSessionLive is false once the latest activity is older than the stale window', () => {
  const now = 1_000_000
  assert.equal(isSessionLive(now - LIVE_STALE_MS - 1, now, LIVE_STALE_MS), false)
})

test('isSessionLive treats the exact stale boundary as no longer live', () => {
  const now = 1_000_000
  assert.equal(isSessionLive(now - LIVE_STALE_MS, now, LIVE_STALE_MS), false)
})
