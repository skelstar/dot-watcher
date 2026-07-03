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
import LegalPage from './LegalPage.tsx'
import AdminPanel from './AdminPanel.tsx'
import AccountSettings from './AccountSettings.tsx'
import { useRunnerMarkers } from './useRunnerMarkers.ts'
import { useReplay } from './useReplay.ts'
import { canManageMembersForRole, canWriteLocationForRole, shouldShowAuthPrompt, shouldShowSessionPrompt } from './sessionState.ts'
import type { AuthResponse, AuthenticatedUser, SessionMembership } from './types.ts'

mapboxgl.accessToken = import.meta.env.VITE_MAPBOX_TOKEN as string

const POLL_INTERVAL_MS: number = parseInt(import.meta.env.VITE_POLL_INTERVAL_MS ?? '2000', 10)
const SERVER_URL: string = import.meta.env.VITE_SERVER_URL ?? '/api'
const APP_VERSION = import.meta.env.VITE_APP_VERSION ?? 'v-local'
const APP_UPDATED_AT = import.meta.env.VITE_APP_UPDATED_AT ?? 'Updated local'
const VERSION_LABEL = `${APP_VERSION} · ${APP_UPDATED_AT}`
const AUTH_TOKEN_KEY = 'userAccessToken'
const AUTH_EXPIRES_KEY = 'userAccessTokenExpiresAt'
const AUTH_USER_KEY = 'user'

interface RouteState {
  sessionName: string | null
  isReplay: boolean
  inviteCode: string | null
  autoJoin: boolean
  legalPage: 'privacy' | 'terms' | null
  isAdmin: boolean
}

