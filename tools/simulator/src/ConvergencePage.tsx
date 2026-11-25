import { useEffect, useRef, useState } from 'react'
import { convergenceIcon, phoneMarkerIcon, runnerColour } from './lib/icons'
import {
  createSession, deleteSimAccounts, makeCrew, randomSessionName, registerParticipant, removeCrew, setEntryCode, setSessionNearby,
  NEARBY_RADII, SERVER_URL, CLIENT_URL, type AuthedUser, type NearbyStatus, type SessionMembership,
} from './lib/dotwatcherApi'
import PhoneSimulator from './PhoneSimulator'
import MapPickerModal from './MapPickerModal'
import SimulatorMap from './SimulatorMap'
import type { LatLon, PhoneSnapshot } from './lib/types'

const WELLINGTON: LatLon = { lat: -41.2865, lon: 174.7762 }

// Seeded automatically (and auto-joined) every time a session is created, so you don't have to
// manually add and join a handful of phones every test run. Add more with "+ Add phone".
const DEFAULT_PHONE_NAMES = ['SK', 'DH', 'CH']

// Must match client/src/useSimulatorRouteOverlay.ts's MESSAGE_TYPE exactly — that's the only
// listener, and this is the only sender. Lets each phone's GPX route get drawn, in that phone's
// own colour, on the real client's own map inside the "Live client view" iframe below — the
// simulator has no same-origin DOM access into it (separate Vite dev server/origin), so
// postMessage is the only way in.
const ROUTE_OVERLAY_MESSAGE_TYPE = 'dotwatcher-simulator-routes'

type PhoneConfig = { id: number; initialName?: string; autoJoin?: boolean }

type ActivePicker =
  | { kind: 'convergence'; intent: 'create' | 'change' }
  | { kind: 'phone'; phoneId: number; displayName: string }

const SPEED_OPTIONS = [
  { label: 'Walk (1.4 m/s)', value: 1.4 },
  { label: 'Jog (2.8 m/s)', value: 2.8 },
  { label: 'Run (4.0 m/s)', value: 4.0 },
]

const TICK_OPTIONS = [
  { label: '2s', value: 2000 },
  { label: '4s', value: 4000 },
  { label: '8s', value: 8000 },
  { label: '15s', value: 15000 },
]

