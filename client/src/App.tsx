import { useEffect, useRef, useState } from 'react'
import mapboxgl from 'mapbox-gl'
import 'mapbox-gl/dist/mapbox-gl.css'
import SessionPrompt from './SessionPrompt.tsx'
import Legend from './Legend.tsx'
import MapMenu from './MapMenu.tsx'
import MemberManager from './MemberManager.tsx'
import ReplayControls from './ReplayControls.tsx'
import ReplayPicker from './ReplayPicker.tsx'
import AuthPrompt from './AuthPrompt.tsx'
import { useRunnerMarkers } from './useRunnerMarkers.ts'
import { useReplay } from './useReplay.ts'
import { canManageMembersForRole, canWriteLocationForRole, shouldShowAuthPrompt, shouldShowSessionPrompt } from './sessionState.ts'
import type { AuthResponse, AuthenticatedUser, SessionMembership } from './types.ts'

mapboxgl.accessToken = import.meta.env.VITE_MAPBOX_TOKEN as string

const POLL_INTERVAL_MS: number = parseInt(import.meta.env.VITE_POLL_INTERVAL_MS ?? '2000', 10)
const SERVER_URL: string = import.meta.env.VITE_SERVER_URL ?? '/api'
const AUTH_TOKEN_KEY = 'userAccessToken'
const AUTH_EXPIRES_KEY = 'userAccessTokenExpiresAt'
const AUTH_USER_KEY = 'user'

interface RouteState {
  sessionCode: string | null
  isReplay: boolean
  inviteCode: string | null
}

