import { useRef, useState } from 'react'
import { MapContainer, TileLayer, Marker, useMapEvents } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { convergenceIcon, phoneMarkerIcon } from './lib/icons'
import { createSession, randomSessionName, registerParticipant, SERVER_URL, CLIENT_URL, type SessionMembership } from './lib/dotwatcherApi'
import PhoneSimulator from './PhoneSimulator'
import type { LatLon, PhoneSnapshot } from './lib/types'

const WELLINGTON: [number, number] = [-41.2865, 174.7762]

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

function ConvergenceClickMarker({ onPick }: { onPick: (lat: number, lon: number) => void }) {
  const onPickRef = useRef(onPick)
  onPickRef.current = onPick
  useMapEvents({
    click(e) { onPickRef.current(e.latlng.lat, e.latlng.lng) },
  })
  return null
}

export default function ConvergencePage() {
  const [sessionName, setSessionName] = useState('')
  const [session, setSession] = useState<SessionMembership | null>(null)
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const [convergencePoint, setConvergencePoint] = useState<LatLon | null>(null)
  const [speedMps, setSpeedMps] = useState(2.8)
  const [tickMs, setTickMs] = useState(4000)
  const [showLiveClient, setShowLiveClient] = useState(true)

  const [phoneIds, setPhoneIds] = useState<number[]>([])
  const nextIdRef = useRef(1)
  const [snapshots, setSnapshots] = useState<Record<number, PhoneSnapshot>>({})

  async function handleCreateSession() {
    setCreating(true)
    setCreateError(null)
    try {
      const organizer = await registerParticipant('Simulator')
      const name = sessionName.trim() || randomSessionName()
      const membership = await createSession(organizer, name)
      setSession(membership)
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Failed to create session.')
    } finally {
      setCreating(false)
    }
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
    setPhoneIds(ids => [...ids, id])
  }

  function removePhone(id: number) {
    setPhoneIds(ids => ids.filter(i => i !== id))
    setSnapshots(prev => {
      const next = { ...prev }
      delete next[id]
      return next
    })
  }

  function handleSnapshot(snapshot: PhoneSnapshot) {
    setSnapshots(prev => ({ ...prev, [snapshot.id]: snapshot }))
  }

  const activePhones = phoneIds.map(id => snapshots[id]).filter((s): s is PhoneSnapshot => !!s)
  const liveClientUrl = session ? `${CLIENT_URL}/code/${session.inviteCode}` : null

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
          <button style={createBtn(creating)} disabled={creating} onClick={handleCreateSession}>
            {creating ? 'Creating…' : session ? 'New session' : 'Create session'}
          </button>
        </div>
        {createError && <div style={errorText}>{createError}</div>}
        {session && (
          <div style={sessionInfo}>
            <span>Invite code: <strong style={inviteCodeText}>{session.inviteCode}</strong></span>
            <button style={copyBtn} onClick={handleCopyInvite}>{copied ? 'Copied!' : 'Copy'}</button>
            <span style={mutedText}>· {session.sessionName} · server {SERVER_URL}</span>
          </div>
        )}
        <p style={hint}>
          Phones default to this invite code when created below, but each phone can be pointed at any invite code — including a real session created from the iOS app or web client.
        </p>
      </section>

      <section style={panel}>
        <h2 style={panelTitle}>Convergence point</h2>
        <p style={hint}>Click the map to choose the point all phones with "Good" GPS will walk/run towards.</p>
        <MapContainer center={WELLINGTON} zoom={13} style={bigMapStyle}>
          <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" attribution='&copy; OpenStreetMap contributors' />
          {convergencePoint && <Marker position={[convergencePoint.lat, convergencePoint.lon]} icon={convergenceIcon()} />}
          {activePhones.filter(p => p.position).map(p => (
            <Marker
              key={p.id}
              position={[p.position!.lat, p.position!.lon]}
              icon={phoneMarkerIcon({ ...p, convergencePoint })}
            />
          ))}
          <ConvergenceClickMarker onPick={(lat, lon) => setConvergencePoint({ lat, lon })} />
        </MapContainer>
        {convergencePoint && (
          <div style={statusLine}>{convergencePoint.lat.toFixed(5)}, {convergencePoint.lon.toFixed(5)}</div>
        )}
      </section>

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

      <section style={panel}>
        <div style={phonesHeader}>
          <h2 style={panelTitle}>Live client view</h2>
          <label style={liveClientToggle}>
            <input type="checkbox" checked={showLiveClient} onChange={e => setShowLiveClient(e.target.checked)} disabled={!session} />
            Show
          </label>
        </div>
        {!session && <p style={hint}>Create a session above to embed the real web client here, viewing that session.</p>}
        {session && showLiveClient && liveClientUrl && (
          <>
            <iframe
              key={liveClientUrl}
              src={liveClientUrl}
              style={clientFrame}
              title="Dot Watcher client"
            />
            <p style={hint}>
              Loads <code>{liveClientUrl}</code> unauthenticated (the invite-code URL form skips
              sign-in). Requires the client's own dev server running separately (<code>cd client &amp;&amp; npm run dev</code>) —
              blank/failed to load usually means it isn't. <a href={liveClientUrl} target="_blank" rel="noreferrer">Open in a new tab</a> instead.
            </p>
          </>
        )}
      </section>

      <section style={panel}>
        <div style={phonesHeader}>
          <h2 style={panelTitle}>Phones ({activePhones.length})</h2>
          <button style={addBtn} onClick={addPhone}>+ Add phone</button>
        </div>
        {phoneIds.length === 0 && <p style={hint}>Add a phone, then join it to a session using an invite code.</p>}
        <div style={phonesGrid}>
          {phoneIds.map((id, i) => (
            <PhoneSimulator
              key={id}
              id={id}
              index={i}
              defaultInviteCode={session?.inviteCode ?? ''}
              convergencePoint={convergencePoint}
              speedMps={speedMps}
              tickMs={tickMs}
              onSnapshot={handleSnapshot}
              onRemove={removePhone}
            />
          ))}
        </div>
      </section>
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
const sessionInfo: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.6rem', fontSize: '0.9rem', marginBottom: '0.4rem' }
const inviteCodeText: React.CSSProperties = { fontFamily: 'monospace', fontSize: '1.05rem', letterSpacing: '0.05em' }
const copyBtn: React.CSSProperties = { padding: '0.2rem 0.6rem', fontSize: '0.78rem', borderRadius: 6, border: '1px solid #cbd5e1', background: '#fff', cursor: 'pointer' }
const mutedText: React.CSSProperties = { color: '#94a3b8', fontSize: '0.8rem' }
const hint: React.CSSProperties = { fontSize: '0.82rem', color: '#64748b', marginBottom: '0.6rem' }
const bigMapStyle: React.CSSProperties = { height: 360, borderRadius: 8, border: '1px solid #e2e8f0' }
const statusLine: React.CSSProperties = { fontSize: '0.78rem', color: '#64748b', fontFamily: 'monospace', marginTop: '0.4rem' }
const movementRow: React.CSSProperties = { display: 'flex', gap: '1.5rem' }
const movementLabel: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: '0.3rem', fontSize: '0.82rem', color: '#475569', fontWeight: 600 }
const select: React.CSSProperties = { padding: '0.35rem 0.5rem', borderRadius: 6, border: '1px solid #cbd5e1', fontSize: '0.85rem' }
const phonesHeader: React.CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '0.6rem' }
const addBtn: React.CSSProperties = { padding: '0.4rem 0.9rem', borderRadius: 6, border: 'none', background: '#2563eb', color: '#fff', fontWeight: 700, fontSize: '0.85rem', cursor: 'pointer' }
const phonesGrid: React.CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: '0.75rem' }
const liveClientToggle: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.85rem', color: '#475569', cursor: 'pointer' }
const clientFrame: React.CSSProperties = { width: '100%', height: 520, border: '1px solid #e2e8f0', borderRadius: 8 }
