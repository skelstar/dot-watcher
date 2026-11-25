import { useEffect, useRef, useState, useCallback } from 'react'
import { initialsFor, runnerColour } from './lib/icons'
import { computeBearing, distanceMeters, destinationPoint, advanceAlongRoute, ROUTE_START_PROGRESS, ARRIVE_METERS, type RouteProgress } from './lib/geo'
import { parseGpxTrackpoints } from './lib/gpx'
import {
  registerParticipant,
  joinSession,
  joinGroup,
  postLocation,
  leaveSession,
  enableGo,
  startGoRun,
  stopGoRun,
  createWatchLink,
  watchOnce,
  reportAppState,
  CLIENT_URL,
  type AppState,
  type AuthedUser,
  type WatchLink,
} from './lib/dotwatcherApi'
import type { LatLon, Quality, BatteryMode, PhoneStatus, PhoneSnapshot } from './lib/types'

export type { LatLon, Quality, BatteryMode, PhoneStatus, PhoneSnapshot }

// Fixed values rather than a free-entry percentage — this only needs to exercise the two
// threshold badges in the real client (see .ai/plans/battery-level-reporting.md), not simulate
// an actual drain curve. Comfortably inside each band so which badge should show is unambiguous.
const BATTERY_LEVEL_FOR_MODE: Record<BatteryMode, number | undefined> = {
  normal: undefined,
  low: 25,
  critical: 5,
}

// Real iOS uses 90s while isUltraConstrained (LocationManager.ultraConstrainedInterval) —
// satellite bursts are slower/costlier than a normal handshake. 20s here instead: long enough to
// read as clearly different from the shared "Update every" cadence above (max 15s), short enough
// that a tester isn't stuck waiting 90s per cycle to see the effect.
const SATELLITE_TICK_MS = 20_000

// A phone goes back to the background this long after the last thing done in the app, like one put
// back in a pocket.
const AUTO_BACKGROUND_MS = 10_000

// How often a pretend viewer polls the watch link: the watch page's own POLL_MS (client/src/WatchPage.tsx).
const VIEWER_POLL_MS = 10_000

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
  // The other phones with accounts, and whether each is this phone's crew (kept by the page, as crew is
  // both ways). Ticking or unticking one changes it for both phones.
  crewOptions?: { phoneId: number; displayName: string; isCrew: boolean; pending: boolean; busy: boolean }[]
  onToggleCrew?: (otherPhoneId: number) => void
}

