import { useEffect, useRef, useState, useCallback } from 'react'
import { MapContainer, TileLayer, Marker, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { initialsFor, runnerColour, phoneMarkerIcon, convergenceIcon } from './lib/icons'
import { computeBearing, distanceMeters, destinationPoint, ARRIVE_METERS } from './lib/geo'
import {
  registerParticipant,
  joinSession,
  postLocation,
  leaveSession,
  type AuthedUser,
} from './lib/dotwatcherApi'
import type { LatLon, Quality, PhoneStatus, PhoneSnapshot } from './lib/types'

export type { LatLon, Quality, PhoneStatus, PhoneSnapshot }

type Props = {
  id: number
  index: number
  defaultInviteCode: string
  convergencePoint: LatLon | null
  speedMps: number
  tickMs: number
  onSnapshot: (snapshot: PhoneSnapshot) => void
  onRemove: (id: number) => void
}

const WELLINGTON: [number, number] = [-41.2865, 174.7762]

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
  id, index, defaultInviteCode, convergencePoint, speedMps, tickMs, onSnapshot, onRemove,
}: Props) {
  // Short by default — this is the actual displayName registered with the server, so it's
  // what the real client (and its own marker labels) will show too, not just a local label.
  const [displayName, setDisplayName] = useState(`P${index + 1}`)
  const [inviteCode, setInviteCode] = useState(defaultInviteCode)
  const [status, setStatus] = useState<PhoneStatus>('idle')
  const [quality, setQuality] = useState<Quality>('good')
  const [position, setPosition] = useState<LatLon | null>(null)
  const [heading, setHeading] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lastSentAt, setLastSentAt] = useState<string | null>(null)

  const userRef = useRef<AuthedUser | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const qualityRef = useRef(quality)
  const positionRef = useRef(position)
  const convergenceRef = useRef(convergencePoint)
  const inFlightRef = useRef(false)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const color = runnerColour(displayName)

  useEffect(() => { qualityRef.current = quality }, [quality])
  useEffect(() => { positionRef.current = position }, [position])
  useEffect(() => { convergenceRef.current = convergencePoint }, [convergencePoint])

  useEffect(() => {
    onSnapshot({ id, displayName, status, quality, position, heading })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, displayName, status, quality, position, heading])

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
    let nextHeading: number | null
    const target = convergenceRef.current
    const stepMeters = speedMps * (tickMs / 1000)

    if (qualityRef.current === 'good' && target) {
      const dist = distanceMeters(cur.lat, cur.lon, target.lat, target.lon)
      if (dist <= ARRIVE_METERS) {
        next = cur
        nextHeading = null // arrived and stationary — a real phone stops reporting heading when not moving
      } else {
        const bearing = computeBearing(cur.lat, cur.lon, target.lat, target.lon)
        const step = Math.min(stepMeters, dist)
        next = destinationPoint(cur.lat, cur.lon, bearing, step)
        nextHeading = bearing
      }
    } else {
      // 'bad' GPS: erratic movement, ignoring the convergence point, with no heading — mirrors
      // the app's real GPS-signal-loss indicator, which keys off a null heading.
      const randomBearing = Math.random() * 360
      const step = stepMeters * (0.2 + Math.random() * 0.8)
      next = destinationPoint(cur.lat, cur.lon, randomBearing, step)
      nextHeading = null
    }

    inFlightRef.current = true
    try {
      await postLocation(user, sessionId, next.lat, next.lon, nextHeading)
      setPosition(next)
      setHeading(nextHeading)
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

  const canPickStart = status === 'ready'
  const canStart = status === 'ready' && position !== null && convergencePoint !== null
  const canLeave = status === 'ready' || status === 'running'

  const mapCenter: [number, number] = position ? [position.lat, position.lon] : WELLINGTON
  const distanceRemaining = position && convergencePoint
    ? distanceMeters(position.lat, position.lon, convergencePoint.lat, convergencePoint.lon)
    : null

  const icon = phoneMarkerIcon({ displayName, quality, status, heading, position, convergencePoint })

  return (
    <div style={card(color)}>
      <div style={headerRow}>
        <span style={initialsBadge(color)}>{initialsFor(displayName)}</span>
        {status === 'idle' || status === 'joining'
          ? <input style={nameInput} value={displayName} onChange={e => setDisplayName(e.target.value)} disabled={status === 'joining'} />
          : <span style={nameLabel}>{displayName}</span>
        }
        <button style={removeBtn} onClick={handleRemove} title="Remove phone">×</button>
      </div>
      <div style={statusPillRow}>
        <span style={statusPill(status)}>{statusLabel(status)}</span>
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
            {status === 'ready' && position && !convergencePoint && 'Waiting for the convergence point.'}
            {status === 'ready' && position && convergencePoint && 'Ready — press Start.'}
            {status === 'running' && 'Sending updates…'}
            {status === 'left' && 'Left the session.'}
          </p>

          <MapContainer center={mapCenter} zoom={13} style={miniMapStyle}>
            <TileLayer
              url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
              attribution='&copy; OpenStreetMap contributors'
            />
            {convergencePoint && <Marker position={[convergencePoint.lat, convergencePoint.lon]} icon={convergenceIcon()} />}
            <ClickMarker enabled={canPickStart} position={position} icon={icon} onPick={(lat, lon) => setPosition({ lat, lon })} />
          </MapContainer>

          <div style={qualityRow}>
            {(['good', 'bad', 'missing'] as Quality[]).map(q => (
              <label key={q} style={qualityLabel(quality === q, status === 'left')}>
                <input
                  type="checkbox"
                  checked={quality === q}
                  disabled={status === 'left'}
                  onChange={() => setQuality(q)}
                  style={{ marginRight: '0.3rem' }}
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
            <button style={leaveBtn(!canLeave)} disabled={!canLeave} onClick={handleLeave}>Leave</button>
          </div>

          <div style={statusLine}>
            {position && <div>{position.lat.toFixed(5)}, {position.lon.toFixed(5)}</div>}
            {distanceRemaining !== null && (
              <div>{distanceRemaining <= ARRIVE_METERS ? 'Arrived' : `${Math.round(distanceRemaining)}m to go`}</div>
            )}
            {lastSentAt && <div>last sent {lastSentAt}</div>}
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
  borderTop: `3px solid ${color}`,
  borderRadius: 8,
  padding: '0.75rem',
  background: '#fff',
  height: '100%',
  boxSizing: 'border-box',
})

const headerRow: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.35rem' }
const initialsBadge = (color: string): React.CSSProperties => ({
  width: 26, height: 26, borderRadius: '50%', background: color, color: '#fff',
  display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0,
  fontSize: '0.7rem', fontWeight: 700, fontFamily: "Arial, 'Helvetica Neue', sans-serif",
})
const nameInput: React.CSSProperties = { flex: 1, minWidth: 0, fontWeight: 600, fontSize: '0.85rem', padding: '0.3rem 0.4rem', border: '1px solid #cbd5e1', borderRadius: 6 }
const nameLabel: React.CSSProperties = { flex: 1, minWidth: 0, fontWeight: 600, fontSize: '0.85rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const removeBtn: React.CSSProperties = { border: 'none', background: 'none', fontSize: '1.1rem', cursor: 'pointer', color: '#94a3b8', lineHeight: 1, padding: '0 0.15rem', flexShrink: 0 }

const statusPillRow: React.CSSProperties = { marginBottom: '0.5rem' }
const statusPill = (status: PhoneStatus): React.CSSProperties => {
  const colors: Record<PhoneStatus, [string, string]> = {
    idle: ['#f1f5f9', '#64748b'],
    joining: ['#e0f2fe', '#0284c7'],
    ready: ['#fef9c3', '#a16207'],
    running: ['#dcfce7', '#16a34a'],
    left: ['#f1f5f9', '#94a3b8'],
  }
  const [bg, fg] = colors[status]
  return { background: bg, color: fg, fontSize: '0.7rem', fontWeight: 700, padding: '2px 8px', borderRadius: 999 }
}

const joinRow: React.CSSProperties = { display: 'flex', gap: '0.4rem', marginBottom: '0.5rem', flexWrap: 'wrap' }
const codeInput: React.CSSProperties = { flex: 1, minWidth: 100, padding: '0.35rem 0.5rem', border: '1px solid #cbd5e1', borderRadius: 6, fontFamily: 'monospace', letterSpacing: '0.05em', fontSize: '0.85rem' }
const errorText: React.CSSProperties = { color: '#dc2626', fontSize: '0.78rem', marginBottom: '0.5rem' }
const hint: React.CSSProperties = { fontSize: '0.76rem', color: '#64748b', margin: '0 0 0.4rem' }
const miniMapStyle: React.CSSProperties = { height: 150, borderRadius: 8, border: '1px solid #e2e8f0', marginBottom: '0.5rem' }

const qualityRow: React.CSSProperties = { display: 'flex', gap: '0.6rem', marginBottom: '0.5rem', fontSize: '0.78rem', flexWrap: 'wrap' }
const qualityLabel = (active: boolean, disabled: boolean): React.CSSProperties => ({
  display: 'flex', alignItems: 'center', cursor: disabled ? 'default' : 'pointer',
  fontWeight: active ? 700 : 400, color: disabled ? '#cbd5e1' : active ? '#1e293b' : '#475569',
})

const actionRow: React.CSSProperties = { display: 'flex', gap: '0.4rem', marginBottom: '0.5rem' }
const primaryBtn = (disabled: boolean): React.CSSProperties => ({
  flex: 1, padding: '0.35rem 0.6rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.8rem',
  background: disabled ? '#e2e8f0' : '#16a34a', color: disabled ? '#94a3b8' : '#fff', cursor: disabled ? 'default' : 'pointer',
})
const pauseBtn: React.CSSProperties = { flex: 1, padding: '0.35rem 0.6rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.8rem', background: '#f59e0b', color: '#fff', cursor: 'pointer' }
const leaveBtn = (disabled: boolean): React.CSSProperties => ({
  flex: 1, padding: '0.35rem 0.6rem', borderRadius: 6, border: '1px solid #fca5a5', fontWeight: 600, fontSize: '0.8rem',
  background: '#fff', color: disabled ? '#e2e8f0' : '#dc2626', cursor: disabled ? 'default' : 'pointer', borderColor: disabled ? '#e2e8f0' : '#fca5a5',
})
const linkBtn: React.CSSProperties = { border: 'none', background: 'none', color: '#2563eb', fontSize: '0.78rem', cursor: 'pointer', textDecoration: 'underline' }
const statusLine: React.CSSProperties = { fontSize: '0.74rem', color: '#64748b', fontFamily: 'monospace' }