function parseUrl(): RouteState {
  const parts = window.location.pathname.replace(/^\//, '').split('/')
  const norm = (s: string) => s.toUpperCase() || null
  if (parts[0] === 'admin') return { sessionName: null, isReplay: false, inviteCode: null, autoJoin: false, legalPage: null, isAdmin: true }
  if (parts[0] === 'privacy') return { sessionName: null, isReplay: false, inviteCode: null, autoJoin: false, legalPage: 'privacy', isAdmin: false }
  if (parts[0] === 'terms') return { sessionName: null, isReplay: false, inviteCode: null, autoJoin: false, legalPage: 'terms', isAdmin: false }
  if (parts[0] === 'code') return { sessionName: null, isReplay: parts[2] === 'replay', inviteCode: norm(parts[1] ?? ''), autoJoin: true, legalPage: null, isAdmin: false }
  if (parts[0] === 'join') return { sessionName: null, isReplay: parts[2] === 'replay', inviteCode: norm(parts[1] ?? ''), autoJoin: false, legalPage: null, isAdmin: false }
  if (parts[0] === 'replay') return { sessionName: null, isReplay: true, inviteCode: null, autoJoin: false, legalPage: null, isAdmin: false }
  if (parts[1] === 'replay') return { sessionName: norm(parts[0]), isReplay: true, inviteCode: null, autoJoin: false, legalPage: null, isAdmin: false }
  return { sessionName: norm(parts[0]), isReplay: false, inviteCode: null, autoJoin: false, legalPage: null, isAdmin: false }
}

function readStoredAuth(): AuthResponse | null {
  const accessToken = localStorage.getItem(AUTH_TOKEN_KEY)
  if (!accessToken) return null

  const expiresAt = localStorage.getItem(AUTH_EXPIRES_KEY)
  const expiresAtMs = expiresAt ? Date.parse(expiresAt) : NaN
  if (!Number.isFinite(expiresAtMs) || expiresAtMs <= Date.now()) {
    clearStoredAuth()
    return null
  }

  const storedUser = localStorage.getItem(AUTH_USER_KEY)
  let user: AuthenticatedUser = { userId: '', username: '', displayName: '' }
  if (storedUser) {
    try {
      user = JSON.parse(storedUser) as AuthenticatedUser
    } catch {
      localStorage.removeItem(AUTH_USER_KEY)
    }
  }

  return { accessToken, expiresAt, user }
}

function clearStoredAuth() {
  localStorage.removeItem(AUTH_TOKEN_KEY)
  localStorage.removeItem(AUTH_EXPIRES_KEY)
  localStorage.removeItem(AUTH_USER_KEY)
  sessionStorage.removeItem(AUTH_TOKEN_KEY)
  sessionStorage.removeItem(AUTH_EXPIRES_KEY)
  sessionStorage.removeItem(AUTH_USER_KEY)
}

export default function App() {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<mapboxgl.Map | null>(null)
  const { sessionName: initialName, isReplay, inviteCode, autoJoin, legalPage, isAdmin } = parseUrl()
  const [sessionName, setSessionName] = useState<string | null>(initialName)
  const [auth, setAuth] = useState<AuthResponse | null>(() => readStoredAuth())
  const [memberships, setMemberships] = useState<SessionMembership[]>([])
  const [membershipsLoaded, setMembershipsLoaded] = useState(false)
  const [menu, setMenu] = useState<{ x: number; y: number; lng: number; lat: number } | null>(null)
  const [showMembers, setShowMembers] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const accessToken = auth?.accessToken ?? null

  useEffect(() => {
    if (legalPage || !containerRef.current) return

    const map = new mapboxgl.Map({
      container: containerRef.current,
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
  }, [isReplay, legalPage])

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

  const activeMembership = memberships.find(membership => membership.sessionName === sessionName) ?? null
  const sessionId = activeMembership?.sessionId ?? null
  const hasSessionMembership = sessionName ? activeMembership !== null : false
  const canWriteLocation = canWriteLocationForRole(activeMembership?.role)
  const canManageMembers = canManageMembersForRole(activeMembership?.role)
  const showSessionPrompt = shouldShowSessionPrompt({
    accessToken,
    membershipsLoaded,
    isReplay,
    inviteCode,
    sessionName,
    hasSessionMembership,
  })

  const replay = useReplay(isReplay && hasSessionMembership ? sessionId : null, SERVER_URL, accessToken)

  const { offScreenRunners, error: liveError, centerOnRunner, fitAll } = useRunnerMarkers(
    mapRef,
    !isReplay && hasSessionMembership ? sessionId : null,
    SERVER_URL,
    accessToken,
    POLL_INTERVAL_MS,
    isReplay ? replay.positions : undefined,
    isReplay ? replay.virtualNowMs : undefined,
    !isReplay && !accessToken ? inviteCode : null,
  )

  async function sendChester(lng: number, lat: number) {
    if (!sessionId || !accessToken || !canWriteLocation) return
    await fetch(`${SERVER_URL}/location`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${accessToken}`,
      },
      body: JSON.stringify({
        runnerName: 'Chester',
        sessionId,
        latitude: lat,
        longitude: lng,
        heading: null,
        timestamp: new Date().toISOString(),
      }),
    })
  }

  function handleAuth(nextAuth: AuthResponse) {
    clearStoredAuth()
    localStorage.setItem(AUTH_TOKEN_KEY, nextAuth.accessToken)
    localStorage.setItem(AUTH_EXPIRES_KEY, nextAuth.expiresAt)
    localStorage.setItem(AUTH_USER_KEY, JSON.stringify(nextAuth.user))
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
    setSessionName(null)
    setShowMembers(false)
    setShowSettings(false)
  }

  async function handleDeleteAccount() {
    if (!accessToken) return
    let response: Response
    try {
      response = await fetch(`${SERVER_URL}/me`, {
        method: 'DELETE',
        headers: { 'Authorization': `Bearer ${accessToken}` },
      })
    } catch {
      window.alert('Delete account failed: network error')
      throw new Error('Delete account failed: network error')
    }

    if (!response.ok) {
      window.alert(`Delete account failed: HTTP ${response.status}`)
      throw new Error(`Delete account failed: HTTP ${response.status}`)
    }

    clearStoredAuth()
    setAuth(null)
    setMemberships([])
    setSessionName(null)
    setShowMembers(false)
    setShowSettings(false)
  }

  function handleMembershipSelect(membership: SessionMembership) {
    window.history.replaceState(null, '', isReplay ? `/${membership.sessionName}/replay` : `/${membership.sessionName}`)
    setSessionName(membership.sessionName)
    setShowMembers(false)
  }

  function handleReplaySelect(membership: SessionMembership) {
    window.history.replaceState(null, '', `/${membership.sessionName}/replay`)
    setSessionName(membership.sessionName)
  }

  if (isAdmin) {
    return <AdminPanel serverUrl={SERVER_URL} />
  }

  if (legalPage) {
    return <LegalPage kind={legalPage} />
  }

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      <div style={versionBadge}>
        <span>{VERSION_LABEL}</span>
        <a href="/privacy" style={legalLink}>Privacy</a>
        <a href="/terms" style={legalLink}>Terms</a>
      </div>
      {auth && (
        <div style={accountBar}>
          <span>{auth.user.displayName || auth.user.username}</span>
          {canManageMembers && (
            <button type="button" style={signOutButton} onClick={() => setShowMembers(true)}>Members</button>
          )}
          <button type="button" style={signOutButton} onClick={() => setShowSettings(true)}>Settings</button>
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
      {isReplay && sessionName && <ReplayControls replay={replay} onFitAll={fitAll} />}
      {liveError && !isReplay && (hasSessionMembership || (!accessToken && inviteCode)) && <div style={statusToast}>{liveError}</div>}
      {replay.error && isReplay && hasSessionMembership && <div style={statusToast}>{replay.error}</div>}
      {shouldShowAuthPrompt(accessToken, inviteCode) && <AuthPrompt serverUrl={SERVER_URL} onAuth={handleAuth} />}
      {accessToken && membershipsLoaded && isReplay && !sessionName && (
        <ReplayPicker memberships={memberships} onSelect={handleReplaySelect} />
      )}
      {accessToken && showSessionPrompt && (
        <SessionPrompt
          serverUrl={SERVER_URL}
          accessToken={accessToken}
          memberships={memberships}
          requestedSessionName={inviteCode ? undefined : sessionName}
          initialInviteCode={inviteCode}
          autoJoinDisplayName={autoJoin ? auth?.user.displayName ?? '' : undefined}
          onSelect={handleMembershipSelect}
          onMembershipsChanged={setMemberships}
        />
      )}
      {accessToken && membershipsLoaded && isReplay && sessionName && !hasSessionMembership && !inviteCode && (
        <SessionPrompt
          serverUrl={SERVER_URL}
          accessToken={accessToken}
          memberships={memberships}
          requestedSessionName={sessionName}
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
      {auth && showSettings && (
        <AccountSettings
          displayName={auth.user.displayName || auth.user.username}
          onClose={() => setShowSettings(false)}
          onDeleteAccount={handleDeleteAccount}
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

const versionBadge: React.CSSProperties = {
  position: 'absolute',
  left: 12,
  bottom: 12,
  zIndex: 7,
  maxWidth: 'min(620px, calc(100vw - 96px))',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  background: 'rgba(255,255,255,0.92)',
  color: '#57606a',
  borderRadius: 4,
  boxShadow: '0 0 0 1px rgba(0,0,0,0.1)',
  padding: '4px 7px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.75rem',
}

const legalLink: React.CSSProperties = {
  flex: '0 0 auto',
  color: '#1f6feb',
  textDecoration: 'none',
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
