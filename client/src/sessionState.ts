import type { SessionMembership } from './types.ts'

type SessionRole = SessionMembership['role'] | undefined

interface PromptState {
  accessToken: string | null
  membershipsLoaded: boolean
  isReplay: boolean
  inviteCode: string | null
  sessionCode: string | null
  hasSessionMembership: boolean
}

export function canWriteLocationForRole(role: SessionRole): boolean {
  return role === 'owner' || role === 'runner'
}

export function canManageMembersForRole(role: SessionRole): boolean {
  return role === 'owner'
}

export function shouldShowAuthPrompt(accessToken: string | null): boolean {
  return !accessToken
}

export function shouldShowSessionPrompt(state: PromptState): boolean {
  if (!state.accessToken || !state.membershipsLoaded || state.isReplay) return false
  if (state.inviteCode) return !state.hasSessionMembership
  return !state.sessionCode || !state.hasSessionMembership
}
