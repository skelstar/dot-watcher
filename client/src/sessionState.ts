import type { SessionMembership } from './types.ts'

type SessionRole = SessionMembership['role'] | undefined

interface PromptState {
  accessToken: string | null
  membershipsLoaded: boolean
  inviteCode: string | null
  sessionName: string | null
  hasSessionMembership: boolean
}

export function canWriteLocationForRole(role: SessionRole): boolean {
  return role === 'owner' || role === 'runner'
}

export function canManageMembersForRole(role: SessionRole): boolean {
  return role === 'owner'
}

export function shouldShowAuthPrompt(accessToken: string | null, inviteCode: string | null = null): boolean {
  return !accessToken && !inviteCode
}

export function shouldShowSessionPrompt(state: PromptState): boolean {
  if (!state.accessToken || !state.membershipsLoaded) return false
  if (state.inviteCode) return !state.hasSessionMembership
  return !state.sessionName || !state.hasSessionMembership
}
