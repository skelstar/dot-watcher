export type LatLon = { lat: number; lon: number }
// 'satellite' moves and reports heading exactly like 'good' — it's an orthogonal network-type
// flag (NWPath.isUltraConstrained), not a GPS-quality issue, matching the real app's own
// distinction between "on satellite" and "cadence/heading is degraded".
export type Quality = 'good' | 'bad' | 'missing' | 'satellite'
export type PhoneStatus = 'idle' | 'joining' | 'ready' | 'running' | 'left'

export type PhoneSnapshot = {
  id: number
  displayName: string
  status: PhoneStatus
  quality: Quality
  position: LatLon | null
  heading: number | null
}
