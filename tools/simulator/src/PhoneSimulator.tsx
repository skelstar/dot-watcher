import { useEffect, useRef, useState, useCallback } from 'react'
import { MapContainer, TileLayer, Marker, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { dotIcon, convergenceIcon } from './lib/icons'
import { computeBearing, distanceMeters, destinationPoint } from './lib/geo'
import {
  registerParticipant,
  joinSession,
  postLocation,
  leaveSession,
  type AuthedUser,
} from './lib/dotwatcherApi'

export type LatLon = { lat: number; lon: number }
export type Quality = 'good' | 'bad' | 'missing'
export type PhoneStatus = 'idle' | 'joining' | 'ready' | 'running' | 'left'

export type PhoneSnapshot = {
  id: number
  displayName: string
  color: string
  status: PhoneStatus
  quality: Quality
  position: LatLon | null
}

type Props = {
  id: number
  index: number
  color: string
  defaultInviteCode: string
  convergencePoint: LatLon | null
  speedMps: number
  tickMs: number
  onSnapshot: (snapshot: PhoneSnapshot) => void
  onRemove: (id: number) => void
}

const WELLINGTON: [number, number] = [-41.2865, 174.7762]
const ARRIVE_METERS = 8

function ClickMarker({ enabled, position, icon, onPick }: {
  enabled: boolean
  position: LatLon | null
  icon: L.DivIcon
  onPick: (lat: number, lon: number) => void
}) {
  const onPickRef = useRef(onPick)
  useEffect(() => { onPickRef.current = onPick })
  useMapEvents({
    click(e) {
      if (enabled) onPickRef.current(e.latlng.lat, e.latlng.lng)
    },
  })
  return position ? <Marker position={[position.lat, position.lon]} icon={icon} /> : null
}

export default function PhoneSimulator({
  id, index, color, defaultInviteCode, convergencePoint, speedMps, tickMs, onSnapshot, onRemove,
}: Props) {
  const [displayName, setDisplayName] = useState(`Phone ${index + 1}`)
  const [inviteCode, setInviteCode] = useState(defaultInviteCode)
  const [status, setStatus] = useState<PhoneStatus>('idle')
  const [quality, setQuality] = useState<Quality>('good')
  const [position, setPosition] = useState<LatLon | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lastSentAt, setLastSentAt] = useState<string | null>(null)

  const userRef = useRef<AuthedUser | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const qualityRef = useRef(quality)
  const positionRef = useRef(position)
  const convergenceRef = useRef(convergencePoint)
  const inFlightRef = useRef(false)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  useEffect(() => { qualityRef.current = quality }, [quality])
  useEffect(() => { positionRef.current = position }, [position])
  useEffect(() => { convergenceRef.current = convergencePoint }, [convergencePoint])

  useEffect(() => {
    onSnapshot({ id, displayName, color, status, quality, position })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, displayName, color, status, quality, position])

  useEffect(() => () => { if (intervalRef.current) clearInterval(intervalRef.current) }, [])

  async function handleJoin() {
    if (!inviteCode.trim()) return
    setStatus('joining')
    setError(null)
    try {
      const user = await registerParticipant(displayName)
      const membership = await joinSession(user, inviteCode.trim(), displayName)
      userRef.current = user
      sessionIdRef.current = membership.sessionId
      setStatus('ready')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to join session.')
      setStatus('idle')
    }
  }

  const tick = useCallback(async () => {
    if (inFlightRef.current) return
    if (qualityRef.current === 'missing') return
    const cur = positionRef.current
    const user = userRef.current
    const sessionId = sessionIdRef.current
    if (!cur || !user || !sessionId) return

    let next: LatLon
    let heading: number | null
    const target = convergenceRef.current
    const stepMeters = speedMps * (tickMs / 1000)

    if (qualityRef.current === 'good' && target) {
      const dist = distanceMeters(cur.lat, cur.lon, target.lat, target.lon)
      if (dist <= ARRIVE_METERS) {
        next = cur
        heading = null // arrived and stationary — a real phone stops reporting heading when not moving
      } else {
        const bearing = computeBearing(cur.lat, cur.lon, target.lat, target.lon)
        const step = Math.min(stepMeters, dist)
        next = destinationPoint(cur.lat, cur.lon, bearing, step)
        heading = bearing
      }
    } else {
      // 'bad' GPS: erratic movement, ignoring the convergence point, with no heading — mirrors
      // the app's real GPS-signal-loss indicator, which keys off a null heading.
      const randomBearing = Math.random() * 360
      const step = stepMeters * (0.2 + Math.random() * 0.8)
      next = destinationPoint(cur.lat, cur.lon, randomBearing, step)
      heading = null
    }

    inFlightRef.current = true
    try {
      await postLocation(user, sessionId, next.lat, next.lon, heading)
      setPosition(next)
      setLastSentAt(new Date().toLocaleTimeString())
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Location post failed.')
    } finally {
      inFlightRef.current = false
    }
  }, [speedMps, tickMs])

  function handleStart() {
    if (!position || status === 'running') return
    setStatus('running')
    intervalRef.current = setInterval(tick, tickMs)
  }

  function handlePause() {
    if (intervalRef.current) { clearInterval(intervalRef.current); intervalRef.current = null }
    setStatus('ready')
  }

  async function handleLeave() {
    if (intervalRef.current) { clearInterval(intervalRef.current); intervalRef.current = null }
    const user = userRef.current
    const sessionId = sessionIdRef.current
    setStatus('left')
    if (user && sessionId) {
      try { await leaveSession(user, sessionId) } catch { /* best-effort */ }
    }
  }

  function handleRemove() {
    if (intervalRef.current) clearInterval(intervalRef.current)
    if (status !== 'left' && status !== 'idle') {
      const user = userRef.current
      const sessionId = sessionIdRef.current
      if (user && sessionId) leaveSession(user, sessionId).catch(() => {})
    }
    onRemove(id)
  }

  const canJoin = status === 'idle'
  const canPickStart = status === 'ready'
  const canStart = status === 'ready' && position !== null && convergencePoint !== null
  const canPause = status === 'running'
  const canLeave = status === 'ready' || status === 'running'

  const mapCenter: [number, number] = position ? [position.lat, position.lon] : WELLINGTON
  const distanceRemaining = position && convergencePoint
    ? distanceMeters(position.lat, position.lon, convergencePoint.lat, convergencePoint.lon)
    : null

  return (
    <div style={card(color)}>
      <div style={headerRow}>
        <span style={swatch(color)} />
        {status === 'idle' || status === 'joining'
          ? <input style={nameInput} value={displayName} onChange={e => setDisplayName(e.target.value)} disabled={status === 'joining'} />
          : <span style={nameLabel}>{displayName}</span>
        }
        <span style={statusPill(status)}>{statusLabel(status)}</span>
        <button style={removeBtn} onClick={handleRemove} title="Remove phone">×</button>
      </div>

      {(status === 'idle' || status === 'joining') && (
        <div style={joinRow}>
          <input
            style={codeInput}
            value={inviteCode}
            onChange={e => setInviteCode(e.target.value.toUpperCase())}
            placeholder="INVITE CODE"
            disabled={status === 'joining'}
          />
          <button style={primaryBtn(!inviteCode.trim() || status === 'joining')} disabled={!inviteCode.trim() || status === 'joining'} onClick={handleJoin}>
            {status === 'joining' ? 'Joining…' : 'Join'}
          </button>
          {defaultInviteCode && defaultInviteCode !== inviteCode && (
            <button style={linkBtn} onClick={() => setInviteCode(defaultInviteCode)}>use {defaultInviteCode}</button>
          )}
        </div>
      )}

      {error && <div style={errorText}>{error}</div>}

      {status !== 'idle' && status !== 'joining' && (
        <>
          <p style={hint}>
            {status === 'ready' && !position && 'Click the map to choose a starting point.'}
            {status === 'ready' && position && !convergencePoint && 'Waiting for the session convergence point to be set.'}
            {status === 'ready' && position && convergencePoint && 'Ready — press Start when this phone should begin moving.'}
            {status === 'running' && 'Sending location updates…'}
            {status === 'left' && 'Left the session.'}
          </p>

          <MapContainer center={mapCenter} zoom={13} style={miniMapStyle}>
            <TileLayer
              url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
              attribution='&copy; OpenStreetMap contributors'
            />
            {convergencePoint && <Marker position={[convergencePoint.lat, convergencePoint.lon]} icon={convergenceIcon()} />}
            <ClickMarker enabled={canPickStart} position={position} icon={dotIcon(color)} onPick={(lat, lon) => setPosition({ lat, lon })} />
          </MapContainer>

          <div style={qualityRow}>
            {(['good', 'bad', 'missing'] as Quality[]).map(q => (
              <label key={q} style={qualityLabel(quality === q, status === 'left')}>
                <input
                  type="checkbox"
                  checked={quality === q}
                  disabled={status === 'left'}
                  onChange={() => setQuality(q)}
                  style={{ marginRight: '0.35rem' }}
                />
                {qualityText(q)}
              </label>
            ))}
          </div>

          <div style={actionRow}>
            {status !== 'running'
              ? <button style={primaryBtn(!canStart)} disabled={!canStart} onClick={handleStart}>Start</button>
              : <button style={pauseBtn} onClick={handlePause}>Pause</button>
            }
            <button style={leaveBtn(!canLeave)} disabled={!canLeave} onClick={handleLeave}>Leave session</button>
          </div>

          <div style={statusLine}>
            {position && <span>{position.lat.toFixed(5)}, {position.lon.toFixed(5)}</span>}
            {distanceRemaining !== null && (
              <span> · {distanceRemaining <= ARRIVE_METERS ? 'Arrived' : `${Math.round(distanceRemaining)}m to go`}</span>
            )}
            {lastSentAt && <span> · last sent {lastSentAt}</span>}
          </div>
        </>
      )}
    </div>
  )
}

