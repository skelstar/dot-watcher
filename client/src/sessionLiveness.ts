export const LIVE_STALE_MS = 5 * 60 * 1000

export function isSessionLive(latestActivityMs: number | null, nowMs: number, staleMs: number = LIVE_STALE_MS): boolean {
  return latestActivityMs !== null && nowMs - latestActivityMs < staleMs
}