export default function ConvergencePage() {
  const [sessionName, setSessionName] = useState('')
  const [session, setSession] = useState<SessionMembership | null>(null)
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [cleanupNote, setCleanupNote] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const [convergencePoint, setConvergencePoint] = useState<LatLon | null>(null)
  const [speedMps, setSpeedMps] = useState(2.8)
  const [tickMs, setTickMs] = useState(4000)
  const [showLiveClient, setShowLiveClient] = useState(true)

  const [phones, setPhones] = useState<PhoneConfig[]>([])
  const [positions, setPositions] = useState<Record<number, LatLon | null>>({})
  const [routes, setRoutes] = useState<Record<number, LatLon[]>>({})
  const nextIdRef = useRef(1)
  const [snapshots, setSnapshots] = useState<Record<number, PhoneSnapshot>>({})
  const liveClientIframeRef = useRef<HTMLIFrameElement>(null)

  // Only one map-picker modal is ever shown at a time — extra requests (e.g. three phones
  // seeded at once, each asking for a start point on mount) queue up behind the current one.
  const [activePicker, setActivePicker] = useState<ActivePicker | null>(null)
  const pickerQueueRef = useRef<ActivePicker[]>([])

  function enqueuePicker(req: ActivePicker) {
    setActivePicker(current => {
      if (current) {
        pickerQueueRef.current.push(req)
        return current
      }
      return req
    })
  }

  function advancePicker() {
    setActivePicker(pickerQueueRef.current.shift() ?? null)
  }

  async function runCreateSession() {
    setCreating(true)
    setCreateError(null)
    setCleanupNote(null)
    try {
      // Best-effort: previous test runs' throwaway accounts (and any sessions they own)
      // shouldn't have to be cleaned up by hand before starting a fresh one.
      try {
        const removed = await deleteSimAccounts()
        setCleanupNote(removed > 0 ? `Cleaned up ${removed} old sim- account${removed === 1 ? '' : 's'}.` : null)
      } catch (err) {
        setCleanupNote(err instanceof Error ? err.message : 'Could not clean up old sim- accounts.')
      }

      const organizer = await registerParticipant('Simulator')
      organizerUserIdRef.current = organizer.userId
      organizerRef.current = organizer
      setNearby(null)
      setMeetingPoint(null)
      setNearbyError(null)
      setEntryCodeSet(null)
      const name = sessionName.trim() || randomSessionName()
      const membership = await createSession(organizer, name)
      setSession(membership)

      // A new session is a full reset — old phone tiles' accounts just got swept up above.
      setSnapshots({})
      setPositions({})
      setRoutes({})
      setCrewPairs(new Set())
      setAppliedCrew(new Set())
      setPhones(DEFAULT_PHONE_NAMES.map(initialName => ({ id: nextIdRef.current++, initialName, autoJoin: true })))
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Failed to create session.')
    } finally {
      setCreating(false)
    }
  }

  // Which phones should be crew, as "lowerId|higherId" of phone tile ids: crew is both ways, so one entry
  // per pair, ticked on both tiles. A tick can be set before the phones have accounts; it's made real on
  // the server (`appliedCrew`) as soon as both do. `busyCrew` is a pair whose change is on its way.
  const [crewPairs, setCrewPairs] = useState<Set<string>>(new Set())
  const [appliedCrew, setAppliedCrew] = useState<Set<string>>(new Set())
  const [busyCrew, setBusyCrew] = useState<Set<string>>(new Set())
  const crewInFlight = useRef(new Set<string>())
  const pairKey = (a: number, b: number) => (a < b ? `${a}|${b}` : `${b}|${a}`)
  const pairIds = (key: string) => key.split('|').map(Number) as [number, number]
  const withKey = (set: Set<string>, key: string, on: boolean) => {
    const next = new Set(set)
    if (on) next.add(key)
    else next.delete(key)
    return next
  }

  // Makes the server match a pair's tick, when both phones have accounts.
  async function applyCrew(key: string, wantCrew: boolean) {
    const [aId, bId] = pairIds(key)
    const a = snapshots[aId]?.user
    const b = snapshots[bId]?.user
    // A ref, not busyCrew: snapshots change every tick, and state wouldn't be updated yet.
    if (!a || !b || crewInFlight.current.has(key)) return
    crewInFlight.current.add(key)
    setBusyCrew(prev => withKey(prev, key, true))
    setCreateError(null)
    try {
      if (wantCrew) await makeCrew(a, b)
      else await removeCrew(a, b)
      setAppliedCrew(prev => withKey(prev, key, wantCrew))
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Changing crew failed.')
      setCrewPairs(prev => withKey(prev, key, !wantCrew)) // the tick goes back to how it is on the server
    } finally {
      crewInFlight.current.delete(key)
      setBusyCrew(prev => withKey(prev, key, false))
    }
  }

  function toggleCrew(phoneId: number, otherId: number) {
    const key = pairKey(phoneId, otherId)
    const wantCrew = !crewPairs.has(key)
    setCrewPairs(prev => withKey(prev, key, wantCrew))
    if (wantCrew || appliedCrew.has(key)) void applyCrew(key, wantCrew)
  }

  // A tick set early is made real once both phones have accounts.
  useEffect(() => {
    for (const key of crewPairs)
      if (!appliedCrew.has(key)) void applyCrew(key, true)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [snapshots, crewPairs])

  // Every other phone still on screen and not done; `pending` while a tick waits for accounts.
  function crewOptionsFor(phoneId: number) {
    if (snapshots[phoneId]?.status === 'left') return []
    return phones
      .filter(p => p.id !== phoneId && snapshots[p.id]?.status !== 'left')
      .map(p => {
        const key = pairKey(phoneId, p.id)
        return {
          phoneId: p.id,
          displayName: snapshots[p.id]?.displayName ?? p.initialName ?? `Phone ${p.id}`,
          isCrew: crewPairs.has(key),
          pending: crewPairs.has(key) && !appliedCrew.has(key),
          busy: busyCrew.has(key),
        }
      })
  }

  // Deletes sim- accounts on the server, and with them everything they made: sessions they own,
  // positions, Go runs, watch links and their activity events (the admin Activity tab). While phones
  // here have accounts, only the others go: those phones and the session's organizer are kept, so the
  // test in progress carries on.
  const [deletingSimData, setDeletingSimData] = useState(false)
  const organizerUserIdRef = useRef<string | null>(null)
  // The session's organiser, who alone can switch nearby join on and set an entry code.
  const organizerRef = useRef<AuthedUser | null>(null)

  // Nearby join for the session (the app's "Let people nearby join" and Ping): the meeting point is put at
  // the convergence point when switched on, and only moves when "Ping here" is pressed again.
  const [nearby, setNearby] = useState<NearbyStatus | null>(null)
  // Where the last Ping put the meeting point, so the map can draw the capture zone there.
  const [meetingPoint, setMeetingPoint] = useState<LatLon | null>(null)
  const [nearbyRadius, setNearbyRadius] = useState<number>(100)
  const [nearbyBusy, setNearbyBusy] = useState(false)
  const [nearbyError, setNearbyError] = useState<string | null>(null)
  const [entryCodeInput, setEntryCodeInput] = useState('')
  const [entryCodeSet, setEntryCodeSet] = useState<string | null>(null)

  async function handleNearby(enabled: boolean) {
    const organizer = organizerRef.current
    if (!session || !organizer) return
    if (enabled && !convergencePoint) return
    setNearbyBusy(true)
    setNearbyError(null)
    try {
      const point = enabled ? convergencePoint : null
      setNearby(await setSessionNearby(organizer, session.sessionId, enabled, point, nearbyRadius))
      setMeetingPoint(point)
    } catch (err) {
      setNearbyError(err instanceof Error ? err.message : 'Changing nearby join failed.')
    } finally {
      setNearbyBusy(false)
    }
  }

  async function handleEntryCode(code: string | null) {
    const organizer = organizerRef.current
    if (!session || !organizer) return
    setNearbyBusy(true)
    setNearbyError(null)
    try {
      await setEntryCode(organizer, session.sessionId, code)
      setEntryCodeSet(code ? code.toUpperCase() : null)
      if (!code) setEntryCodeInput('')
    } catch (err) {
      setNearbyError(err instanceof Error ? err.message : 'Setting the entry code failed.')
    } finally {
      setNearbyBusy(false)
    }
  }
  const inUseUserIds = new Set(
    [
      ...phones.map(p => snapshots[p.id]?.userId ?? null),
      session ? organizerUserIdRef.current : null,
    ].filter((id): id is string => !!id))
  const keepsSomething = inUseUserIds.size > 0

  async function handleDeleteSimData() {
    const question = keepsSomething
      ? `Delete every sim- account on ${SERVER_URL} except the ${inUseUserIds.size} in use here, with their sessions, Go runs and activity?`
      : `Delete every sim- account on ${SERVER_URL}, with their sessions, Go runs and activity?`
    if (!window.confirm(question)) return
    setDeletingSimData(true)
    setCreateError(null)
    try {
      const removed = await deleteSimAccounts(inUseUserIds)
      // Nothing on screen is touched: every account in use here was kept.
      setCleanupNote(`Deleted ${removed} ${keepsSomething ? 'other ' : ''}sim- account${removed === 1 ? '' : 's'} and their data.`)
    } catch (err) {
      setCleanupNote(err instanceof Error ? err.message : 'Could not delete sim- accounts.')
    } finally {
      setDeletingSimData(false)
    }
  }

  function handleCreateSessionClick() {
    enqueuePicker({ kind: 'convergence', intent: 'create' })
  }

  function handleChangeConvergencePoint() {
    enqueuePicker({ kind: 'convergence', intent: 'change' })
  }

  function handleRequestStartPoint(id: number, displayName: string) {
    enqueuePicker({ kind: 'phone', phoneId: id, displayName })
  }

  async function handlePickerConfirm(point: LatLon) {
    if (!activePicker) return
    if (activePicker.kind === 'convergence') {
      const { intent } = activePicker
      setConvergencePoint(point)
      advancePicker()
      if (intent === 'create') await runCreateSession()
    } else {
      setPositions(prev => ({ ...prev, [activePicker.phoneId]: point }))
      advancePicker()
    }
  }

  function handlePickerCancel() {
    advancePicker()
  }

  function handleCopyInvite() {
    if (!session) return
    navigator.clipboard?.writeText(session.inviteCode).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }).catch(() => {})
  }

  function addPhone() {
    const id = nextIdRef.current++
    setPhones(prev => [...prev, { id }])
  }

  function removePhone(id: number) {
    setPhones(prev => prev.filter(p => p.id !== id))
    setSnapshots(prev => {
      const next = { ...prev }
      delete next[id]
      return next
    })
    setPositions(prev => {
      const next = { ...prev }
      delete next[id]
      return next
    })
    setRoutes(prev => {
      const next = { ...prev }
      delete next[id]
      return next
    })
  }

  function handleRouteChange(id: number, route: LatLon[] | null) {
    setRoutes(prev => {
      if (!route || route.length === 0) {
        if (!(id in prev)) return prev
        const next = { ...prev }
        delete next[id]
        return next
      }
      return { ...prev, [id]: route }
    })
    // A route's first point doubles as the phone's start point — matches how "Choose start
    // point" itself sets position, and means the phone's marker shows up on the map right away.
    if (route && route.length > 0) {
      setPositions(prev => ({ ...prev, [id]: route[0] }))
    }
  }

  function handleSnapshot(snapshot: PhoneSnapshot) {
    setSnapshots(prev => ({ ...prev, [snapshot.id]: snapshot }))
  }

  function handlePositionChange(id: number, point: LatLon) {
    setPositions(prev => ({ ...prev, [id]: point }))
  }

  const activePhones = phones.map(p => snapshots[p.id]).filter((s): s is PhoneSnapshot => !!s)
  const liveClientUrl = session ? `${CLIENT_URL}/code/${session.inviteCode}` : null
  const showingLiveClient = !!(session && showLiveClient && liveClientUrl)

  // [lon, lat] to match Mapbox/GeoJSON coordinate order, which is what the client-side listener
  // (client/src/useSimulatorRouteOverlay.ts) expects to hand straight to a GeoJSON LineString.
  const overlayRoutes = activePhones
    .map(phone => ({
      id: phone.id,
      color: runnerColour(phone.displayName),
      points: (routes[phone.id] ?? []).map(p => [p.lon, p.lat] as [number, number]),
    }))
    .filter(r => r.points.length > 1)

  function postRoutesToLiveClient() {
    liveClientIframeRef.current?.contentWindow?.postMessage(
      { type: ROUTE_OVERLAY_MESSAGE_TYPE, routes: overlayRoutes },
      CLIENT_URL,
    )
  }

  // Re-sent on every route/phone change, not just once — the iframe can (re)load after this
  // effect first runs (e.g. a slow dev-server compile), which would otherwise drop the message
  // before the client's listener was ever attached to receive it.
  useEffect(() => {
    postRoutesToLiveClient()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [routes, activePhones.map(p => p.id + p.displayName).join(','), liveClientUrl])

  return (
    <div>
      <section style={panel}>
        <h2 style={panelTitle}>Session</h2>
        <div style={sessionRow}>
          <input
            style={sessionNameInput}
            placeholder="Session name (optional)"
            value={sessionName}
            onChange={e => setSessionName(e.target.value)}
            disabled={creating}
          />
          <button style={createBtn(creating)} disabled={creating} onClick={handleCreateSessionClick}>
            {creating ? 'Creating…' : session ? 'New session' : 'Create session'}
          </button>
        </div>
        {createError && <div style={errorText}>{createError}</div>}
        {cleanupNote && <div style={mutedText}>{cleanupNote}</div>}
        {session && (
          <div style={sessionInfo}>
            <span>Invite code: <strong style={inviteCodeText}>{session.inviteCode}</strong></span>
            <button style={copyBtn} onClick={handleCopyInvite}>{copied ? 'Copied!' : 'Copy'}</button>
            <span style={mutedText}>· {session.sessionName} · server {SERVER_URL}</span>
          </div>
        )}
        <p style={hint}>
          Creating a session first asks for a convergence point, then deletes every old sim-
          account (best-effort — needs <code>VITE_BEARER_TOKEN</code> configured) and seeds
          phones {DEFAULT_PHONE_NAMES.map(n => `"${n}"`).join(', ')}, auto-joined to it and
          prompting for a start point each. Any phone can still be re-pointed at a different
          invite code — including a real session created from the iOS app or web client.
        </p>
      </section>

      {session && (
        <section style={panel}>
          <h2 style={panelTitle}>Convergence point</h2>
          <div style={sessionInfo}>
            {convergencePoint
              ? <span style={statusLine}>{convergencePoint.lat.toFixed(5)}, {convergencePoint.lon.toFixed(5)}</span>
              : <span style={mutedText}>Not set</span>}
            <button style={copyBtn} onClick={handleChangeConvergencePoint}>Change</button>
          </div>
          <div style={sessionInfo}>
            <strong>Nearby join</strong>
            {nearby?.nearby
              ? <span style={statusLine}>On · {nearby.radiusMeters} m</span>
              : <span style={mutedText}>Off</span>}
            <select value={nearbyRadius} onChange={e => setNearbyRadius(Number(e.target.value))} disabled={nearbyBusy}>
              {NEARBY_RADII.map(r => <option key={r} value={r}>{r} m</option>)}
            </select>
            <button
              style={copyBtn}
              disabled={nearbyBusy || !convergencePoint}
              onClick={() => handleNearby(true)}
              title="The organiser's Ping: puts the meeting point at the convergence point, with this radius"
            >
              {nearby?.nearby ? 'Ping here' : 'Let people nearby join here'}
            </button>
            {nearby?.nearby && (
              <button style={copyBtn} disabled={nearbyBusy} onClick={() => handleNearby(false)}>Switch off</button>
            )}
          </div>
          <div style={sessionInfo}>
            <strong>Entry code</strong>
            {entryCodeSet
              ? <span style={statusLine}>{entryCodeSet}</span>
              : <span style={mutedText}>None</span>}
            <input
              style={sessionNameInput}
              placeholder="4-8 letters or numbers"
              value={entryCodeInput}
              onChange={e => setEntryCodeInput(e.target.value.toUpperCase())}
              disabled={nearbyBusy}
            />
            <button style={copyBtn} disabled={nearbyBusy || !entryCodeInput.trim()} onClick={() => handleEntryCode(entryCodeInput.trim())}>
              Set
            </button>
            {entryCodeSet && (
              <button style={copyBtn} disabled={nearbyBusy} onClick={() => handleEntryCode(null)}>Remove</button>
            )}
          </div>
          {nearbyError && <div style={errorText}>{nearbyError}</div>}
          <SimulatorMap
            convergencePoint={convergencePoint}
            phones={activePhones}
            routes={routes}
            captureZone={nearby?.nearby && meetingPoint ? { center: meetingPoint, radiusMeters: nearby.radiusMeters } : null}
          />
          <p style={hint}>
            Each phone's uploaded GPX route is drawn here in that phone's own colour — upload one
            from its card below to have it walk the route instead of straight-line towards the
            convergence point above. Nearby join puts the session's meeting point at the convergence
            point (moving the convergence point later doesn't move it; press Ping here), drawn as the
            yellow circle. Phones within the radius find it with <strong>Groups near you</strong>.
          </p>
        </section>
      )}

      <section style={panel}>
        <h2 style={panelTitle}>Movement</h2>
        <div style={movementRow}>
          <label style={movementLabel}>
            Speed
            <select style={select} value={speedMps} onChange={e => setSpeedMps(Number(e.target.value))}>
              {SPEED_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
          <label style={movementLabel}>
            Update every
            <select style={select} value={tickMs} onChange={e => setTickMs(Number(e.target.value))}>
              {TICK_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
        </div>
      </section>

      {(() => {
        const liveClientHeader = (
          <div style={phonesHeader}>
            <h2 style={panelTitle}>Live client view</h2>
            <label style={liveClientToggle}>
              <input type="checkbox" checked={showLiveClient} onChange={e => setShowLiveClient(e.target.checked)} disabled={!session} />
              Show
            </label>
          </div>
        )
        const liveClientBody = showingLiveClient && (
          <>
            <iframe
              key={liveClientUrl}
              ref={liveClientIframeRef}
              src={liveClientUrl!}
              style={clientFrame}
              title="Dot Watcher client"
              onLoad={postRoutesToLiveClient}
            />
            <p style={hint}>
              Loads <code>{liveClientUrl}</code> unauthenticated (the invite-code URL form skips
              sign-in). Requires the client's own dev server running separately (<code>cd client &amp;&amp; npm run dev</code>) —
              blank/failed to load usually means it isn't. <a href={liveClientUrl!} target="_blank" rel="noreferrer">Open in a new tab</a> instead.
              Each phone's uploaded GPX route is drawn here too, in that phone's own colour, via a
              postMessage the real client listens for — "Open in a new tab" won't get it, only this
              embedded iframe does.
            </p>
          </>
        )
        const phonesHeaderEl = (
          <div style={phonesHeader}>
            <h2 style={panelTitle}>Phones ({activePhones.length})</h2>
            <div style={{ display: 'flex', gap: '0.5rem' }}>
              <button style={deleteSimBtn} onClick={handleDeleteSimData} disabled={deletingSimData}
                title={keepsSomething
                  ? `Deletes every sim- account on ${SERVER_URL} except the phones (and session) here, and everything they made`
                  : `Deletes every sim- account on ${SERVER_URL}, and everything they made`}>
                {deletingSimData ? 'Deleting…' : keepsSomething ? 'Delete other sim-phone data' : 'Delete sim data'}
              </button>
              <button style={addBtn} onClick={addPhone}>+ Add phone</button>
            </div>
          </div>
        )
        const phoneCards = phones.map((p, i) => (
          <PhoneSimulator
            key={p.id}
            id={p.id}
            index={i}
            initialDisplayName={p.initialName}
            autoJoin={p.autoJoin}
            defaultInviteCode={session?.inviteCode ?? ''}
            position={positions[p.id] ?? null}
            convergencePoint={convergencePoint}
            route={routes[p.id] ?? null}
            speedMps={speedMps}
            tickMs={tickMs}
            onSnapshot={handleSnapshot}
            onPositionChange={handlePositionChange}
            onRequestStartPoint={handleRequestStartPoint}
            onRouteChange={handleRouteChange}
            onRemove={removePhone}
            crewOptions={crewOptionsFor(p.id)}
            onToggleCrew={otherId => void toggleCrew(p.id, otherId)}
          />
        ))

        // Once there's a live client to look at, the phones form a single vertical column
        // to its right instead of a wrapping grid below it — the iframe stretches (flex
        // align-items: stretch) to match whatever height that column naturally needs, so it's
        // 3 phones deep by default and grows taller as more are added, no hardcoded pixel height.
        if (showingLiveClient) {
          return (
            <section style={panel}>
              <div style={sideBySideRow}>
                <div style={liveClientColumn}>
                  {liveClientHeader}
                  {liveClientBody}
                </div>
                <div style={phonesColumn}>
                  {phonesHeaderEl}
                  {phones.length === 0 && <p style={hint}>Add a phone, then join it to a session using an invite code.</p>}
                  <div style={phonesStack}>{phoneCards}</div>
                </div>
              </div>
            </section>
          )
        }

        return (
          <>
            <section style={panel}>
              {liveClientHeader}
              {!session && <p style={hint}>Create a session above to embed the real web client here, viewing that session.</p>}
              {liveClientBody}
            </section>
            <section style={panel}>
              {phonesHeaderEl}
              {phones.length === 0 && <p style={hint}>Add a phone, then join it to a session using an invite code.</p>}
              <div style={phonesGrid}>{phoneCards}</div>
            </section>
          </>
        )
      })()}

      {activePicker?.kind === 'convergence' && (
        <MapPickerModal
          title={activePicker.intent === 'create' ? 'Choose the convergence point' : 'Change the convergence point'}
          hint='Click the map to choose where "Good" GPS phones will walk/run towards.'
          center={convergencePoint ?? WELLINGTON}
          initialValue={convergencePoint}
          pickIcon={convergenceIcon()}
          onConfirm={handlePickerConfirm}
          onCancel={handlePickerCancel}
        />
      )}
      {activePicker?.kind === 'phone' && (
        <MapPickerModal
          title={`${activePicker.displayName} — choose a starting point`}
          center={convergencePoint ?? WELLINGTON}
          initialValue={positions[activePicker.phoneId] ?? null}
          pickIcon={phoneMarkerIcon({
            displayName: activePicker.displayName,
            quality: 'good',
            status: 'ready',
            heading: null,
            position: null,
            convergencePoint: null,
          })}
          referencePoint={convergencePoint}
          referenceIcon={convergenceIcon()}
          onConfirm={handlePickerConfirm}
          onCancel={handlePickerCancel}
        />
      )}
    </div>
  )
}

const panel: React.CSSProperties = { marginBottom: '1.5rem', paddingBottom: '1.25rem', borderBottom: '1px solid #e2e8f0' }
const panelTitle: React.CSSProperties = { fontSize: '1rem', fontWeight: 700, marginBottom: '0.6rem' }
const sessionRow: React.CSSProperties = { display: 'flex', gap: '0.5rem', marginBottom: '0.5rem' }
const sessionNameInput: React.CSSProperties = { flex: 1, padding: '0.45rem 0.7rem', border: '1px solid #cbd5e1', borderRadius: 6 }
const createBtn = (disabled: boolean): React.CSSProperties => ({
  padding: '0.45rem 1.1rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.9rem',
  background: disabled ? '#e2e8f0' : '#1e293b', color: disabled ? '#94a3b8' : '#fff', cursor: disabled ? 'default' : 'pointer',
})
const errorText: React.CSSProperties = { color: '#dc2626', fontSize: '0.85rem', marginBottom: '0.5rem' }
const sessionInfo: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.6rem', fontSize: '0.9rem', marginBottom: '0.4rem', flexWrap: 'wrap' }
const inviteCodeText: React.CSSProperties = { fontFamily: 'monospace', fontSize: '1.05rem', letterSpacing: '0.05em' }
const copyBtn: React.CSSProperties = { padding: '0.2rem 0.6rem', fontSize: '0.78rem', borderRadius: 6, border: '1px solid #cbd5e1', background: '#fff', cursor: 'pointer' }
const mutedText: React.CSSProperties = { color: '#94a3b8', fontSize: '0.8rem' }
const hint: React.CSSProperties = { fontSize: '0.82rem', color: '#64748b', marginBottom: '0.6rem' }
const statusLine: React.CSSProperties = { fontSize: '0.78rem', color: '#64748b', fontFamily: 'monospace' }
const movementRow: React.CSSProperties = { display: 'flex', gap: '1.5rem' }
const movementLabel: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: '0.3rem', fontSize: '0.82rem', color: '#475569', fontWeight: 600 }
const select: React.CSSProperties = { padding: '0.35rem 0.5rem', borderRadius: 6, border: '1px solid #cbd5e1', fontSize: '0.85rem' }
const phonesHeader: React.CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '0.6rem' }
const deleteSimBtn: React.CSSProperties = { padding: '0.4rem 0.9rem', borderRadius: 6, border: '1px solid #fca5a5', background: '#fff', color: '#dc2626', fontWeight: 600, fontSize: '0.85rem', cursor: 'pointer' }
const addBtn: React.CSSProperties ={ padding: '0.4rem 0.9rem', borderRadius: 6, border: 'none', background: '#2563eb', color: '#fff', fontWeight: 700, fontSize: '0.85rem', cursor: 'pointer' }
const phonesGrid: React.CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: '0.75rem' }
const liveClientToggle: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.85rem', color: '#475569', cursor: 'pointer' }
const clientFrame: React.CSSProperties = { width: '100%', flex: 1, minHeight: 0, border: '1px solid #e2e8f0', borderRadius: 8 }

// Once a session exists, the client iframe and the phone list sit side by side instead of
// stacked — the phones form a single vertical column (not the wrapping grid used when there's
// no iframe alongside them) and the iframe stretches (via flex align-items: stretch) to match
// whatever height that column naturally needs. 3 seeded phones by default, growing taller with
// each one added, rather than a hardcoded pixel height.
const PHONE_COLUMN_WIDTH = 260
const sideBySideRow: React.CSSProperties = { display: 'flex', alignItems: 'stretch', gap: '1rem' }
const liveClientColumn: React.CSSProperties = { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }
const phonesColumn: React.CSSProperties = { flexShrink: 0, width: PHONE_COLUMN_WIDTH, display: 'flex', flexDirection: 'column' }
const phonesStack: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: '0.75rem' }