function statusLabel(status: PhoneStatus): string {
  switch (status) {
    case 'idle': return 'Not joined'
    case 'joining': return 'Joining…'
    case 'ready': return 'Joined'
    case 'running': return 'Running'
    case 'left': return 'Left'
  }
}

function qualityText(q: Quality): string {
  switch (q) {
    case 'good': return 'Good'
    case 'bad': return 'Bad GPS'
    case 'missing': return 'Missing'
  }
}

const card = (color: string): React.CSSProperties => ({
  border: '1px solid #e2e8f0',
  borderLeft: `4px solid ${color}`,
  borderRadius: 8,
  padding: '0.85rem',
  marginBottom: '0.85rem',
  background: '#fff',
})

const headerRow: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }
const swatch = (color: string): React.CSSProperties => ({ width: 12, height: 12, borderRadius: '50%', background: color, flexShrink: 0 })
const nameInput: React.CSSProperties = { flex: 1, fontWeight: 600, fontSize: '0.9rem', padding: '0.3rem 0.5rem', border: '1px solid #cbd5e1', borderRadius: 6 }
const nameLabel: React.CSSProperties = { flex: 1, fontWeight: 600, fontSize: '0.9rem' }
const removeBtn: React.CSSProperties = { border: 'none', background: 'none', fontSize: '1.1rem', cursor: 'pointer', color: '#94a3b8', lineHeight: 1, padding: '0 0.25rem' }

