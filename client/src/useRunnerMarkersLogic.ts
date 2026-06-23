export function livePollingError(status: number): string {
  return status === 403
    ? 'No membership for this session.'
    : `Live update failed: HTTP ${status}`
}

export function shouldPollLivePositions(
  sessionId: string | null,
  accessToken: string | null,
  replayPositionsProvided: boolean,
): boolean {
  return Boolean(sessionId && accessToken && !replayPositionsProvided)
}
