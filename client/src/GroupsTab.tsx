import { Fragment, useCallback, useEffect, useState, type FormEvent } from 'react'
import { apiHeaders } from './apiHeaders'
import JoinLocationPicker, { SIMULATOR_DEFAULT, type LatLon } from './JoinLocationPicker.tsx'

// The admin Groups tab: every group (a named session, see .ai/plans/go-and-groups.md) with its organiser,
// time left, nearby area and entry code. A row opens an editor for those, plus End / Reopen / Delete and the
// members (add by username, with no distance or code check, or remove). Server: Controllers/AdminGroupsController.cs.

interface AdminGroup {
  sessionId: string
  sessionName: string
  inviteCode: string
  ownerUserId: string
  ownerUsername: string
  ownerDisplayName: string
  memberCount: number
  createdAt: string
  lengthHours: number
  endsAt: string
  status: 'active' | 'expired' | 'ended'
  nearby: boolean
  nearbyRadiusMeters: number | null
  joinLatitude: number | null
  joinLongitude: number | null
  entryCode: string | null
}

interface AdminGroupMember {
  userId: string
  username: string
  displayName: string
  role: string
  joinedAt: string
  leftAt: string | null
}

const RADII = [50, 100, 250, 500]
const MAX_LENGTH_HOURS = 240

type Groups = AdminGroup[] | 'loading' | 'unsupported' | { error: string }

