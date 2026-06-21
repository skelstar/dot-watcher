import { useEffect, useState, type FormEvent } from 'react'

interface AdminUser {
  id: string
  username: string
  displayName: string
  createdAt: string
}

interface AdminSession {
  sessionCode: string
  ownerUsername: string
  memberCount: number
  createdAt: string
}

const BEARER_TOKEN_KEY = 'adminBearerToken'

export default function AdminPanel() {
  const [token, setToken] = useState(() => sessionStorage.getItem(BEARER_TOKEN_KEY) ?? (import.meta.env.VITE_BEARER_TOKEN as string | undefined) ?? '')
  const [tokenInput, setTokenInput] = useState(token)
  const [users, setUsers] = useState<AdminUser[]>([])
  const [sessions, setSessions] = useState<AdminSession[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  async function loadAll(bearerToken: string) {
    setLoading(true)
    setError(null)
    try {
      const [usersRes, sessionsRes] = await Promise.all([
        fetch('/admin/users', { headers: { Authorization: `Bearer ${bearerToken}` } }),
        fetch('/admin/sessions', { headers: { Authorization: `Bearer ${bearerToken}` } }),
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

  async function handleDelete(user: AdminUser) {
    if (!window.confirm(`Delete user "${user.username}"? This cannot be undone.`)) return
    setDeletingId(user.id)
    try {
      const response = await fetch(`/admin/users/${user.id}`, {
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

  return (
    <div style={page}>
      <h1 style={heading}>Admin — Users</h1>

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
                <td style={td}>{new Date(user.createdAt).toLocaleString()}</td>
                <td style={td}>
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
              <th style={th}>Code</th>
              <th style={th}>Owner</th>
              <th style={th}>Members</th>
              <th style={th}>Created</th>
            </tr>
          </thead>
          <tbody>
            {sessions.map(session => (
              <tr key={session.sessionCode} style={tr}>
                <td style={td}>{session.sessionCode}</td>
                <td style={td}>{session.ownerUsername}</td>
                <td style={td}>{session.memberCount}</td>
                <td style={td}>{new Date(session.createdAt).toLocaleString()}</td>
              </tr>
            ))}
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