function parseUrl(): RouteState {
  const parts = window.location.pathname.replace(/^\//, '').split('/')
  const norm = (s: string) => s.toUpperCase() || null
  if (parts[0] === 'join') return { sessionCode: null, isReplay: false, inviteCode: norm(parts[1] ?? '') }
  if (parts[0] === 'replay') return { sessionCode: null, isReplay: true, inviteCode: null }
  if (parts[1] === 'replay') return { sessionCode: norm(parts[0]), isReplay: true, inviteCode: null }
  return { sessionCode: norm(parts[0]), isReplay: false, inviteCode: null }
}

function readStoredAuth(): AuthResponse | null {
  const accessToken = sessionStorage.getItem(AUTH_TOKEN_KEY)
  if (!accessToken) return null

  const expiresAt = sessionStorage.getItem(AUTH_EXPIRES_KEY)
  const expiresAtMs = expiresAt ? Date.parse(expiresAt) : NaN
  if (!Number.isFinite(expiresAtMs) || expiresAtMs <= Date.now()) {
    clearStoredAuth()
    return null
  }

  const storedUser = sessionStorage.getItem(AUTH_USER_KEY)
  let user: AuthenticatedUser = { userId: '', username: '', displayName: '' }
  if (storedUser) {
    try {
      user = JSON.parse(storedUser) as AuthenticatedUser
    } catch {
      sessionStorage.removeItem(AUTH_USER_KEY)
    }
  }

  return { accessToken, expiresAt, user }
}

function clearStoredAuth() {
  sessionStorage.removeItem(AUTH_TOKEN_KEY)
  sessionStorage.removeItem(AUTH_EXPIRES_KEY)
  sessionStorage.removeItem(AUTH_USER_KEY)
  localStorage.removeItem(AUTH_TOKEN_KEY)
  localStorage.removeItem(AUTH_EXPIRES_KEY)
  localStorage.removeItem(AUTH_USER_KEY)
}

export default function App() {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<mapboxgl.Map | null>(null)
  const { sessionCode: initialCode, isReplay, inviteCode } = parseUrl()
  const [sessionCode, setSessionCode] = useState<string | null>(initialCode)
  const [auth, setAuth] = useState<AuthResponse | null>(() => readStoredAuth())
  const [memberships, setMemberships] = useState<SessionMembership[]>([])
  const [membershipsLoaded, setMembershipsLoaded] = useState(false)
  const [menu, setMenu] = useState<{ x: number; y: number; lng: number; lat: number } | null>(null)
  const [showMembers, setShowMembers] = useState(false)
  const accessToken = auth?.accessToken ?? null

  useEffect(() => {
    const map = new mapboxgl.Map({
      container: containerRef.current!,
      style: 'mapbox://styles/mapbox/streets-v12',
      center: [151.2093, -33.8688],
      zoom: 13,
    })

    map.doubleClickZoom.disable()
    if (!isReplay) {
      map.on('dblclick', (e) => {
        setMenu({ x: e.point.x, y: e.point.y, lng: e.lngLat.lng, lat: e.lngLat.lat })
      })
    }

    map.addControl(new mapboxgl.NavigationControl(), 'top-right')
    map.addControl(new mapboxgl.GeolocateControl({
      positionOptions: { enableHighAccuracy: true },
      trackUserLocation: true,
    }), 'top-right')

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [])

  useEffect(() => {
    if (!accessToken) {
      setMemberships([])
      setMembershipsLoaded(false)
      return
    }

    let cancelled = false
    setMembershipsLoaded(false)

    async function loadMemberships() {
      try {
        const response = await fetch(`${SERVER_URL}/me/sessions`, {
          headers: { 'Authorization': `Bearer ${accessToken}` },
        })

        if (cancelled) return

        if (response.status === 401) {
          handleSignOut()
          return
        }

        if (response.ok) {
          setMemberships(await response.json() as SessionMembership[])
        }
      } finally {
        if (!cancelled) setMembershipsLoaded(true)
      }
    }

    void loadMemberships()
    return () => { cancelled = true }
  }, [accessToken])

  const activeMembership = memberships.find(membership => membership.sessionCode === sessionCode) ?? null
  const hasSessionMembership = sessionCode ? activeMembership !== null : false
  const canWriteLocation = canWriteLocationForRole(activeMembership?.role)
  const canManageMembers = canManageMembersForRole(activeMembership?.role)
  const showSessionPrompt = shouldShowSessionPrompt({
    accessToken,
    membershipsLoaded,
    isReplay,
    inviteCode,
    sessionCode,
    hasSessionMembership,
  })

  const replay = useReplay(isReplay && hasSessionMembership ? sessionCode : null, SERVER_URL, accessToken)

  const { offScreenRunners, error: liveError, centerOnRunner, fitAll } = useRunnerMarkers(
    mapRef,
    !isReplay && hasSessionMembership ? sessionCode : null,
    SERVER_URL,
    accessToken,
    POLL_INTERVAL_MS,
    isReplay ? replay.positions : undefined,
    isReplay ? replay.virtualNowMs : undefined,
  )

  async function sendChester(lng: number, lat: number) {
    if (!sessionCode || !accessToken || !canWriteLocation) return
    await fetch(`${SERVER_URL}/location`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${accessToken}`,
      },
      body: JSON.stringify({
        runnerName: 'Chester',
        sessionCode,
        latitude: lat,
        longitude: lng,
        heading: null,
        timestamp: new Date().toISOString(),
      }),
    })
  }

  function handleAuth(nextAuth: AuthResponse) {
    clearStoredAuth()
    sessionStorage.setItem(AUTH_TOKEN_KEY, nextAuth.accessToken)
    sessionStorage.setItem(AUTH_EXPIRES_KEY, nextAuth.expiresAt)
    sessionStorage.setItem(AUTH_USER_KEY, JSON.stringify(nextAuth.user))
    setAuth(nextAuth)
  }

  function handleSignOut() {
    const tokenToRevoke = accessToken
    if (tokenToRevoke) {
      void fetch(`${SERVER_URL}/auth/logout`, {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${tokenToRevoke}` },
      }).catch(() => undefined)
    }

    clearStoredAuth()
    setAuth(null)
    setMemberships([])
    setSessionCode(null)
    setShowMembers(false)
  }

  function handleMembershipSelect(membership: SessionMembership) {
    window.history.replaceState(null, '', isReplay ? `/${membership.sessionCode}/replay` : `/${membership.sessionCode}`)
    setSessionCode(membership.sessionCode)
    setShowMembers(false)
  }

  function handleReplaySelect(code: string) {
    window.history.replaceState(null, '', `/${code}/replay`)
    setSessionCode(code)
  }

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      {auth && (
        <div style={accountBar}>
          <span>{auth.user.displayName || auth.user.username}</span>
          {canManageMembers && (
            <button type="button" style={signOutButton} onClick={() => setShowMembers(true)}>Members</button>
          )}
          <button type="button" style={signOutButton} onClick={handleSignOut}>Sign out</button>
        </div>
      )}
      {!isReplay && <button onClick={fitAll} style={fitAllBtn} title="Fit all">⤢</button>}
      <Legend runners={offScreenRunners} onRunnerClick={centerOnRunner} />
      {menu && canWriteLocation && (
        <MapMenu
          x={menu.x}
          y={menu.y}
          onSendChester={() => sendChester(menu.lng, menu.lat)}
          onClose={() => setMenu(null)}
        />
      )}
      {isReplay && sessionCode && <ReplayControls replay={replay} onFitAll={fitAll} />}
      {liveError && !isReplay && hasSessionMembership && <div style={statusToast}>{liveError}</div>}
      {replay.error && isReplay && hasSessionMembership && <div style={statusToast}>{replay.error}</div>}
      {shouldShowAuthPrompt(accessToken) && <AuthPrompt serverUrl={SERVER_URL} onAuth={handleAuth} />}
      {accessToken && membershipsLoaded && isReplay && !sessionCode && (
        <ReplayPicker memberships={memberships} onSelect={handleReplaySelect} />
      )}
      {accessToken && showSessionPrompt && (
        <SessionPrompt
          serverUrl={SERVER_URL}
          accessToken={accessToken}
          memberships={memberships}
          requestedSessionCode={inviteCode ? undefined : sessionCode}
          initialInviteCode={inviteCode}
          onSelect={handleMembershipSelect}
          onMembershipsChanged={setMemberships}
        />
      )}
      {accessToken && membershipsLoaded && isReplay && sessionCode && !hasSessionMembership && !inviteCode && (
        <SessionPrompt
          serverUrl={SERVER_URL}
          accessToken={accessToken}
          memberships={memberships}
          requestedSessionCode={sessionCode}
          isReplay
          onSelect={handleMembershipSelect}
          onMembershipsChanged={setMemberships}
        />
      )}
      {accessToken && canManageMembers && activeMembership && showMembers && (
        <MemberManager
          serverUrl={SERVER_URL}
          accessToken={accessToken}
          membership={activeMembership}
          onClose={() => setShowMembers(false)}
        />
      )}
    </>
  )
}

const fitAllBtn: React.CSSProperties = {
  position: 'absolute',
  bottom: 32,
  right: 12,
  width: 36,
  height: 36,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  fontSize: '1.1rem',
  color: '#333',
  padding: 0,
}

const accountBar: React.CSSProperties = {
  position: 'absolute',
  top: 12,
  left: 12,
  zIndex: 8,
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  background: '#fff',
  color: '#24292f',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '6px 8px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.85rem',
}

const signOutButton: React.CSSProperties = {
  border: '1px solid #d0d7de',
  borderRadius: 4,
  background: '#f6f8fa',
  color: '#24292f',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.8rem',
  padding: '3px 6px',
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
