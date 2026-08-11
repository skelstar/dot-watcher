import { useEffect, useRef, useState, useCallback } from 'react'
import { initialsFor, runnerColour } from './lib/icons'
import { computeBearing, distanceMeters, destinationPoint, advanceAlongRoute, ROUTE_START_PROGRESS, ARRIVE_METERS, type RouteProgress } from './lib/geo'
import { parseGpxTrackpoints } from './lib/gpx'
import {
  registerParticipant,
  joinSession,
  postLocation,
  leaveSession,
  type AuthedUser,
} from './lib/dotwatcherApi'
import type { LatLon, Quality, PhoneStatus, PhoneSnapshot } from './lib/types'

export type { LatLon, Quality, PhoneStatus, PhoneSnapshot }

// Real iOS uses 90s while isUltraConstrained (LocationManager.ultraConstrainedInterval) —
// satellite bursts are slower/costlier than a normal handshake. 20s here instead: long enough to
// read as clearly different from the shared "Update every" cadence above (max 15s), short enough
// that a tester isn't stuck waiting 90s per cycle to see the effect.
const SATELLITE_TICK_MS = 20_000

type Props = {
  id: number
  index: number
  initialDisplayName?: string
  autoJoin?: boolean
  defaultInviteCode: string
  position: LatLon | null
  convergencePoint: LatLon | null
  route: LatLon[] | null
  speedMps: number
  tickMs: number
  onSnapshot: (snapshot: PhoneSnapshot) => void
  onPositionChange: (id: number, point: LatLon) => void
  onRequestStartPoint: (id: number, displayName: string) => void
  onRouteChange: (id: number, route: LatLon[] | null) => void
  onRemove: (id: number) => void
}