export default function GroupsTab({ serverUrl, token }: { serverUrl: string; token: string }) {
  const [groups, setGroups] = useState<Groups>('loading')
  const [hideFinished, setHideFinished] = useState(true)
  const [openId, setOpenId] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const res = await fetch(`${serverUrl}/admin/groups`, { headers: apiHeaders(token, 'web-admin') })
      if (res.status === 404) { setGroups('unsupported'); return }
      if (!res.ok) { setGroups({ error: `HTTP ${res.status}` }); return }
      setGroups(await res.json() as AdminGroup[])
    } catch (e) {
      setGroups({ error: e instanceof Error ? e.message : String(e) })
    }
  }, [serverUrl, token])

  useEffect(() => { void load() }, [load])

  const shown = Array.isArray(groups) ? groups.filter(g => !hideFinished || g.status === 'active') : []

  return (
    <>
      <div style={headerRow}>
        <h2 style={subheading}>Groups</h2>
        <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
          <label style={{ fontSize: '0.875rem', display: 'flex', gap: '0.35rem', alignItems: 'center' }}>
            <input type="checkbox" checked={hideFinished} onChange={e => setHideFinished(e.target.checked)} />
            Active only
          </label>
          <button style={secondaryBtn} onClick={() => void load()}>Refresh</button>
        </div>
      </div>
      <p style={note}>
        Every named session counts as a group. Changes here skip the organiser checks, so use them on test groups.
        Phones pick changes up when they next load their groups (coming back to the app, or pulling down).
      </p>
      {groups === 'loading' && <p style={muted}>Loading…</p>}
      {groups === 'unsupported' && <p style={errorText}>This server doesn't have the Groups admin endpoints yet.</p>}
      {typeof groups === 'object' && !Array.isArray(groups) && <p style={errorText}>Couldn't load groups: {groups.error}</p>}
      {Array.isArray(groups) && shown.length === 0 && (
        <p style={muted}>{groups.length === 0 ? 'No groups.' : 'No active groups. Untick "Active only" to see the rest.'}</p>
      )}
      {shown.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={th}>Name</th>
              <th style={th}>Organiser</th>
              <th style={th}>Members</th>
              <th style={th}>Status</th>
              <th style={th}>Nearby</th>
              <th style={th}>Entry code</th>
              <th style={th}>Invite code</th>
            </tr>
          </thead>
          <tbody>
            {shown.map(group => {
              const isOpen = openId === group.sessionId
              return (
                <Fragment key={group.sessionId}>
                  <tr
                    style={{ ...tr, cursor: 'pointer', background: isOpen ? '#f8fafc' : undefined }}
                    onClick={() => setOpenId(isOpen ? null : group.sessionId)}
                  >
                    <td style={{ ...td, fontWeight: 600 }}>{group.sessionName}</td>
                    <td style={td} title={group.ownerUsername}>{group.ownerDisplayName}</td>
                    <td style={td}>{group.memberCount}</td>
                    <td style={td} title={`Ends ${new Date(group.endsAt).toLocaleString()}`}>{statusText(group)}</td>
                    <td style={td}>{group.nearby ? `${group.nearbyRadiusMeters ?? 100} m` : 'Off'}</td>
                    <td style={{ ...td, fontFamily: 'ui-monospace, monospace' }}>{group.entryCode ?? '—'}</td>
                    <td style={{ ...td, fontFamily: 'ui-monospace, monospace' }}>{group.inviteCode}</td>
                  </tr>
                  {isOpen && (
                    <tr>
                      <td colSpan={7} style={{ padding: 0 }}>
                        <GroupEditor
                          key={`${group.sessionId}-${group.status}`}
                          group={group}
                          serverUrl={serverUrl}
                          token={token}
                          onChanged={() => void load()}
                          onDeleted={() => { setOpenId(null); void load() }}
                        />
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      )}
    </>
  )
}

function statusText(group: AdminGroup): string {
  if (group.status === 'ended') return 'Ended'
  if (group.status === 'expired') return 'Expired'
  const minutes = Math.floor((Date.parse(group.endsAt) - Date.now()) / 60000)
  if (minutes < 60) return `Active · ${Math.max(minutes, 0)} m left`
  const hours = Math.floor(minutes / 60)
  return hours < 48 ? `Active · ${hours} h ${minutes % 60} m left` : `Active · ${Math.floor(hours / 24)} d left`
}

function GroupEditor({ group, serverUrl, token, onChanged, onDeleted }: {
  group: AdminGroup
  serverUrl: string
  token: string
  onChanged: () => void
  onDeleted: () => void
}) {
  const [name, setName] = useState(group.sessionName)
  const [lengthHours, setLengthHours] = useState(String(group.lengthHours))
  const [nearby, setNearby] = useState(group.nearby)
  const [radius, setRadius] = useState(group.nearbyRadiusMeters ?? 100)
  const [latitude, setLatitude] = useState(group.joinLatitude?.toString() ?? '')
  const [longitude, setLongitude] = useState(group.joinLongitude?.toString() ?? '')
  const [entryCode, setEntryCode] = useState(group.entryCode ?? '')
  const [members, setMembers] = useState<AdminGroupMember[] | null>(null)
  const [addUsername, setAddUsername] = useState('')
  const [addDisplayName, setAddDisplayName] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null)

  const base = `${serverUrl}/admin/groups/${group.sessionId}`
  const headers = apiHeaders(token, 'web-admin')

  const loadMembers = useCallback(async () => {
    const res = await fetch(`${base}/members`, { headers: apiHeaders(token, 'web-admin') })
    setMembers(res.ok ? await res.json() as AdminGroupMember[] : [])
  }, [base, token])

  useEffect(() => { void loadMembers() }, [loadMembers])

  // Sends one request; on success refreshes the list (and members), on failure shows the server's words.
  async function send(method: string, url: string, body: object | undefined, done: string) {
    setBusy(true)
    setMessage(null)
    try {
      const res = await fetch(url, {
        method,
        headers: body ? { ...headers, 'Content-Type': 'application/json' } : headers,
        body: body ? JSON.stringify(body) : undefined,
      })
      if (!res.ok) {
        const error = await res.json().then(j => (j as { error?: string }).error).catch(() => undefined)
        setMessage({ text: error ?? `HTTP ${res.status}`, isError: true })
        return false
      }
      setMessage({ text: done, isError: false })
      onChanged()
      void loadMembers()
      return true
    } catch (e) {
      setMessage({ text: e instanceof Error ? e.message : String(e), isError: true })
      return false
    } finally {
      setBusy(false)
    }
  }

  function save(e: FormEvent) {
    e.preventDefault()
    // Only what changed, so a stale form can't undo someone else's change to another field.
    const changes: Record<string, unknown> = {}
    if (name.trim() !== group.sessionName) changes.sessionName = name
    if (Number(lengthHours) !== group.lengthHours) changes.lengthHours = Number(lengthHours)
    if (nearby !== group.nearby) changes.nearby = nearby
    if (radius !== (group.nearbyRadiusMeters ?? 100)) changes.nearbyRadiusMeters = radius
    if (latitude !== (group.joinLatitude?.toString() ?? '') || longitude !== (group.joinLongitude?.toString() ?? '')) {
      changes.joinLatitude = latitude === '' ? null : Number(latitude)
      changes.joinLongitude = longitude === '' ? null : Number(longitude)
    }
    if (entryCode.trim().toUpperCase() !== (group.entryCode ?? '')) changes.entryCode = entryCode
    if (Object.keys(changes).length === 0) {
      setMessage({ text: 'Nothing changed.', isError: false })
      return
    }
    void send('PATCH', base, changes, 'Saved.')
  }

  async function addMember(e: FormEvent) {
    e.preventDefault()
    const added = await send('POST', `${base}/members`, {
      username: addUsername,
      displayName: addDisplayName || null,
    }, `Added ${addUsername.trim()}.`)
    if (added) { setAddUsername(''); setAddDisplayName('') }
  }

  async function remove() {
    if (!window.confirm(`Delete ${group.sessionName} and its recording? This can't be undone.`)) return
    setBusy(true)
    const res = await fetch(`${serverUrl}/admin/sessions/${group.sessionId}`, { method: 'DELETE', headers })
    setBusy(false)
    if (res.ok) onDeleted()
    else setMessage({ text: `Couldn't delete: HTTP ${res.status}`, isError: true })
  }

  // The point on the map: what the two boxes say, when they hold a valid position.
  const lat = Number(latitude), lon = Number(longitude)
  const chosenPoint: LatLon | null = latitude.trim() !== '' && longitude.trim() !== ''
    && Number.isFinite(lat) && Number.isFinite(lon) && Math.abs(lat) <= 90 && Math.abs(lon) <= 180
    ? { latitude: lat, longitude: lon } : null

  function setPoint(point: LatLon) {
    setLatitude(String(point.latitude))
    setLongitude(String(point.longitude))
  }

  // Some browsers never share a location: the VS Code browser refuses (or never answers), so say what to do
  // instead of failing quietly.
  function fillMyLocation() {
    const blocked = "This browser won't share its location (the VS Code browser never does). " +
      'Open this page in Safari or Chrome, or click the map.'
    if (!navigator.geolocation) {
      setMessage({ text: blocked, isError: true })
      return
    }
    setMessage({ text: 'Getting your location…', isError: false })
    navigator.geolocation.getCurrentPosition(
      at => {
        setPoint({ latitude: Math.round(at.coords.latitude * 1e6) / 1e6, longitude: Math.round(at.coords.longitude * 1e6) / 1e6 })
        setMessage({ text: 'Moved to your location. Save to keep it.', isError: false })
      },
      error => setMessage({
        text: error.code === error.PERMISSION_DENIED ? blocked : `Couldn't get your location: ${error.message}`,
        isError: true,
      }),
      { timeout: 10_000 },
    )
  }

  const endsAt = new Date(Date.parse(group.createdAt) + (Number(lengthHours) || 0) * 3600_000)
  const current = members?.filter(m => m.leftAt === null) ?? []
  const left = members?.filter(m => m.leftAt !== null) ?? []

  return (
    <div style={panel}>
      <form onSubmit={save}>
      <div style={formGrid}>
        <label style={field}>
          <span style={fieldLabel}>Name</span>
          <input style={input} value={name} maxLength={30} onChange={e => setName(e.target.value)} />
        </label>
        <label style={field}>
          <span style={fieldLabel}>Length (hours from created)</span>
          <input
            style={input} type="number" min={1} max={MAX_LENGTH_HOURS} value={lengthHours}
            onChange={e => setLengthHours(e.target.value)}
          />
          <span style={hint}>
            Created {new Date(group.createdAt).toLocaleString()} · ends {endsAt.toLocaleString()}
          </span>
        </label>
        <label style={field}>
          <span style={fieldLabel}>Entry code</span>
          <input
            style={{ ...input, fontFamily: 'ui-monospace, monospace', textTransform: 'uppercase' }}
            value={entryCode} maxLength={8} placeholder="None" onChange={e => setEntryCode(e.target.value)}
          />
          <span style={hint}>4-8 letters or numbers; empty removes it.</span>
        </label>
        <div style={field}>
          <span style={fieldLabel}>Nearby</span>
          <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
            <label style={{ display: 'flex', gap: '0.3rem', alignItems: 'center', fontSize: '0.875rem' }}>
              <input type="checkbox" checked={nearby} onChange={e => setNearby(e.target.checked)} />
              Show to people nearby
            </label>
            <select style={input} value={radius} onChange={e => setRadius(Number(e.target.value))} aria-label="Join area">
              {RADII.map(r => <option key={r} value={r}>{r} m</option>)}
            </select>
          </div>
        </div>
      </div>
      <div style={{ ...field, marginTop: '1rem' }}>
        <span style={fieldLabel}>Join location</span>
        <span style={hint}>
          Click the map or drag the pin. People inside the yellow area see the group on their start screen
          (with "Show to people nearby" on). Save to keep it.
        </span>
        <JoinLocationPicker value={chosenPoint} radiusMeters={radius} onChange={setPoint} />
        <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', alignItems: 'center' }}>
          <input style={{ ...input, width: '9rem' }} value={latitude} placeholder="Latitude" aria-label="Latitude"
            onChange={e => setLatitude(e.target.value)} />
          <input style={{ ...input, width: '9rem' }} value={longitude} placeholder="Longitude" aria-label="Longitude"
            onChange={e => setLongitude(e.target.value)} />
          <button style={smallBtn} type="button" onClick={() => setPoint(SIMULATOR_DEFAULT)}
            title="Where the iOS Simulator puts a phone unless told otherwise">
            Simulator default (Apple Park)
          </button>
          <button style={smallBtn} type="button" onClick={fillMyLocation}>My location</button>
        </div>
      </div>
      <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginTop: '1rem' }}>
        <button style={primaryBtn} type="submit" disabled={busy}>Save</button>
        {group.status === 'ended'
          ? <button style={secondaryBtn} type="button" disabled={busy}
              onClick={() => void send('POST', `${base}/reopen`, undefined, 'Reopened.')}>Reopen</button>
          : <button style={secondaryBtn} type="button" disabled={busy}
              onClick={() => { if (window.confirm(`End ${group.sessionName} for everyone?`)) void send('POST', `${base}/end`, undefined, 'Ended.') }}>
              End group
            </button>}
        <button style={deleteBtn} type="button" disabled={busy} onClick={() => void remove()}>Delete</button>
      </div>
      </form>
      {message && <p style={message.isError ? errorText : okText}>{message.text}</p>}

      <h3 style={membersHeading}>Members</h3>
      {members === null && <p style={muted}>Loading…</p>}
      {members !== null && current.length === 0 && <p style={muted}>Nobody is in it now.</p>}
      {current.length > 0 && (
        <table style={{ ...table, fontSize: '0.8rem' }}>
          <thead>
            <tr>
              <th style={th}>Name in group</th>
              <th style={th}>Username</th>
              <th style={th}>Role</th>
              <th style={th}>Joined</th>
              <th style={th}></th>
            </tr>
          </thead>
          <tbody>
            {current.map(m => (
              <tr key={m.userId} style={tr}>
                <td style={{ ...td, fontWeight: 600 }}>
                  {m.displayName}{m.userId === group.ownerUserId ? ' (organiser)' : ''}
                </td>
                <td style={td}>{m.username}</td>
                <td style={td}>{m.role}</td>
                <td style={td}>{new Date(m.joinedAt).toLocaleString()}</td>
                <td style={td}>
                  <button style={smallBtn} disabled={busy}
                    onClick={() => void send('DELETE', `${base}/members/${m.userId}`, undefined, `Removed ${m.username}.`)}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {left.length > 0 && (
        <p style={{ ...muted, fontSize: '0.8rem', marginTop: '0.5rem' }}>
          Left: {left.map(m => m.username).join(', ')}
        </p>
      )}
      {group.status !== 'ended' && (
        <form onSubmit={e => void addMember(e)} style={{ display: 'flex', gap: '0.5rem', marginTop: '0.75rem', flexWrap: 'wrap' }}>
          <input style={input} value={addUsername} placeholder="Username" aria-label="Username to add"
            onChange={e => setAddUsername(e.target.value)} />
          <input style={input} value={addDisplayName} placeholder="Name in group (optional)" aria-label="Name in group"
            onChange={e => setAddDisplayName(e.target.value)} />
          <button style={secondaryBtn} type="submit" disabled={busy || addUsername.trim() === ''}>Add member</button>
        </form>
      )}
    </div>
  )
}

const subheading: React.CSSProperties = { fontSize: '1.1rem', fontWeight: 700, margin: 0 }
const headerRow: React.CSSProperties = {
  display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '0.75rem', margin: '2rem 0 0.25rem',
}
const note: React.CSSProperties = { margin: '0.25rem 0 0.75rem', fontSize: '0.875rem', opacity: 0.7 }
const muted: React.CSSProperties = { color: '#64748b' }
const errorText: React.CSSProperties = { color: '#dc2626', margin: '0.5rem 0' }
const okText: React.CSSProperties = { color: '#15803d', margin: '0.5rem 0' }
const table: React.CSSProperties = { width: '100%', borderCollapse: 'collapse', fontSize: '0.9rem' }
const th: React.CSSProperties = {
  textAlign: 'left', padding: '0.5rem 0.75rem', borderBottom: '2px solid #e2e8f0', color: '#64748b', fontWeight: 600,
}
const tr: React.CSSProperties = { borderBottom: '1px solid #f1f5f9' }
const td: React.CSSProperties = { padding: '0.5rem 0.75rem', verticalAlign: 'middle' }
const panel: React.CSSProperties = {
  padding: '1rem', background: '#f8fafc', borderTop: '1px solid #e2e8f0', borderBottom: '2px solid #e2e8f0',
}
const formGrid: React.CSSProperties = {
  display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(16rem, 1fr))', gap: '0.75rem 1rem',
}
const field: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: '0.25rem' }
const fieldLabel: React.CSSProperties = { fontSize: '0.8rem', fontWeight: 600, color: '#475569' }
const hint: React.CSSProperties = { fontSize: '0.75rem', color: '#64748b' }
const input: React.CSSProperties = {
  fontSize: '0.9rem', padding: '0.4rem 0.6rem', borderRadius: 6, border: '1.5px solid #ccc', background: '#fff',
}
const membersHeading: React.CSSProperties = { fontSize: '0.95rem', fontWeight: 700, margin: '1.25rem 0 0.5rem' }
const primaryBtn: React.CSSProperties = {
  padding: '0.5rem 1rem', borderRadius: 6, border: 'none', background: '#3b82f6', color: '#fff', cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif', fontSize: '0.9rem', fontWeight: 600,
}
const secondaryBtn: React.CSSProperties = {
  ...primaryBtn, background: '#f1f5f9', color: '#1e293b', border: '1px solid #e2e8f0',
}
const deleteBtn: React.CSSProperties = { ...primaryBtn, background: '#ef4444' }
const smallBtn: React.CSSProperties = { ...secondaryBtn, fontSize: '0.75rem', padding: '0.2rem 0.5rem' }
