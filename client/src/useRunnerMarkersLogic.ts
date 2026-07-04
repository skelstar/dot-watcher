export function livePollingError(status: number): string {
  return status === 403
    ? 'No membership for this session.'
    : status === 404
    ? 'Invite not found.'
    : `Live update failed: HTTP ${status}`
}

export function shouldPollLivePositions(
  sessionId: string | null,
  accessToken: string | null,
  isReplay: boolean,
): boolean {
  return Boolean(sessionId && accessToken && !isReplay)
}

export function shouldPollLivePositionsByInvite(
  inviteCode: string | null,
  accessToken: string | null,
  isReplay: boolean,
): boolean {
  return Boolean(inviteCode && !accessToken && !isReplay)
}