export default function PhoneSimulator({
  id, index, initialDisplayName, autoJoin, defaultInviteCode, position, convergencePoint, route, speedMps, tickMs,
  onSnapshot, onPositionChange, onRequestStartPoint, onRouteChange, onRemove,
}: Props) {
  // Short by default — this is the actual displayName registered with the server, so it's
  // what the real client (and its own marker labels) will show too, not just a local label.
  const [displayName, setDisplayName] = useState(initialDisplayName ?? `P${index + 1}`)
  const [inviteCode, setInviteCode] = useState(defaultInviteCode)
  const [status, setStatus] = useState<PhoneStatus>('idle')
  const [quality, setQuality] = useState<Quality>('good')
  const [heading, setHeading] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lastSentAt, setLastSentAt] = useState<string | null>(null)
  const [routeFileName, setRouteFileName] = useState<string | null>(null)
  const [routeError, setRouteError] = useState<string | null>(null)

  const userRef = useRef<AuthedUser | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const qualityRef = useRef(quality)
  const positionRef = useRef(position)
  const convergenceRef = useRef(convergencePoint)
  const routeRef = useRef(route)
  const routeProgressRef = useRef<RouteProgress>(ROUTE_START_PROGRESS)
  const inFlightRef = useRef(false)
  // A self-rescheduling setTimeout rather than a fixed setInterval — the delay before each next
  // tick is decided fresh at schedule time (see scheduleNextTick), the same self-correcting
  // approach as the real iOS app's trackingLoop, so toggling quality to/from 'satellite' mid-run
  // changes the cadence starting from the very next tick without needing to restart.
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const tickMsRef = useRef(tickMs)
  const hasAutoJoinedRef = useRef(false)

  const color = runnerColour(displayName)

  useEffect(() => { qualityRef.current = quality }, [quality])
  useEffect(() => { tickMsRef.current = tickMs }, [tickMs])
  useEffect(() => { positionRef.current = position }, [position])
  useEffect(() => { convergenceRef.current = convergencePoint }, [convergencePoint])
  // A new/cleared route always restarts progress from its first point — resuming mid-route from
  // a stale index would make no sense once the underlying path itself has changed.
  useEffect(() => {
    routeRef.current = route
    routeProgressRef.current = ROUTE_START_PROGRESS
  }, [route])

  useEffect(() => {
    onSnapshot({ id, displayName, status, quality, position, heading })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, displayName, status, quality, position, heading])

  useEffect(() => () => { if (timeoutRef.current) clearTimeout(timeoutRef.current) }, [])

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

  // Auto-joins once on mount for phones seeded by "Create session" — skips the manual
  // invite-code-then-Join step for the default set of phones. Guarded the same way as the
  // start-point request above, otherwise StrictMode's double-invoke would register two
  // accounts and join twice.
  useEffect(() => {
    if (hasAutoJoinedRef.current) return
    if (autoJoin && defaultInviteCode.trim()) {
      hasAutoJoinedRef.current = true
      handleJoin()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

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
    const activeRoute = routeRef.current
    // The interval that actually governs this tick's spacing (see scheduleNextTick) — using the
    // shared tickMs here instead would make satellite-mode phones appear to crawl, since they'd
    // be sized for a tick five times more frequent than the one actually firing.
    const tickIntervalMs = qualityRef.current === 'satellite' ? SATELLITE_TICK_MS : tickMsRef.current
    const stepMeters = speedMps * (tickIntervalMs / 1000)

    // 'satellite' moves the same as 'good' — it's a network-type flag, not a GPS-quality issue
    // (see the Quality type) — only the isUltraConstrained flag on the post itself differs.
    const movesNormally = qualityRef.current === 'good' || qualityRef.current === 'satellite'

    if (movesNormally && activeRoute && activeRoute.length > 1) {
      // A route takes priority over the convergence point when both are set — it's the more
      // specific instruction for this phone.
      const result = advanceAlongRoute(activeRoute, routeProgressRef.current, stepMeters)
      next = result.position
      nextHeading = result.heading
      routeProgressRef.current = result.progress
    } else if (movesNormally && target) {
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
      await postLocation(user, sessionId, next.lat, next.lon, nextHeading, qualityRef.current === 'satellite')
      onPositionChange(id, next)
      setHeading(nextHeading)
      setLastSentAt(new Date().toLocaleTimeString())
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Location post failed.')
    } finally {
      inFlightRef.current = false
    }
  }, [id, speedMps, onPositionChange])

  // Reads quality/tickMs fresh (via refs) at each reschedule rather than once at Start time, so
  // flipping a phone to/from 'satellite' mid-run changes its cadence starting the very next tick.
  const scheduleNextTick = useCallback(() => {
    const delay = qualityRef.current === 'satellite' ? SATELLITE_TICK_MS : tickMsRef.current
    timeoutRef.current = setTimeout(async () => {
      await tick()
      scheduleNextTick()
    }, delay)
  }, [tick])

  function handleStart() {
    if (!position || status === 'running') return
    setStatus('running')
    scheduleNextTick()
  }

  function handlePause() {
    if (timeoutRef.current) { clearTimeout(timeoutRef.current); timeoutRef.current = null }
    setStatus('ready')
  }

  async function handleLeave() {
    if (timeoutRef.current) { clearTimeout(timeoutRef.current); timeoutRef.current = null }
    const user = userRef.current
    const sessionId = sessionIdRef.current
    setStatus('left')
    if (user && sessionId) {
      try { await leaveSession(user, sessionId) } catch { /* best-effort */ }
    }
  }

  function handleRemove() {
    if (timeoutRef.current) clearTimeout(timeoutRef.current)
    if (status !== 'left' && status !== 'idle') {
      const user = userRef.current
      const sessionId = sessionIdRef.current
      if (user && sessionId) leaveSession(user, sessionId).catch(() => {})
    }
    onRemove(id)
  }

  function handleRouteFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setRouteError(null)
    const reader = new FileReader()
    reader.onload = evt => {
      try {
        const points = parseGpxTrackpoints(evt.target?.result as string)
        if (points.length === 0) {
          setRouteError('No track points found.')
          return
        }
        setRouteFileName(file.name)
        onRouteChange(id, points) // a new upload always replaces whatever route this phone had
      } catch (err) {
        setRouteError(err instanceof Error ? err.message : 'Failed to parse GPX.')
      }
    }
    reader.readAsText(file)
  }

  function handleClearRoute() {
    setRouteFileName(null)
    setRouteError(null)
    onRouteChange(id, null)
  }

  // A route substitutes for the convergence point — a phone following its own path doesn't need
  // the shared target too.
  const canStart = status === 'ready' && position !== null && (convergencePoint !== null || (route !== null && route.length > 1))
  const canLeave = status === 'ready' || status === 'running'
  const canPickPosition = status !== 'left'

  // A route's own end point is this phone's target once it has one, in place of the shared
  // convergence point — matches the priority order tick() and canStart use.
  const target = route && route.length > 0 ? route[route.length - 1] : convergencePoint
  const distanceRemaining = position && target
    ? distanceMeters(position.lat, position.lon, target.lat, target.lon)
    : null

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

      <div style={positionRow}>
        {position
          ? <span style={positionText}>{position.lat.toFixed(5)}, {position.lon.toFixed(5)}</span>
          : <span style={positionMuted}>No start point set</span>}
        <button style={changePointBtn} disabled={!canPickPosition} onClick={() => onRequestStartPoint(id, displayName)}>
          {position ? 'Change' : 'Choose start point'}
        </button>
      </div>

      <div style={positionRow}>
        {routeFileName
          ? <span style={positionText}>{routeFileName} ({route?.length ?? 0} pts)</span>
          : <span style={positionMuted}>No route</span>}
        <label style={changePointBtn}>
          {routeFileName ? 'Change' : 'Upload GPX'}
          <input type="file" accept=".gpx" onChange={handleRouteFile} disabled={!canPickPosition} style={{ display: 'none' }} />
        </label>
        {routeFileName && (
          <button style={removeBtn} onClick={handleClearRoute} disabled={!canPickPosition} title="Clear route">×</button>
        )}
      </div>
      {routeError && <div style={errorText}>{routeError}</div>}

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
            {status === 'ready' && !position && 'Choose a starting point above.'}
            {status === 'ready' && position && !target && 'Waiting for the convergence point (or upload a route).'}
            {status === 'ready' && position && target && 'Ready — press Start.'}
            {status === 'running' && 'Sending updates…'}
            {status === 'left' && 'Left the session.'}
          </p>

          <div style={qualityRow}>
            {(['good', 'bad', 'missing', 'satellite'] as Quality[]).map(q => (
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
    case 'satellite': return 'Satellite'
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

const positionRow: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.5rem', flexWrap: 'wrap' }
const positionText: React.CSSProperties = { fontSize: '0.74rem', color: '#475569', fontFamily: 'monospace' }
const positionMuted: React.CSSProperties = { fontSize: '0.78rem', color: '#94a3b8' }
const changePointBtn: React.CSSProperties = {
  padding: '0.25rem 0.6rem', borderRadius: 6, border: '1px solid #cbd5e1', background: '#fff',
  color: '#1e293b', fontSize: '0.74rem', fontWeight: 600, cursor: 'pointer',
}

const joinRow: React.CSSProperties = { display: 'flex', gap: '0.4rem', marginBottom: '0.5rem', flexWrap: 'wrap' }
const codeInput: React.CSSProperties = { flex: 1, minWidth: 100, padding: '0.35rem 0.5rem', border: '1px solid #cbd5e1', borderRadius: 6, fontFamily: 'monospace', letterSpacing: '0.05em', fontSize: '0.85rem' }
const errorText: React.CSSProperties = { color: '#dc2626', fontSize: '0.78rem', marginBottom: '0.5rem' }
const hint: React.CSSProperties = { fontSize: '0.76rem', color: '#64748b', margin: '0 0 0.4rem' }

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
