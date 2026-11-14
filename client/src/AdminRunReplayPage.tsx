import { useEffect, useMemo, useRef, useState } from 'react'
import type { MapLibreMap } from 'maplibre-gl'
import RunnerMap from './RunnerMap.tsx'
import TraceToggle from './TraceToggle.tsx'
import ReplayControls from './ReplayControls.tsx'
import MapPlayButton from './MapPlayButton.tsx'
import { useSignalTrailLayer } from './useSignalTrailLayer.ts'
import { useSessionTimeline, type TimelineSource } from './useSessionTimeline.ts'
import { apiHeaders } from './apiHeaders.ts'
import { readAdminToken } from './adminToken.ts'

interface Props {
  serverUrl: string
  runId: string
}

type MetaStatus = 'loading' | 'ok' | 'empty' | 'not-found' | 'unauthorized' | 'error'

/**
 * Admin replay of one Go (casual) run, /admin/go/{runId}: the session map's player, fed from the
 * admin run endpoints instead of an invite code. Opened from the admin page's Go runs tab in the same
 * tab, so the admin token signed in there (sessionStorage, per tab) is still available.
 */
export default function AdminRunReplayPage({ serverUrl, runId }: Props) {
  const mapRef = useRef<MapLibreMap | null>(null)
  const token = readAdminToken()

  const recordingBase = `${serverUrl}/admin/casual/runs/${encodeURIComponent(runId)}`
  const source = useMemo<TimelineSource | null>(
    () => token ? { recordingBase, liveUrl: null, headers: apiHeaders(token, 'web-admin') } : null,
    [recordingBase, token],
  )
  const timeline = useSessionTimeline(source)

  // The timeline hook treats a missing recording as "not started yet"; here it's worth saying why
  // nothing is on the map (wrong token, or the run's positions are past retention).
  const [status, setStatus] = useState<MetaStatus>('loading')
  useEffect(() => {
    if (!source) return
    let cancelled = false
    fetch(`${recordingBase}/recording/meta`, { headers: source.headers })
      .then(async res => {
        if (cancelled) return
        if (res.status === 401) return setStatus('unauthorized')
        if (res.status === 404) return setStatus('not-found')
        if (!res.ok) return setStatus('error')
        const meta = await res.json() as { runStartTimestamp: string | null }
        if (!cancelled) setStatus(meta.runStartTimestamp ? 'ok' : 'empty')
      })
      .catch(() => { if (!cancelled) setStatus('error') })
    return () => { cancelled = true }
  }, [source, recordingBase])

  const [showTrace, setShowTrace] = useState(() => new URLSearchParams(window.location.search).has('trace'))
  const tracePositions = useMemo(
    () => showTrace
      ? timeline.allPositions.filter(p => Date.parse(p.timestamp) <= timeline.virtualNowMs)
      : null,
    [showTrace, timeline.allPositions, timeline.virtualNowMs],
  )
  useSignalTrailLayer(mapRef, tracePositions)

  const runnerName = timeline.allPositions[0]?.runnerName ?? null
  const startedLabel = timeline.runStartMs !== null
    ? new Date(timeline.runStartMs).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
    : null

  const message = !token ? 'Sign in on the admin page first, then open the run from its Go runs tab.'
    : status === 'unauthorized' ? 'The admin token was rejected. Sign in again on the admin page.'
    : status === 'not-found' ? 'This run no longer exists. Go runs are deleted after the retention period (7 days by default).'
    : status === 'empty' ? 'This run has no stored positions.'
    : status === 'error' ? 'Could not load this run.'
    : null

  return (
    <RunnerMap mapRef={mapRef} data={timeline}>
      <div style={header}>
        <a href="/admin" style={backLink}>← Admin</a>
        <span style={title}>
          Go run{runnerName ? ` · ${runnerName}` : ''}{startedLabel ? ` · ${startedLabel}` : ''}
        </span>
      </div>
      {status === 'ok' && (
        <>
          <TraceToggle on={showTrace} onToggle={() => setShowTrace(v => !v)} />
          {timeline.runStartMs !== null && (
            <>
              <ReplayControls timeline={timeline} />
              {!timeline.playing && <MapPlayButton onPlay={timeline.play} />}
            </>
          )}
        </>
      )}
      {message && <div style={statusToast}>{message}</div>}
    </RunnerMap>
  )
}

const header: React.CSSProperties = {
  position: 'absolute',
  top: 10,
  left: '50%',
  transform: 'translateX(-50%)',
  zIndex: 8,
  maxWidth: 'calc(100vw - 120px)',
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  background: '#fff',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '6px 12px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
  color: '#1e293b',
}

const backLink: React.CSSProperties = {
  color: '#3b82f6',
  textDecoration: 'none',
  fontWeight: 600,
  whiteSpace: 'nowrap',
}

const title: React.CSSProperties = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
}

const statusToast: React.CSSProperties = {
  position: 'absolute',
  left: '50%',
  bottom: 88,
  transform: 'translateX(-50%)',
  zIndex: 8,
  maxWidth: 'min(420px, 92vw)',
  background: '#fff',
  color: '#92400e',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '8px 12px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}
