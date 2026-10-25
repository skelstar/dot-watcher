// What a casual watch link returns (server: Controllers/WatchController.cs, CasualWatchView) and how the
// page describes it. Kept apart from the component so the wording and thresholds can be tested.

export interface WatchPositionDto {
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
  // The server's clock when it last heard from the runner; "last seen" is measured from this, never
  // from the phone's own timestamp, which can be wrong.
  receivedAt: string
}

export interface WatchViewDto {
  label: string
  ended: boolean
  live: boolean
  position: WatchPositionDto | null
}

export type WatchState =
  | { kind: 'ended' }
  | { kind: 'waiting' }
  | { kind: 'live' }
  | { kind: 'stale'; lastSeenMs: number }

export function watchState(view: WatchViewDto, nowMs: number): WatchState {
  if (view.ended) return { kind: 'ended' }
  if (!view.position) return { kind: 'waiting' }
  if (view.live) return { kind: 'live' }
  return { kind: 'stale', lastSeenMs: nowMs - Date.parse(view.position.receivedAt) }
}

/** "just now", "42 s ago", "3 min ago", "2 hr 5 min ago". */
export function lastSeenText(ageMs: number): string {
  const seconds = Math.max(0, Math.round(ageMs / 1000))
  if (seconds < 10) return 'just now'
  if (seconds < 60) return `${seconds} s ago`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? `${hours} hr ago` : `${hours} hr ${rest} min ago`
}

/** The status line under the map, from the state of the link. */
export function statusLine(state: WatchState, nowMs: number, receivedAt: string | null): string {
  switch (state.kind) {
    case 'ended':
      return 'This run has ended'
    case 'waiting':
      return 'Waiting for the first position…'
    case 'live':
      return receivedAt ? `Live · updated ${lastSeenText(nowMs - Date.parse(receivedAt))}` : 'Live'
    case 'stale':
      return `Not updating · last seen ${lastSeenText(state.lastSeenMs)}`
  }
}
