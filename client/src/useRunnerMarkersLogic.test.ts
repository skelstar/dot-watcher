import assert from 'node:assert/strict'
import test from 'node:test'
import { livePollingError, shouldPollLivePositions } from './useRunnerMarkersLogic.ts'

test('shouldPollLivePositions requires a session and access token', () => {
  assert.equal(shouldPollLivePositions('SUNSET23', 'token', false), true)
  assert.equal(shouldPollLivePositions(null, 'token', false), false)
  assert.equal(shouldPollLivePositions('SUNSET23', null, false), false)
})

test('shouldPollLivePositions skips live polling while replay positions are provided', () => {
  assert.equal(shouldPollLivePositions('SUNSET23', 'token', true), false)
})

test('livePollingError gives membership-specific copy for 403 responses', () => {
  assert.equal(livePollingError(403), 'No membership for this session.')
  assert.equal(livePollingError(500), 'Live update failed: HTTP 500')
})
