import { useEffect, useLayoutEffect, useState, type FormEvent, Fragment } from 'react'
import { apiHeaders } from './apiHeaders'

interface AdminUser {
  id: string
  username: string
  displayName: string
  createdAt: string
}

interface AdminSession {
  sessionId: string
  sessionName: string
  inviteCode: string
  ownerUsername: string
  memberCount: number
  createdAt: string
}

interface AdminMemberStats {
  displayName: string
  role: string
  positionCount: number
  lastPositionAt: string | null
}

type MemberStatsState = AdminMemberStats[] | 'loading' | { error: string }

const BEARER_TOKEN_KEY = 'adminBearerToken'
const APP_VERSION = import.meta.env.VITE_APP_VERSION ?? 'v-local'
// Matches the simulator's own VITE_CLIENT_URL convention (tools/simulator/.env.example) —
// each tool points at the other's default local port unless overridden.
const SIMULATOR_URL = import.meta.env.VITE_SIMULATOR_URL ?? 'http://localhost:5174'

export default function AdminPanel({ serverUrl }: { serverUrl: string }) {
  const [token, setToken] = useState(() => sessionStorage.getItem(BEARER_TOKEN_KEY) ?? (import.meta.env.VITE_BEARER_TOKEN as string | undefined) ?? '')
  const [tokenInput, setTokenInput] = useState(token)
  const [users, setUsers] = useState<AdminUser[]>([])
  const [sessions, setSessions] = useState<AdminSession[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [expandedSessionId, setExpandedSessionId] = useState<string | null>(null)
  const [memberStats, setMemberStats] = useState<Record<string, MemberStatsState>>({})
  const [clearingRecordsId, setClearingRecordsId] = useState<string | null>(null)
  const [selectedUserIds, setSelectedUserIds] = useState<Set<string>>(new Set())
  const [selectedSessionIds, setSelectedSessionIds] = useState<Set<string>>(new Set())
  const [bulkDeletingUsers, setBulkDeletingUsers] = useState(false)
  const [bulkDeletingSessions, setBulkDeletingSessions] = useState(false)

  useLayoutEffect(() => {
    const root = document.getElementById('root')
    const prev = { html: document.documentElement.style.overflow, body: document.body.style.overflow, root: root?.style.overflow ?? '' }
    document.documentElement.style.overflow = 'auto'
    document.body.style.overflow = 'auto'
    if (root) { root.style.overflow = 'auto'; root.style.height = 'auto' }
    return () => {
      document.documentElement.style.overflow = prev.html
      document.body.style.overflow = prev.body
      if (root) { root.style.overflow = prev.root; root.style.height = '' }
    }
  }, [])

  async function loadAll(bearerToken: string) {
    setLoading(true)
    setError(null)
    try {
      const headers = apiHeaders(bearerToken, 'web-admin')
      const [usersRes, sessionsRes] = await Promise.all([
        fetch(`${serverUrl}/admin/users`, { headers }),
        fetch(`${serverUrl}/admin/sessions`, { headers }),
      ])
      if (usersRes.status === 401 || sessionsRes.status === 401) {
        setError('Invalid bearer token.')
        return
      }
      if (!usersRes.ok || !sessionsRes.ok) {
        setError('Failed to load data.')
        return
      }
      setUsers(await usersRes.json() as AdminUser[])
      setSessions(await sessionsRes.json() as AdminSession[])
      setSelectedUserIds(new Set())
      setSelectedSessionIds(new Set())
    } catch {
      setError('Network error.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (token) void loadAll(token)
  }, [])

  function handleTokenSubmit(event: FormEvent) {
    event.preventDefault()
    const t = tokenInput.trim()
    sessionStorage.setItem(BEARER_TOKEN_KEY, t)
    setToken(t)
    void loadAll(t)
  }

  function toggleUserSelected(id: string) {
    setSelectedUserIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }

  function toggleAllUsersSelected() {
    setSelectedUserIds(prev => prev.size === users.length ? new Set() : new Set(users.map(u => u.id)))
  }

  function toggleSessionSelected(id: string) {
    setSelectedSessionIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id); else next.add(id)
      return next
    })
  }

  function toggleAllSessionsSelected() {
    setSelectedSessionIds(prev => prev.size === sessions.length ? new Set() : new Set(sessions.map(s => s.sessionId)))
  }

  async function handleBulkDeleteUsers() {
    const ids = Array.from(selectedUserIds)
    if (ids.length === 0) return
    if (!window.confirm(`Delete ${ids.length} user${ids.length !== 1 ? 's' : ''}? This removes their accounts, sessions, and all location data. This cannot be undone.`)) return
    setBulkDeletingUsers(true)
    try {
      const results = await Promise.all(ids.map(id =>
        fetch(`${serverUrl}/admin/users/${id}`, { method: 'DELETE', headers: apiHeaders(token, 'web-admin') })
          .then(r => ({ id, ok: r.ok }))
          .catch(() => ({ id, ok: false }))
      ))
      const succeeded = new Set(results.filter(r => r.ok).map(r => r.id))
      setUsers(prev => prev.filter(u => !succeeded.has(u.id)))
      setSelectedUserIds(prev => new Set(Array.from(prev).filter(id => !succeeded.has(id))))
      if (succeeded.size < ids.length) setError(`${ids.length - succeeded.size} user delete(s) failed.`)
    } finally {
      setBulkDeletingUsers(false)
    }
  }

  async function handleBulkDeleteSessions() {
    const ids = Array.from(selectedSessionIds)
    if (ids.length === 0) return
    if (!window.confirm(`Delete ${ids.length} session${ids.length !== 1 ? 's' : ''}? This removes all members and location data. This cannot be undone.`)) return
    setBulkDeletingSessions(true)
    try {
      const results = await Promise.all(ids.map(id =>
        fetch(`${serverUrl}/admin/sessions/${id}`, { method: 'DELETE', headers: apiHeaders(token, 'web-admin') })
          .then(r => ({ id, ok: r.ok }))
          .catch(() => ({ id, ok: false }))
      ))
      const succeeded = new Set(results.filter(r => r.ok).map(r => r.id))
      setSessions(prev => prev.filter(s => !succeeded.has(s.sessionId)))
      setSelectedSessionIds(prev => new Set(Array.from(prev).filter(id => !succeeded.has(id))))
      if (succeeded.has(expandedSessionId ?? '')) setExpandedSessionId(null)
      if (succeeded.size < ids.length) setError(`${ids.length - succeeded.size} session delete(s) failed.`)
    } finally {
      setBulkDeletingSessions(false)
    }
  }

  async function loadMemberStats(sessionId: string) {
    setMemberStats(prev => ({ ...prev, [sessionId]: 'loading' }))
    try {
      const r = await fetch(`${serverUrl}/admin/sessions/${sessionId}/member-stats`, {
        headers: apiHeaders(token, 'web-admin'),
      })
      if (!r.ok) {
        setMemberStats(prev => ({ ...prev, [sessionId]: { error: `HTTP ${r.status}` } }))
        return
      }
      const stats = await r.json() as AdminMemberStats[]
      setMemberStats(prev => ({ ...prev, [sessionId]: stats }))
    } catch (e) {
      setMemberStats(prev => ({ ...prev, [sessionId]: { error: String(e) } }))
    }
  }

  async function handleSessionRowClick(session: AdminSession) {
    if (expandedSessionId === session.sessionId) {
      setExpandedSessionId(null)
      return
    }
    setExpandedSessionId(session.sessionId)
    await loadMemberStats(session.sessionId)
  }

  async function handleClearRecords(e: React.MouseEvent, sessionId: string) {
    e.stopPropagation()
    if (!window.confirm('Clear all location records for this session? This cannot be undone.')) return
    setClearingRecordsId(sessionId)
    try {
      const r = await fetch(`${serverUrl}/sessions/${sessionId}/recording`, {
        method: 'DELETE',
        headers: apiHeaders(token, 'web-admin'),
      })
      if (r.ok || r.status === 204) {
        await loadMemberStats(sessionId)
      } else {
        setError(`Failed to clear records (HTTP ${r.status}).`)
      }
    } catch {
      setError('Network error.')
    } finally {
      setClearingRecordsId(null)
    }
  }

  function timeAgo(timestamp: string) {
    try {
      const mins = Math.floor((Date.now() - new Date(timestamp).getTime()) / 60000)
      if (mins < 1) return 'just now'
      if (mins === 1) return '1 min ago'
      if (mins < 60) return `${mins} mins ago`
      const hrs = Math.floor(mins / 60)
      if (hrs === 1) return '1 hr ago'
      if (hrs < 24) return `${hrs} hrs ago`
      return `${Math.floor(hrs / 24)}d ago`
    } catch { return timestamp }
  }

  return (
    <div style={page}>
      <h1 style={heading}>Admin — Users</h1>
      <p style={versionText}>{APP_VERSION}</p>
      {/* Dev-only: import.meta.env.DEV is false in the built app, so this never appears on the
          deployed staging/production admin page, where no simulator is running to link to. */}
      {import.meta.env.DEV && (
        <a href={SIMULATOR_URL} target="_blank" rel="noopener noreferrer" style={simulatorLink}>
          🧪 Open simulator
        </a>
      )}

      {!token && (
        <form onSubmit={handleTokenSubmit} style={tokenForm}>
          <input
            style={tokenInput_}
            value={tokenInput}
            onChange={e => setTokenInput(e.target.value)}
            placeholder="Bearer token"
            type="password"
          />
          <button style={primaryBtn} type="submit">Sign in</button>
        </form>
      )}

      {token && (
        <div style={toolbar}>
          <button style={secondaryBtn} onClick={() => void loadAll(token)} disabled={loading}>
            {loading ? 'Loading…' : 'Refresh'}
          </button>
          <button style={secondaryBtn} onClick={() => {
            sessionStorage.removeItem(BEARER_TOKEN_KEY)
            setToken('')
            setTokenInput('')
            setUsers([])
          }}>
            Sign out
          </button>
        </div>
      )}

      {error && <p style={errorText}>{error}</p>}

      <div style={subheadingRow}>
        <h2 style={subheadingNoMargin}>Users</h2>
        {selectedUserIds.size > 0 && (
          <button
            style={deleteBtn}
            onClick={() => void handleBulkDeleteUsers()}
            disabled={bulkDeletingUsers}
          >
            {bulkDeletingUsers ? '…' : `Delete selected (${selectedUserIds.size})`}
          </button>
        )}
      </div>
      {users.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={checkboxTh}>
                <input
                  type="checkbox"
                  checked={selectedUserIds.size === users.length}
                  onChange={toggleAllUsersSelected}
                  aria-label="Select all users"
                />
              </th>
              <th style={th}>Username</th>
              <th style={th}>Display name</th>
              <th style={th}>Created</th>
            </tr>
          </thead>
          <tbody>
            {users.map(user => (
              <tr key={user.id} style={tr}>
                <td style={checkboxTd}>
                  <input
                    type="checkbox"
                    checked={selectedUserIds.has(user.id)}
                    onChange={() => toggleUserSelected(user.id)}
                    aria-label={`Select ${user.username}`}
                  />
                </td>
                <td style={td}>{user.username}</td>
                <td style={td}>{user.displayName}</td>
                <td style={td}>{timeAgo(user.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {token && !loading && users.length === 0 && !error && (
        <p style={emptyText}>No users.</p>
      )}

      <div style={subheadingRow}>
        <h2 style={subheadingNoMargin}>Sessions</h2>
        {selectedSessionIds.size > 0 && (
          <button
            style={deleteBtn}
            onClick={() => void handleBulkDeleteSessions()}
            disabled={bulkDeletingSessions}
          >
            {bulkDeletingSessions ? '…' : `Delete selected (${selectedSessionIds.size})`}
          </button>
        )}
      </div>
      {sessions.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={checkboxTh}>
                <input
                  type="checkbox"
                  checked={selectedSessionIds.size === sessions.length}
                  onChange={toggleAllSessionsSelected}
                  aria-label="Select all sessions"
                />
              </th>
              <th style={th}>Name</th>
              <th style={th}>Invite code</th>
              <th style={th}>Owner</th>
              <th style={th}>Members</th>
              <th style={th}>Created</th>
            </tr>
          </thead>
          <tbody>
            {sessions.map(session => {
              const isExpanded = expandedSessionId === session.sessionId
              return (
                <Fragment key={session.sessionId}>
                  <tr
                    style={{ ...tr, cursor: 'pointer', background: isExpanded ? '#f8fafc' : undefined }}
                    onClick={() => void handleSessionRowClick(session)}
                  >
                    <td style={checkboxTd} onClick={e => e.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={selectedSessionIds.has(session.sessionId)}
                        onChange={() => toggleSessionSelected(session.sessionId)}
                        aria-label={`Select ${session.sessionName}`}
                      />
                    </td>
                    <td style={td}>{session.sessionName}</td>
                    <td style={td}>
                      {session.inviteCode}
                      <a
                        href={`/code/${session.inviteCode}`}
                        target="_blank"
                        rel="noopener noreferrer"
                        onClick={e => e.stopPropagation()}
                        title="Open map"
                        style={mapLink}
                      >
                        🗺️
                      </a>
                    </td>
                    <td style={td}>{session.ownerUsername}</td>
                    <td style={td}>{session.memberCount}</td>
                    <td style={td}>{timeAgo(session.createdAt)}</td>
                  </tr>
                  {isExpanded && (
                    <tr>
                      <td
                        colSpan={6}
                        style={{ padding: 0, cursor: 'pointer' }}
                        onClick={() => setExpandedSessionId(null)}
                      >
                        {(() => {
                          const stats = memberStats[session.sessionId]
                          return (
                            <div style={recordsPanel} onClick={e => e.stopPropagation()}>
                              <div style={recordsHeader}>
                                <span style={recordsLabel}>
                                  {stats === 'loading' ? 'Loading…'
                                    : !Array.isArray(stats) ? `Error: ${(stats as { error: string }).error}`
                                    : `${stats.length} member${stats.length !== 1 ? 's' : ''}`}
                                </span>
                                <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                                  <button
                                    style={clearBtn}
                                    onClick={e => void handleClearRecords(e, session.sessionId)}
                                    disabled={clearingRecordsId === session.sessionId}
                                  >
                                    {clearingRecordsId === session.sessionId ? '…' : 'Clear positions'}
                                  </button>
                                  <button style={collapseBtn} onClick={() => setExpandedSessionId(null)}>✕</button>
                                </div>
                              </div>
                              {Array.isArray(stats) && stats.length === 0 && (
                                <p style={{ padding: '0.5rem 0.75rem', color: '#94a3b8', fontSize: '0.85rem' }}>No members.</p>
                              )}
                              {Array.isArray(stats) && stats.length > 0 && (
                                <table style={{ ...table, fontSize: '0.8rem' }}>
                                  <thead>
                                    <tr>
                                      <th style={recTh}>Name</th>
                                      <th style={recTh}>Role</th>
                                      <th style={recTh}>Positions</th>
                                      <th style={recTh}>Last position</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {stats.map((m, i) => (
                                      <tr key={i} style={{ borderBottom: '1px solid #f1f5f9' }}>
                                        <td style={{ ...recTd, fontWeight: 600 }}>{m.displayName}</td>
                                        <td style={{ ...recTd, color: '#64748b' }}>{m.role}</td>
                                        <td style={recTd}>{m.positionCount}</td>
                                        <td style={{ ...recTd, color: '#64748b' }}>{m.lastPositionAt ? timeAgo(m.lastPositionAt) : '—'}</td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              )}
                            </div>
                          )
                        })()}
                      </td>
                    </tr>
                  )}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      )}
      {token && !loading && sessions.length === 0 && !error && (
        <p style={emptyText}>No sessions.</p>
      )}
    </div>
  )
}

const page: React.CSSProperties = {
  maxWidth: 720,
  margin: '2rem auto',
  padding: '0 1rem',
  fontFamily: 'system-ui, sans-serif',
}

const subheading: React.CSSProperties = {
  fontSize: '1.1rem',
  fontWeight: 700,
  margin: '2rem 0 0.75rem',
}

const subheadingNoMargin: React.CSSProperties = {
  ...subheading,
  margin: 0,
}

const subheadingRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '0.75rem',
  margin: '2rem 0 0.75rem',
}

const heading: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  marginBottom: '1rem',
}

const tokenForm: React.CSSProperties = {
  display: 'flex',
  gap: '0.5rem',
  marginBottom: '1rem',
}

const tokenInput_: React.CSSProperties = {
  flex: 1,
  fontSize: '1rem',
  padding: '0.5rem 0.75rem',
  borderRadius: 6,
  border: '1.5px solid #ccc',
}

const toolbar: React.CSSProperties = {
  display: 'flex',
  gap: '0.5rem',
  marginBottom: '1rem',
}

const primaryBtn: React.CSSProperties = {
  padding: '0.5rem 1rem',
  borderRadius: 6,
  border: 'none',
  background: '#3b82f6',
  color: '#fff',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
  fontWeight: 600,
}

const secondaryBtn: React.CSSProperties = {
  ...primaryBtn,
  background: '#f1f5f9',
  color: '#1e293b',
  border: '1px solid #e2e8f0',
}

const deleteBtn: React.CSSProperties = {
  ...primaryBtn,
  background: '#ef4444',
  fontSize: '0.8rem',
  padding: '0.3rem 0.6rem',
}

const clearBtn: React.CSSProperties = {
  ...primaryBtn,
  background: '#f97316',
  fontSize: '0.75rem',
  padding: '0.2rem 0.5rem',
}

const collapseBtn: React.CSSProperties = {
  background: 'none',
  border: 'none',
  cursor: 'pointer',
  color: '#94a3b8',
  fontSize: '0.85rem',
  padding: '0.1rem 0.3rem',
  lineHeight: 1,
}

const errorText: React.CSSProperties = {
  color: '#dc2626',
  marginBottom: '1rem',
}

const emptyText: React.CSSProperties = {
  color: '#64748b',
}

const table: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  fontSize: '0.9rem',
}

const th: React.CSSProperties = {
  textAlign: 'left',
  padding: '0.5rem 0.75rem',
  borderBottom: '2px solid #e2e8f0',
  color: '#64748b',
  fontWeight: 600,
}

const tr: React.CSSProperties = {
  borderBottom: '1px solid #f1f5f9',
}

const td: React.CSSProperties = {
  padding: '0.5rem 0.75rem',
  verticalAlign: 'middle',
}

const checkboxTh: React.CSSProperties = {
  ...th,
  width: '1%',
  whiteSpace: 'nowrap',
}

const checkboxTd: React.CSSProperties = {
  ...td,
  width: '1%',
  whiteSpace: 'nowrap',
}

const mapLink: React.CSSProperties = {
  marginLeft: '0.5rem',
  textDecoration: 'none',
}

const versionText: React.CSSProperties = {
  color: '#94a3b8',
  fontSize: '0.75rem',
  marginBottom: '1rem',
}

const simulatorLink: React.CSSProperties = {
  display: 'inline-block',
  marginBottom: '1rem',
  color: '#3b82f6',
  fontSize: '0.85rem',
  fontWeight: 600,
  textDecoration: 'none',
}

const recordsPanel: React.CSSProperties = {
  background: '#f8fafc',
  borderTop: '1px solid #e2e8f0',
  borderBottom: '2px solid #e2e8f0',
}

const recordsHeader: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '0.4rem 0.75rem',
  borderBottom: '1px solid #e2e8f0',
}

const recordsLabel: React.CSSProperties = {
  fontSize: '0.8rem',
  color: '#64748b',
  fontWeight: 600,
}

const recTh: React.CSSProperties = {
  textAlign: 'left',
  padding: '0.3rem 0.75rem',
  borderBottom: '1px solid #e2e8f0',
  color: '#94a3b8',
  fontWeight: 600,
  background: '#f8fafc',
}

const recTd: React.CSSProperties = {
  padding: '0.25rem 0.75rem',
  verticalAlign: 'middle',
  fontFamily: 'monospace',
  fontSize: '0.8rem',
}

