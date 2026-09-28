export type LatLon = { lat: number; lon: number }
// 'satellite' moves and reports heading exactly like 'good' — it's an orthogonal network-type
// flag (NWPath.isUltraConstrained), not a GPS-quality issue, matching the real app's own
// distinction between "on satellite" and "cadence/heading is degraded".
export type Quality = 'good' | 'bad' | 'missing' | 'satellite'
// Independent of Quality — unlike 'satellite' above, battery level doesn't affect movement,
// heading, or cadence at all in the real app, it's a separate self-reported field on the same
// POST. See .ai/plans/battery-level-reporting.md for the thresholds this exercises
// (LOW_BATTERY_THRESHOLD=30, CRITICAL_BATTERY_THRESHOLD=10 in useSessionTimelineLogic.ts).
export type BatteryMode = 'normal' | 'low' | 'critical'
export type PhoneStatus = 'idle' | 'joining' | 'ready' | 'running' | 'left'

export type PhoneSnapshot = {
  id: number
  displayName: string
  status: PhoneStatus
  quality: Quality
  position: LatLon | null
  heading: number | null
}
