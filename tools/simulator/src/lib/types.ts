export type LatLon = { lat: number; lon: number }
export type Quality = 'good' | 'bad' | 'missing'
export type PhoneStatus = 'idle' | 'joining' | 'ready' | 'running' | 'left'

export type PhoneSnapshot = {
  id: number
  displayName: string
  status: PhoneStatus
  quality: Quality
  position: LatLon | null
  heading: number | null
}
