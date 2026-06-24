import { useEffect, useLayoutEffect, useState, type FormEvent, Fragment } from 'react'

interface AdminUser {
  id: string
  username: string
  displayName: string
  createdAt: string
}

interface AdminSession {
  sessionId: string
  sessionName: string
  ownerUsername: string
  memberCount: number
  createdAt: string
}

interface AdminJoinRequest {
  requestId: string
  sessionId: string
  username: string
  displayName: string
  status: 'pending' | 'approved' | 'denied'
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

export default function AdminPanel({ serverUrl }: { serverUrl: string }) {
  const [token, setToken] = useState(() => sessionStorage.getItem(BEARER_TOKEN_KEY) ?? (import.meta.env.VITE_BEARER_TOKEN as string | undefined) ?? '')
  const [tokenInput, setTokenInput] = useState(token)
  const [users, setUsers] = useState<AdminUser[]>([])
  const [sessions, setSessions] = useState<AdminSession[]>([])
  const [joinRequests, setJoinRequests] = useState<AdminJoinRequest[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [deletingSessionId, setDeletingSessionId] = useState<string | null>(null)
  const [expandedSessionId, setExpandedSessionId] = useState<string | null>(null)
  const [memberStats, setMemberStats] = useState<Record<string, MemberStatsState>>({})
  const [clearingRecordsId, setClearingRecordsId] = useState<string | null>(null)
  const [deletingJoinRequestId, setDeletingJoinRequestId] = useState<string | null>(null)

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
      const headers = { Authorization: `Bearer ${bearerToken}` }
      const [usersRes, sessionsRes, requestsRes] = await Promise.all([
        fetch(`${serverUrl}/admin/users`, { headers }),
        fetch(`${serverUrl}/admin/sessions`, { headers }),
        fetch(`${serverUrl}/admin/join-requests`, { headers }),
      ])
      if (usersRes.status === 401 || sessionsRes.status === 401 || requestsRes.status === 401) {
        setError('Invalid bearer token.')
        return
      }
      if (!usersRes.ok || !sessionsRes.ok || !requestsRes.ok) {
        setError('Failed to load data.')
        return
      }
      setUsers(await usersRes.json() as AdminUser[])
      setSessions(await sessionsRes.json() as AdminSession[])
      setJoinRequests(await requestsRes.json() as AdminJoinRequest[])
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

  async function handleDeleteSession(session: AdminSession) {
    if (!window.confirm(`Delete session "${session.sessionName}"? This removes all members, location data, and join requests. This cannot be undone.`)) return
    setDeletingSessionId(session.sessionId)
    try {
      const response = await fetch(`${serverUrl}/admin/sessions/${session.sessionId}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      })
      if (response.ok) {
        setSessions(prev => prev.filter(s => s.sessionId !== session.sessionId))
        if (expandedSessionId === session.sessionId) setExpandedSessionId(null)
      } else {
        setError(`Delete failed (HTTP ${response.status}).`)
      }
    } catch {
      setError('Network error.')
    } finally {
      setDeletingSessionId(null)
    }
  }

  async function handleDelete(user: AdminUser) {
    if (!window.confirm(`Delete user "${user.username}"? This removes their account, sessions, and all location data. This cannot be undone.`)) return
    setDeletingId(user.id)
    try {
      const response = await fetch(`${serverUrl}/admin/users/${user.id}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      })
      if (response.ok) {
        setUsers(prev => prev.filter(u => u.id !== user.id))
      } else {
        setError(`Delete failed (HTTP ${response.status}).`)
      }
    } catch {
      setError('Network error.')
    } finally {
      setDeletingId(null)
    }
  }

  async function loadMemberStats(sessionId: string) {
    setMemberStats(prev => ({ ...prev, [sessionId]: 'loading' }))
    try {
      const r = await fetch(`${serverUrl}/admin/sessions/${sessionId}/member-stats`, {
        headers: { Authorization: `Bearer ${token}` },
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
        headers: { Authorization: `Bearer ${token}` },
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

  async function handleDeleteJoinRequest(requestId: string) {
    if (!window.confirm('Delete this join request?')) return
    setDeletingJoinRequestId(requestId)
    try {
      const r = await fetch(`${serverUrl}/admin/join-requests/${requestId}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      })
      if (r.ok || r.status === 204) {
        setJoinRequests(prev => prev.filter(jr => jr.requestId !== requestId))
      } else {
        setError(`Delete failed (HTTP ${r.status}).`)
      }
    } catch {
      setError('Network error.')
    } finally {
      setDeletingJoinRequestId(null)
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

      <h2 style={subheading}>Users</h2>
      {users.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={th}>Username</th>
              <th style={th}>Display name</th>
              <th style={th}>Created</th>
              <th style={th}></th>
            </tr>
          </thead>
          <tbody>
            {users.map(user => (
              <tr key={user.id} style={tr}>
                <td style={td}>{user.username}</td>
                <td style={td}>{user.displayName}</td>
                <td style={td}>{timeAgo(user.createdAt)}</td>
                <td style={actionTd}>
                  <button
                    style={deleteBtn}
                    onClick={() => void handleDelete(user)}
                    disabled={deletingId === user.id}
                  >
                    {deletingId === user.id ? '…' : 'Delete'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {token && !loading && users.length === 0 && !error && (
        <p style={emptyText}>No users.</p>
      )}

      <h2 style={subheading}>Sessions</h2>
      {sessions.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={th}>Name</th>
              <th style={th}>Owner</th>
              <th style={th}>Members</th>
              <th style={th}>Created</th>
              <th style={th}></th>
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
                    <td style={td}>{session.sessionName}</td>
                    <td style={td}>{session.ownerUsername}</td>
                    <td style={td}>{session.memberCount}</td>
                    <td style={td}>{timeAgo(session.createdAt)}</td>
                    <td style={actionTd} onClick={e => e.stopPropagation()}>
                      <button
                        style={deleteBtn}
                        onClick={() => void handleDeleteSession(session)}
                        disabled={deletingSessionId === session.sessionId}
                      >
                        {deletingSessionId === session.sessionId ? '…' : 'Delete'}
                      </button>
                    </td>
                  </tr>
                  {isExpanded && (
                    <tr>
                      <td
                        colSpan={5}
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

      <h2 style={subheading}>Join Requests</h2>
      {joinRequests.length > 0 && (
        <table style={table}>
          <thead>
            <tr>
              <th style={th}>Session ID</th>
              <th style={th}>User</th>
              <th style={th}>Display name</th>
              <th style={th}>Status</th>
              <th style={th}>Requested</th>
              <th style={th}></th>
            </tr>
          </thead>
          <tbody>
            {joinRequests.map(req => (
              <tr key={req.requestId} style={tr}>
                <td style={td}><code>{req.sessionId}</code></td>
                <td style={td}>{req.username}</td>
                <td style={td}>{req.displayName}</td>
                <td style={td}>
                  <span style={statusBadge(req.status)}>{req.status}</span>
                </td>
                <td style={td}>{timeAgo(req.createdAt)}</td>
                <td style={actionTd}>
                  <button
                    style={deleteBtn}
                    onClick={() => void handleDeleteJoinRequest(req.requestId)}
                    disabled={deletingJoinRequestId === req.requestId}
                  >
                    {deletingJoinRequestId === req.requestId ? '…' : 'Delete'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {token && !loading && joinRequests.length === 0 && !error && (
        <p style={emptyText}>No join requests.</p>
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

const actionTd: React.CSSProperties = {
  ...td,
  textAlign: 'right',
  width: '1%',
  whiteSpace: 'nowrap',
}

const versionText: React.CSSProperties = {
  color: '#94a3b8',
  fontSize: '0.75rem',
  marginBottom: '1rem',
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

function statusBadge(status: string): React.CSSProperties {
  const colors: Record<string, string> = {
    pending: '#f59e0b',
    approved: '#22c55e',
    denied: '#ef4444',
  }
  return {
    display: 'inline-block',
    padding: '0.15rem 0.5rem',
    borderRadius: 999,
    fontSize: '0.75rem',
    fontWeight: 600,
    background: colors[status] ?? '#94a3b8',
    color: '#fff',
  }
}
