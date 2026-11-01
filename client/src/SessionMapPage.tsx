import { useEffect, useMemo, useRef, useState } from 'react'
import type { MapLibreMap } from 'maplibre-gl'
import RunnerMap from './RunnerMap.tsx'
import InvalidInvitePrompt from './InvalidInvitePrompt.tsx'
import SignInPanel from './SignInPanel.tsx'
import AccountButton from './AccountButton.tsx'
import TraceToggle from './TraceToggle.tsx'
import ReplayControls from './ReplayControls.tsx'
import MapPlayButton from './MapPlayButton.tsx'
import InitialsBadge from './components/InitialsBadge.tsx'
import { useRouteLayer } from './useRouteLayer.ts'
import { useSignalTrailLayer } from './useSignalTrailLayer.ts'
import { useSessionTimeline } from './useSessionTimeline.ts'
import { parseGpxCoordinates } from './gpx.ts'
import { apiHeaders } from './apiHeaders.ts'
import { useAuthSession } from './useAuthSession.ts'

interface Props {
  serverUrl: string
  inviteCode: string | null
}

/** The map for a session, reached by its invite code (/code/{code}): live, or replayed from the recording. */
export default function SessionMapPage({ serverUrl, inviteCode }: Props) {
  const mapRef = useRef<MapLibreMap | null>(null)
  const [routeCoordinates, setRouteCoordinates] = useState<[number, number][] | null>(null)

  // Optional viewer sign-in, reached only through the account button in the top-right column.
  // Anonymous viewing is unaffected; the panel just stores a session that apiHeaders() then sends.
  const authSession = useAuthSession()
  const [accountOpen, setAccountOpen] = useState(false)

  const timeline = useSessionTimeline(serverUrl, inviteCode)

  useRouteLayer(mapRef, routeCoordinates, timeline.runStartMs === null)
  // Signal trace (toggle button, or ?trace in the URL to start with it on): plots every position
  // up to the playhead — a blue dot for a normal connection, the runner dot's satellite badge for
  // an ultra-constrained path, and a red X where a satellite read probably failed.
  const [showTrace, setShowTrace] = useState(() => new URLSearchParams(window.location.search).has('trace'))
  const tracePositions = useMemo(
    () => showTrace
      ? timeline.allPositions.filter(p => Date.parse(p.timestamp) <= timeline.virtualNowMs)
      : null,
    [showTrace, timeline.allPositions, timeline.virtualNowMs],
  )
  useSignalTrailLayer(mapRef, tracePositions)

  const routeBase = inviteCode ? `${serverUrl}/session-invites/${inviteCode}` : null

  useEffect(() => {
    setRouteCoordinates(null)
    if (!routeBase) return

    let cancelled = false
    async function loadRoute() {
      const response = await fetch(`${routeBase}/route`, {
        headers: apiHeaders(),
      })
      if (cancelled || !response.ok) return
      const gpx = await response.text()
      setRouteCoordinates(parseGpxCoordinates(gpx))
    }

    void loadRoute()
    return () => { cancelled = true }
  }, [routeBase])

  // Who created the session, for the "hasn't started yet" toast — from the tokenless
  // session-info endpoint, the same invite-code-only surface everything else here uses.
  const [ownerDisplayName, setOwnerDisplayName] = useState<string | null>(null)
  useEffect(() => {
    setOwnerDisplayName(null)
    if (!inviteCode) return

    let cancelled = false
    fetch(`${serverUrl}/session-invites/${inviteCode}`)
      .then(res => (res.ok ? res.json() as Promise<{ ownerDisplayName: string }> : null))
      .then(info => { if (!cancelled && info) setOwnerDisplayName(info.ownerDisplayName) })
      .catch(() => undefined)
    return () => { cancelled = true }
  }, [serverUrl, inviteCode])

  return (
    <RunnerMap mapRef={mapRef} data={timeline}>
      <AccountButton displayName={authSession?.user.displayName ?? null} onClick={() => setAccountOpen(true)} />
      {inviteCode && !timeline.invalidInvite && (
        <TraceToggle on={showTrace} onToggle={() => setShowTrace(v => !v)} />
      )}
      {!timeline.invalidInvite && timeline.runStartMs !== null && inviteCode && (
        <>
          <ReplayControls timeline={timeline} />
          {/* "following" alone would hide this on a finished session's very first load: scrubTimeMs
              starts null (following=true) same as it does mid-live-watching, but there's no live
              edge to follow once the session's over — isLive tells those two states apart. */}
          {!timeline.playing && (!timeline.isLive || !timeline.following) && (
            <MapPlayButton onPlay={timeline.play} />
          )}
        </>
      )}
      {!timeline.invalidInvite && !timeline.error && timeline.runStartMs === null && inviteCode && (
        <div style={notStartedToast}>
          {ownerDisplayName && (
            <div style={notStartedToastLine}>
              <span>Created by </span>
              <InitialsBadge name={ownerDisplayName} />
            </div>
          )}
          <div style={notStartedToastLine}>The session hasn&rsquo;t started yet.</div>
        </div>
      )}
      {timeline.error && !timeline.invalidInvite && inviteCode && (
        <div style={statusToast}>{timeline.error}</div>
      )}
      {accountOpen && <SignInPanel serverUrl={serverUrl} onClose={() => setAccountOpen(false)} />}
      {timeline.invalidInvite && (
        <InvalidInvitePrompt
          message={
            inviteCode
              ? `Invite code '${inviteCode}' not found. Session might have expired or been deleted.`
              : timeline.error ?? 'Invite not found.'
          }
        />
      )}
    </RunnerMap>
  )
}

const notStartedToast: React.CSSProperties = {
  position: 'absolute',
  left: '50%',
  bottom: 100,
  transform: 'translateX(-50%)',
  zIndex: 8,
  maxWidth: '92vw',
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  gap: 4,
  background: '#fff',
  color: '#57606a',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '8px 14px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}

const notStartedToastLine: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
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