const statusPill = (status: PhoneStatus): React.CSSProperties => {
  const colors: Record<PhoneStatus, [string, string]> = {
    idle: ['#f1f5f9', '#64748b'],
    joining: ['#e0f2fe', '#0284c7'],
    ready: ['#fef9c3', '#a16207'],
    running: ['#dcfce7', '#16a34a'],
    left: ['#f1f5f9', '#94a3b8'],
  }
  const [bg, fg] = colors[status]
  return { background: bg, color: fg, fontSize: '0.72rem', fontWeight: 700, padding: '2px 8px', borderRadius: 999 }
}

const joinRow: React.CSSProperties = { display: 'flex', gap: '0.5rem', marginBottom: '0.5rem', flexWrap: 'wrap' }
const codeInput: React.CSSProperties = { flex: 1, minWidth: 120, padding: '0.4rem 0.6rem', border: '1px solid #cbd5e1', borderRadius: 6, fontFamily: 'monospace', letterSpacing: '0.05em' }
const errorText: React.CSSProperties = { color: '#dc2626', fontSize: '0.8rem', marginBottom: '0.5rem' }
const hint: React.CSSProperties = { fontSize: '0.8rem', color: '#64748b', margin: '0 0 0.5rem' }
const miniMapStyle: React.CSSProperties = { height: 200, borderRadius: 8, border: '1px solid #e2e8f0', marginBottom: '0.6rem' }

const qualityRow: React.CSSProperties = { display: 'flex', gap: '1rem', marginBottom: '0.6rem', fontSize: '0.85rem' }
const qualityLabel = (active: boolean, disabled: boolean): React.CSSProperties => ({
  display: 'flex', alignItems: 'center', cursor: disabled ? 'default' : 'pointer',
  fontWeight: active ? 700 : 400, color: disabled ? '#cbd5e1' : active ? '#1e293b' : '#475569',
})

const actionRow: React.CSSProperties = { display: 'flex', gap: '0.5rem', marginBottom: '0.5rem' }
const primaryBtn = (disabled: boolean): React.CSSProperties => ({
  padding: '0.4rem 1rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.85rem',
  background: disabled ? '#e2e8f0' : '#16a34a', color: disabled ? '#94a3b8' : '#fff', cursor: disabled ? 'default' : 'pointer',
})
const pauseBtn: React.CSSProperties = { padding: '0.4rem 1rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.85rem', background: '#f59e0b', color: '#fff', cursor: 'pointer' }
const leaveBtn = (disabled: boolean): React.CSSProperties => ({
  padding: '0.4rem 1rem', borderRadius: 6, border: '1px solid #fca5a5', fontWeight: 600, fontSize: '0.85rem',
  background: '#fff', color: disabled ? '#e2e8f0' : '#dc2626', cursor: disabled ? 'default' : 'pointer', borderColor: disabled ? '#e2e8f0' : '#fca5a5',
})
const linkBtn: React.CSSProperties = { border: 'none', background: 'none', color: '#2563eb', fontSize: '0.8rem', cursor: 'pointer', textDecoration: 'underline' }
const statusLine: React.CSSProperties = { fontSize: '0.78rem', color: '#64748b', fontFamily: 'monospace' }
