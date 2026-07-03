import assert from 'node:assert/strict'
import test from 'node:test'
import {
  canManageMembersForRole,
  canWriteLocationForRole,
  shouldShowAuthPrompt,
  shouldShowSessionPrompt,
} from './sessionState.ts'

test('canWriteLocationForRole allows owners and runners only', () => {
  assert.equal(canWriteLocationForRole('owner'), true)
  assert.equal(canWriteLocationForRole('runner'), true)
  assert.equal(canWriteLocationForRole('viewer'), false)
  assert.equal(canWriteLocationForRole(undefined), false)
})

test('canManageMembersForRole allows owners only', () => {
  assert.equal(canManageMembersForRole('owner'), true)
  assert.equal(canManageMembersForRole('runner'), false)
  assert.equal(canManageMembersForRole('viewer'), false)
  assert.equal(canManageMembersForRole(undefined), false)
})

test('shouldShowAuthPrompt requires missing access token', () => {
  assert.equal(shouldShowAuthPrompt(null), true)
  assert.equal(shouldShowAuthPrompt('token'), false)
})

test('shouldShowAuthPrompt is skipped when an invite code lets someone view without signing in', () => {
  assert.equal(shouldShowAuthPrompt(null, 'INVITE123'), false)
  assert.equal(shouldShowAuthPrompt(null, null), true)
  assert.equal(shouldShowAuthPrompt('token', 'INVITE123'), false)
})

test('shouldShowSessionPrompt waits for auth and memberships', () => {
  assert.equal(shouldShowSessionPrompt({
    accessToken: null,
    membershipsLoaded: true,
    isReplay: false,
    inviteCode: null,
    sessionName: null,
    hasSessionMembership: false,
  }), false)
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: false,
    isReplay: false,
    inviteCode: null,
    sessionName: null,
    hasSessionMembership: false,
  }), false)
})

test('shouldShowSessionPrompt covers create, join, and missing-membership states', () => {
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: false,
    inviteCode: null,
    sessionName: null,
    hasSessionMembership: false,
  }), true)
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: false,
    inviteCode: 'INVITE123',
    sessionName: null,
    hasSessionMembership: false,
  }), true)
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: false,
    inviteCode: null,
    sessionName: 'SUNSET23',
    hasSessionMembership: false,
  }), true)
})

test('shouldShowSessionPrompt stays hidden for active sessions and replay flows', () => {
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: false,
    inviteCode: null,
    sessionName: 'SUNSET23',
    hasSessionMembership: true,
  }), false)
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: true,
    inviteCode: null,
    sessionName: 'SUNSET23',
    hasSessionMembership: false,
  }), false)
})

test('shouldShowSessionPrompt still auto-joins via invite code when the link requests replay', () => {
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: true,
    inviteCode: 'INVITE123',
    sessionName: null,
    hasSessionMembership: false,
  }), true)
  assert.equal(shouldShowSessionPrompt({
    accessToken: 'token',
    membershipsLoaded: true,
    isReplay: true,
    inviteCode: 'INVITE123',
    sessionName: null,
    hasSessionMembership: true,
  }), false)
})