export default function PhoneSimulator({
  id, index, initialDisplayName, autoJoin, defaultInviteCode, position, convergencePoint, route, speedMps, tickMs,
  onSnapshot, onPositionChange, onRequestStartPoint, onRouteChange, onRemove, crewOptions = [], onToggleCrew,
}: Props) {
  // Short by default — this is the actual displayName registered with the server, so it's
  // what the real client (and its own marker labels) will show too, not just a local label.
  const [displayName, setDisplayName] = useState(initialDisplayName ?? `P${index + 1}`)
  const [inviteCode, setInviteCode] = useState(defaultInviteCode)
  const [status, setStatus] = useState<PhoneStatus>('idle')
  const [quality, setQuality] = useState<Quality>('good')
  const [batteryMode, setBatteryMode] = useState<BatteryMode>('normal')
  const [heading, setHeading] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [lastSentAt, setLastSentAt] = useState<string | null>(null)
  const [routeFileName, setRouteFileName] = useState<string | null>(null)
  const [routeError, setRouteError] = useState<string | null>(null)
  // 'go': out on its own, like the app's Go sign, instead of in a session. Positions then go to the
  // casual session and Leave becomes Stop.
  const [mode, setMode] = useState<'session' | 'go'>('session')
  // A group this phone's account is in (see joinGroup). Separate from mode: as in the app, a phone can be
  // in a group before going out, and stays in it when the Go run stops. Its own code box, so joining a
  // group while out doesn't need the session join row.
  const [group, setGroup] = useState<{ sessionId: string; name: string } | null>(null)
  const [groupCode, setGroupCode] = useState(defaultInviteCode)
  const [groupBusy, setGroupBusy] = useState(false)
  const [watchLink, setWatchLink] = useState<WatchLink | null>(null)
  const [viewerCount, setViewerCount] = useState(0)
  // One poll timer per pretend viewer still watching.
  const viewersRef = useRef<ReturnType<typeof setInterval>[]>([])
  const [watchBusy, setWatchBusy] = useState(false)
  // Whether the pretend app is on screen. Only reported to the server (for the Activity tab): like the
  // real app, a phone in the background keeps sending positions. It starts closed; doing anything in the
  // app brings it to the front, and (unless switched off) it goes back AUTO_BACKGROUND_MS after that.
  const [appState, setAppState] = useState<AppState>('back')
  const appStateRef = useRef<AppState>('back')
  const [autoBackground, setAutoBackground] = useState(true)
  const autoBackgroundRef = useRef(autoBackground)
  const backgroundTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Made the first time the app is opened (see ensureAccount).
  const [hasAccount, setHasAccount] = useState(false)
  // The 10 s auto-background waits until the phone is out or joined, so the app doesn't close while an
  // invite code is being typed.
  const statusRef = useRef(status)
  useEffect(() => {
    statusRef.current = status
    if (status === 'ready' || status === 'running') armBackgroundTimer()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status])

  const userRef = useRef<AuthedUser | null>(null)
  const sessionIdRef = useRef<string | null>(null)
  const qualityRef = useRef(quality)
  const batteryModeRef = useRef(batteryMode)
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
  // A Go phone has no session, so often no convergence point: with no route either it walks in a
  // straight line on this bearing, picked when it goes out.
  const goBearingRef = useRef<number | null>(null)

  const color = runnerColour(displayName)

  useEffect(() => { qualityRef.current = quality }, [quality])
  useEffect(() => { batteryModeRef.current = batteryMode }, [batteryMode])
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
    // userRef is set just before status leaves 'joining', so the status change carries the account.
    onSnapshot({
      id, displayName, status, quality, position, heading,
      userId: userRef.current?.userId ?? null, user: userRef.current,
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, displayName, status, quality, position, heading, hasAccount])

  useEffect(() => () => {
    if (timeoutRef.current) clearTimeout(timeoutRef.current)
    if (backgroundTimerRef.current) clearTimeout(backgroundTimerRef.current)
    viewersRef.current.forEach(clearInterval)
  }, [])
  useEffect(() => {
    autoBackgroundRef.current = autoBackground
    if (autoBackground) armBackgroundTimer()
    else if (backgroundTimerRef.current) { clearTimeout(backgroundTimerRef.current); backgroundTimerRef.current = null }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoBackground])

  // The phone's account: made the first time the app is opened, as a real user is signed in before they
  // open the app, so "Opened the app" is recorded when it happens rather than when they press Go or
  // Join. One promise, so a quick double tap doesn't make two.
  const accountRef = useRef<Promise<AuthedUser> | null>(null)
  function ensureAccount(): Promise<AuthedUser> {
    if (userRef.current) return Promise.resolve(userRef.current)
    accountRef.current ??= (async () => {
      const user = await registerParticipant(displayName)
      userRef.current = user
      setHasAccount(true)
      await reportOpened(user)
      return user
    })().catch(err => { accountRef.current = null; throw err })
    return accountRef.current
  }

  async function handleJoin() {
    if (!inviteCode.trim()) return
    setStatus('joining')
    setError(null)
    try {
      const user = await ensureAccount()
      const membership = await joinSession(user, inviteCode.trim(), displayName)
      sessionIdRef.current = membership.sessionId
      setStatus('ready')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to join session.')
      setStatus('idle')
    }
  }

  // Goes out on its own (the app's Go sign): a new account with Go switched on, then a casual run.
  // Start tracking then posts positions to the casual session exactly as it does to a joined session.
  async function handleGo() {
    setStatus('joining')
    setError(null)
    try {
      const user = await ensureAccount()
      await enableGo(user)
      const run = await startGoRun(user)
      sessionIdRef.current = run.sessionId
      goBearingRef.current = Math.random() * 360
      setMode('go')
      setStatus('ready')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to go out.')
      setStatus('idle')
    }
  }

  // Joins a group by its invite code, the app's way for a "groups" account: the account gets the feature,
  // joins the session, and carries on (or later starts) posting to its own Go run. Doesn't start tracking.
  async function handleJoinGroup() {
    const code = groupCode.trim()
    if (!code) return
    armBackgroundTimer() // used just now: the 10 s start again
    setGroupBusy(true)
    setError(null)
    try {
      const user = await ensureAccount()
      const membership = await joinGroup(user, code, displayName)
      setGroup({ sessionId: membership.sessionId, name: membership.sessionName })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to join the group.')
    } finally {
      setGroupBusy(false)
    }
  }

  async function handleLeaveGroup() {
    const user = userRef.current
    if (!user || !group) return
    armBackgroundTimer()
    setGroupBusy(true)
    try {
      await leaveSession(user, group.sessionId)
      setGroup(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to leave the group.')
    } finally {
      setGroupBusy(false)
    }
  }

  // Tells the server the app came to the front or went away, if that's a change. `user` is passed while
  // the account is still being set up (userRef isn't set yet).
  async function sendAppState(next: AppState, user: AuthedUser | null = userRef.current) {
    if (appStateRef.current === next) return
    appStateRef.current = next
    setAppState(next)
    // No account yet: nothing to record against. Its first report comes when the account is made.
    if (!user) return
    try {
      await reportAppState(user, next)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to report the app state.')
    }
  }

  // Goes back to the background AUTO_BACKGROUND_MS after the last thing done in the app.
  function armBackgroundTimer() {
    if (backgroundTimerRef.current) clearTimeout(backgroundTimerRef.current)
    backgroundTimerRef.current = null
    if (!autoBackgroundRef.current || appStateRef.current !== 'front') return
    if (statusRef.current !== 'ready' && statusRef.current !== 'running') return
    backgroundTimerRef.current = setTimeout(() => {
      backgroundTimerRef.current = null
      void sendAppState('back')
    }, AUTO_BACKGROUND_MS)
  }

  // Joining or going out is done in the app, so it's opened for that: the line starts with "Opened the
  // app". After that the switch (top right) brings it back.
  async function reportOpened(user: AuthedUser) {
    // Sent even though the app is already open on the tile: that was before there was an account.
    appStateRef.current = 'front'
    setAppState('front')
    try { await reportAppState(user, 'front') } catch { /* the Activity tab just won't show it */ }
    armBackgroundTimer()
  }

  async function handleAppState(next: AppState) {
    if (next === 'front' && !userRef.current) {
      // First time open: the account is made now, and records "Opened the app" itself.
      setAppState('front')
      appStateRef.current = 'front'
      try {
        await ensureAccount()
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to make an account for this phone.')
        appStateRef.current = 'back'
        setAppState('back')
      }
      return
    }
    await sendAppState(next)
    if (next === 'front') armBackgroundTimer()
    else if (backgroundTimerRef.current) { clearTimeout(backgroundTimerRef.current); backgroundTimerRef.current = null }
  }

  async function handleShareLink() {
    const user = userRef.current
    if (!user) return
    armBackgroundTimer() // used just now: the 10 s start again
    setWatchBusy(true)
    setError(null)
    try {
      setWatchLink(await createWatchLink(user))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to make a watch link.')
    } finally {
      setWatchBusy(false)
    }
  }

  // A pretend viewer with a browser id of its own, polling the link every 10 s like the watch page with
  // its tab on screen, until stopped: one viewing on the runner's Activity tab, as long as it watched.
  function handleAddViewer() {
    const link = watchLink
    if (!link) return
    setError(null)
    const browserId = `sim-viewer-${crypto.randomUUID()}`
    const look = () => watchOnce(link, browserId).catch(err => setError(err instanceof Error ? err.message : 'Failed to watch.'))
    void look()
    viewersRef.current.push(setInterval(look, VIEWER_POLL_MS))
    setViewerCount(viewersRef.current.length)
  }

  // The longest-watching viewer closes the tab.
  function handleStopViewer() {
    const timer = viewersRef.current.shift()
    if (timer) clearInterval(timer)
    setViewerCount(viewersRef.current.length)
  }

  function stopAllViewers() {
    viewersRef.current.forEach(clearInterval)
    viewersRef.current = []
    setViewerCount(0)
  }

  // Auto-joins once on mount for phones seeded by "Create session" — skips the manual
  // invite-code-then-Join step for the default set of phones. Guarded the same way as the
  // start-point request above, otherwise StrictMode's double-invoke would register two
  // accounts and join twice.
  useEffect(() => {
    if (hasAutoJoinedRef.current) return
    if (autoJoin && defaultInviteCode.trim()) {
      hasAutoJoinedRef.current = true
      // Seeded phones open the app to join.
      appStateRef.current = 'front'
      setAppState('front')
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
    } else if (movesNormally && goBearingRef.current !== null) {
      next = destinationPoint(cur.lat, cur.lon, goBearingRef.current, stepMeters)
      nextHeading = goBearingRef.current
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
      // Matches scheduleNextTick's own delay computation, so the self-reported heartbeat lines up
      // with when the next post will actually fire — this is what drives the Legend's countdown
      // ring (findAdaptiveCountdowns skips any runner with no nextExpectedAt at all).
      const nextExpectedAt = new Date(Date.now() + tickIntervalMs)
      await postLocation(
        user, sessionId, next.lat, next.lon, nextHeading, qualityRef.current === 'satellite', nextExpectedAt,
        BATTERY_LEVEL_FOR_MODE[batteryModeRef.current])
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

  // Go starts tracking straight away, as the app does after "Before you go" (CasualStore.start): as soon
  // as the phone is out and has a place to start from, whichever comes second.
  useEffect(() => {
    if (mode === 'go' && status === 'ready' && position) handleStart()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, status, position])

  async function handleLeave() {
    if (timeoutRef.current) { clearTimeout(timeoutRef.current); timeoutRef.current = null }
    const user = userRef.current
    const sessionId = sessionIdRef.current
    armBackgroundTimer() // used just now: the 10 s start again
    setStatus('left')
    setWatchLink(null)
    stopAllViewers() // the run is over: nothing left to watch
    if (user && sessionId) {
      try {
        if (mode === 'go') await stopGoRun(user)
        else await leaveSession(user, sessionId)
      } catch { /* best-effort */ }
    }
  }

  function handleRemove() {
    if (timeoutRef.current) clearTimeout(timeoutRef.current)
    stopAllViewers()
    if (status !== 'left' && status !== 'idle') {
      const user = userRef.current
      const sessionId = sessionIdRef.current
      if (user && sessionId) {
        if (mode === 'go') stopGoRun(user).catch(() => {})
        else leaveSession(user, sessionId).catch(() => {})
      }
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
  const canStart = status === 'ready' && position !== null
    && (mode === 'go' || convergencePoint !== null || (route !== null && route.length > 1))
  const canLeave = status === 'ready' || status === 'running'
  // In the background the app's own buttons can't be pressed; the simulator's controls still work.
  const away = appState === 'back'
  const canPickPosition = status !== 'left'
  // Groups are for phones on Go (or not out yet), not phones tracking in a joined session.
  const canUseGroups = mode === 'go' ? status !== 'left' : status === 'idle'

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
        {/* The name is the account's display name, so it's fixed once the account is made. */}
        {(status === 'idle' || status === 'joining') && !hasAccount
          ? <input style={nameInput} value={displayName} onChange={e => setDisplayName(e.target.value)} disabled={status === 'joining'} />
          : <span style={nameLabel}>{displayName}</span>
        }
        {status !== 'left' && (
          <button
            type="button"
            role="switch"
            aria-checked={!away}
            aria-label="App in the foreground"
            title={away ? 'In the background: tap to open the app' : 'In the foreground: tap to put the app away'}
            onClick={() => handleAppState(away ? 'front' : 'back')}
            style={appSwitch}
          >
            <span style={switchTrack(!away)}><span style={switchKnob(!away)} /></span>
            <span style={switchText(!away)}>{away ? 'Background' : 'Foreground'}</span>
          </button>
        )}
        <button style={removeBtn} onClick={handleRemove} title="Remove phone">×</button>
      </div>
      <div style={statusPillRow}>
        <span style={statusPill(status)}>{statusLabel(status, mode)}</span>
        {group && <span style={groupPill}>Group: {group.name}</span>}
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

      {status !== 'left' && crewOptions.length > 0 && (
        <div style={qualityRow} title="Crew is both ways: ticking here ticks this phone on the other one too">
          <span style={{ color: '#64748b' }}>Crew:</span>
          {crewOptions.map(option => (
            <label key={option.phoneId} style={qualityLabel(option.isCrew, option.busy)}>
              <input
                type="checkbox"
                checked={option.isCrew}
                disabled={option.busy}
                onChange={() => onToggleCrew?.(option.phoneId)}
                style={{ marginRight: '0.3rem' }}
              />
              {option.displayName}
              {option.pending && <span style={{ color: '#94a3b8', marginLeft: '0.25rem' }}>(once both apps are open)</span>}
            </label>
          ))}
        </div>
      )}

      {(status === 'idle' || status === 'joining') && (
        <div style={joinRow}>
          <input
            style={codeInput}
            value={inviteCode}
            onChange={e => setInviteCode(e.target.value.toUpperCase())}
            placeholder="INVITE CODE"
            disabled={status === 'joining'}
          />
          <button style={primaryBtn(!inviteCode.trim() || status === 'joining' || away)} disabled={!inviteCode.trim() || status === 'joining' || away} onClick={handleJoin}>
            {status === 'joining' ? 'Joining…' : 'Join'}
          </button>
          {defaultInviteCode && defaultInviteCode !== inviteCode && (
            <button style={linkBtn} onClick={() => setInviteCode(defaultInviteCode)}>use {defaultInviteCode}</button>
          )}
        </div>
      )}

      {status === 'idle' && (
        <div style={joinRow}>
          <button style={goBtn(away)} disabled={away} onClick={handleGo} title="Go out on your own, like the app's Go sign, instead of joining a session">
            Go (no session)
          </button>
        </div>
      )}
      {status === 'idle' && away && (
        <p style={hint}>The app is closed: switch it on (top right) to join or go.</p>
      )}

      {canUseGroups && (
        <div style={joinRow} title="Join a group by its invite code, as a groups account does in the app: no tracking in the session; members see each other by name on Go">
          {group ? (
            <button style={changePointBtn} disabled={groupBusy || away} onClick={handleLeaveGroup}>
              {groupBusy ? 'Leaving…' : `Leave group ${group.name}`}
            </button>
          ) : (
            <>
              <input
                style={codeInput}
                value={groupCode}
                onChange={e => setGroupCode(e.target.value.toUpperCase())}
                placeholder="GROUP INVITE CODE"
                disabled={groupBusy}
              />
              <button style={changePointBtn} disabled={!groupCode.trim() || groupBusy || away} onClick={handleJoinGroup}>
                {groupBusy ? 'Joining…' : 'Join group'}
              </button>
              {defaultInviteCode && defaultInviteCode !== groupCode && (
                <button style={linkBtn} onClick={() => setGroupCode(defaultInviteCode)}>use {defaultInviteCode}</button>
              )}
            </>
          )}
        </div>
      )}

      {error && <div style={errorText}>{error}</div>}

      {status !== 'idle' && status !== 'joining' && (
        <>
          <p style={hint}>
            {status === 'ready' && !position && 'Choose a starting point above.'}
            {status === 'ready' && position && !target && mode === 'session' && 'Waiting for the convergence point (or upload a route).'}
            {status === 'ready' && position && target && mode === 'session' && 'Ready — press Start tracking.'}
            {status === 'ready' && !position && mode === 'go' && ' Tracking starts once it has one, as Go does in the app.'}
            {status === 'running' && 'Sending updates…'}
            {status === 'left' && (mode === 'go' ? 'Stopped the Go run.' : 'Left the session.')}
          </p>

          {canLeave && (
            <div style={qualityRow}>
              <label style={qualityLabel(autoBackground, false)} title="Like a phone being put away when you're done with it">
                <input
                  type="checkbox"
                  checked={autoBackground}
                  onChange={e => setAutoBackground(e.target.checked)}
                  style={{ marginRight: '0.3rem' }}
                />
                App goes to the background {AUTO_BACKGROUND_MS / 1000} s after use
              </label>
            </div>
          )}
          {canLeave && away && (
            <p style={hint}>The app is in the background: switch it on (top right) to use it.</p>
          )}


          {mode === 'go' && canLeave && (
            <div style={watchBox}>
              {watchLink ? (
                <>
                  <a style={watchLinkText} href={`${CLIENT_URL}${watchLink.path}`} target="_blank" rel="noreferrer">Open watch link</a>
                  <button style={changePointBtn} onClick={handleAddViewer} title="A pretend browser opens the link and keeps it on screen until stopped">
                    Add a viewer
                  </button>
                  {viewerCount > 0 && (
                    <>
                      <span style={positionText}>{viewerCount} watching</span>
                      <button style={changePointBtn} onClick={handleStopViewer} title="The longest-watching viewer closes the tab">
                        Stop a viewer
                      </button>
                    </>
                  )}
                </>
              ) : (
                <button style={changePointBtn} disabled={watchBusy || away} onClick={handleShareLink}>Share a watch link</button>
              )}
            </div>
          )}

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

          {/* Separate row, not folded into quality above — battery is an independent field on the
              real POST (see BatteryMode's comment), takes effect on the next tick via
              batteryModeRef the same way a mid-run quality change does. */}
          <div style={qualityRow}>
            {(['normal', 'low', 'critical'] as BatteryMode[]).map(b => (
              <label key={b} style={qualityLabel(batteryMode === b, status === 'left')}>
                <input
                  type="checkbox"
                  checked={batteryMode === b}
                  disabled={status === 'left'}
                  onChange={() => setBatteryMode(b)}
                  style={{ marginRight: '0.3rem' }}
                />
                {batteryText(b)}
              </label>
            ))}
          </div>

          <div style={actionRow}>
            {/* No pause: a real phone tracks until it's stopped. "Missing" is the phone going quiet. */}
            <button style={primaryBtn(!canStart)} disabled={!canStart} onClick={handleStart}>
              {status === 'running' ? 'Tracking' : 'Start tracking'}
            </button>
            <button style={leaveBtn(!canLeave || away)} disabled={!canLeave || away} onClick={handleLeave}>{mode === 'go' ? 'Stop' : 'Leave'}</button>
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

function statusLabel(status: PhoneStatus, mode: 'session' | 'go'): string {
  switch (status) {
    case 'idle': return 'Not joined'
    case 'joining': return 'Joining…'
    case 'ready': return mode === 'go' ? 'Out on Go' : 'Joined'
    case 'running': return mode === 'go' ? 'Running (Go)' : 'Running'
    case 'left': return mode === 'go' ? 'Stopped' : 'Left'
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

function batteryText(b: BatteryMode): string {
  switch (b) {
    case 'normal': return 'Battery: normal'
    case 'low': return `Battery: low (${BATTERY_LEVEL_FOR_MODE.low}%)`
    case 'critical': return `Battery: critical (${BATTERY_LEVEL_FOR_MODE.critical}%)`
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

const statusPillRow: React.CSSProperties = { marginBottom: '0.5rem', display: 'flex', gap: '0.4rem', flexWrap: 'wrap' }
const groupPill: React.CSSProperties = { fontSize: '0.72rem', fontWeight: 600, padding: '0.15rem 0.5rem', borderRadius: 999, background: '#ede9fe', color: '#5b21b6' }
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
const leaveBtn = (disabled: boolean): React.CSSProperties => ({
  flex: 1, padding: '0.35rem 0.6rem', borderRadius: 6, border: '1px solid #fca5a5', fontWeight: 600, fontSize: '0.8rem',
  background: '#fff', color: disabled ? '#e2e8f0' : '#dc2626', cursor: disabled ? 'default' : 'pointer', borderColor: disabled ? '#e2e8f0' : '#fca5a5',
})
const appSwitch: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', gap: '0.3rem', border: 'none', background: 'none',
  padding: 0, cursor: 'pointer', flexShrink: 0,
}
const switchTrack = (on: boolean): React.CSSProperties => ({
  position: 'relative', width: 30, height: 17, borderRadius: 999, flexShrink: 0,
  background: on ? '#16a34a' : '#cbd5e1', transition: 'background 0.15s',
})
const switchKnob = (on: boolean): React.CSSProperties => ({
  position: 'absolute', top: 2, left: on ? 15 : 2, width: 13, height: 13, borderRadius: '50%',
  background: '#fff', boxShadow: '0 1px 2px rgba(0,0,0,0.3)', transition: 'left 0.15s',
})
const switchText = (on: boolean): React.CSSProperties => ({
  fontSize: '0.68rem', fontWeight: 600, color: on ? '#16a34a' : '#64748b', width: '4.6rem', textAlign: 'left',
})
const goBtn = (disabled: boolean): React.CSSProperties => ({
  flex: 1, padding: '0.35rem 0.6rem', borderRadius: 6, fontWeight: 700, fontSize: '0.8rem',
  border: `1px solid ${disabled ? '#e2e8f0' : '#fdba74'}`, background: disabled ? '#f8fafc' : '#fff7ed',
  color: disabled ? '#cbd5e1' : '#c2410c', cursor: disabled ? 'default' : 'pointer',
})
const watchBox: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.5rem', flexWrap: 'wrap', marginBottom: '0.5rem' }
const watchLinkText: React.CSSProperties = { fontSize: '0.78rem', color: '#2563eb', fontWeight: 600 }
const linkBtn: React.CSSProperties ={ border: 'none', background: 'none', color: '#2563eb', fontSize: '0.78rem', cursor: 'pointer', textDecoration: 'underline' }
const statusLine: React.CSSProperties = { fontSize: '0.74rem', color: '#64748b', fontFamily: 'monospace' }
