import { useEffect, useMemo, useState, type FormEvent } from 'react'
import type { SessionMembership } from './types.ts'

interface Props {
  serverUrl: string
  accessToken: string
  memberships: SessionMembership[]
  requestedSessionName?: string | null
  initialInviteCode?: string | null
  autoJoinDisplayName?: string
  isReplay?: boolean
  onSelect: (membership: SessionMembership) => void
  onMembershipsChanged: (memberships: SessionMembership[]) => void
}

type Mode = 'sessions' | 'create' | 'join'

export default function SessionPrompt({
  serverUrl,
  accessToken,
  memberships,
  requestedSessionName,
  initialInviteCode,
  autoJoinDisplayName,
  isReplay = false,
  onSelect,
  onMembershipsChanged,
}: Props) {
  const [mode, setMode] = useState<Mode>(initialInviteCode ? 'join' : memberships.length ? 'sessions' : 'create')
  const [createCode, setCreateCode] = useState(requestedSessionName ?? '')
  const [createName, setCreateName] = useState('')
  const [inviteCode, setInviteCode] = useState(initialInviteCode ?? '')
  const [joinName, setJoinName] = useState(autoJoinDisplayName ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (initialInviteCode) {
      setInviteCode(initialInviteCode)
      setMode('join')
    }
  }, [initialInviteCode])

  useEffect(() => {
    if (autoJoinDisplayName === undefined || !initialInviteCode) return
    void joinSession()
  }, [])

  const sortedMemberships = useMemo(
    () => [...memberships].sort((a, b) => a.sessionName.localeCompare(b.sessionName)),
    [memberships],
  )

  async function refreshMemberships() {
    const response = await fetch(`${serverUrl}/me/sessions`, {
      headers: { 'Authorization': `Bearer ${accessToken}` },
    })
    if (!response.ok) return
    onMembershipsChanged(await response.json() as SessionMembership[])
  }

  async function createSession(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    const body = {
      sessionName: cleanInput(createCode) || null,
      displayName: createName.trim() || null,
    }

    try {
      const response = await fetch(`${serverUrl}/sessions`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${accessToken}`,
        },
        body: JSON.stringify(body),
      })

      if (!response.ok) {
        setError(`Create failed: HTTP ${response.status}`)
        return
      }

      const membership = await response.json() as SessionMembership
      onMembershipsChanged(upsertMembership(memberships, membership))
      onSelect(membership)
    } catch {
      setError('Network error.')
    } finally {
      setBusy(false)
    }
  }

  async function joinSession(event?: FormEvent) {
    event?.preventDefault()
    const code = cleanInput(inviteCode)
    if (!code) return

    setBusy(true)
    setError(null)
    try {
      const response = await fetch(`${serverUrl}/session-invites/${code}/join`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${accessToken}`,
        },
        body: JSON.stringify({ displayName: joinName.trim() || null }),
      })

      if (!response.ok) {
        setError(response.status === 404 ? 'Invite not found.' : `Join failed: HTTP ${response.status}`)
        return
      }

      const membership = await response.json() as SessionMembership
      onMembershipsChanged(upsertMembership(memberships, membership))
      onSelect(membership)
    } catch {
      setError('Network error.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div style={overlay}>
      <div style={card}>
        <h1 style={heading}>{isReplay ? 'Replay Session' : 'Dot Watcher'}</h1>
        {requestedSessionName && !memberships.some(m => m.sessionName === requestedSessionName) && (
          <p style={notice}>No membership for {requestedSessionName}.</p>
        )}

        <div style={tabs}>
          <button type="button" style={tabStyle(mode === 'sessions')} onClick={() => { setMode('sessions'); void refreshMemberships() }}>Sessions</button>
          <button type="button" style={tabStyle(mode === 'create')} onClick={() => setMode('create')}>Create</button>
          <button type="button" style={tabStyle(mode === 'join')} onClick={() => setMode('join')}>Join</button>
        </div>

        {mode === 'sessions' && (
          <div style={sessionList}>
            {sortedMemberships.length === 0 ? (
              <p style={empty}>No sessions yet.</p>
            ) : sortedMemberships.map(membership => (
              <button key={membership.sessionId} type="button" style={sessionButton} onClick={() => onSelect(membership)}>
                <span>
                  <span style={sessionNameStyle}>{membership.sessionName}</span>
                  {membership.role === 'owner' && <span style={inviteCodeText}>Invite {membership.inviteCode}</span>}
                </span>
                <span style={role}>{membership.role}</span>
              </button>
            ))}
          </div>
        )}

        {mode === 'create' && (
          <form style={form} onSubmit={createSession}>
            <input
              style={input}
              value={createCode}
              onChange={event => setCreateCode(event.target.value.toUpperCase())}
              placeholder="Session name"
              autoCapitalize="characters"
              autoCorrect="off"
              spellCheck={false}
            />
            <input
              style={input}
              value={createName}
              onChange={event => setCreateName(event.target.value)}
              placeholder="Display name"
              autoComplete="name"
            />
            <button style={button} type="submit" disabled={busy}>
              Create
            </button>
          </form>
        )}

        {mode === 'join' && (
          <form style={form} onSubmit={joinSession}>
            <input
              style={input}
              value={inviteCode}
              onChange={event => setInviteCode(event.target.value.toUpperCase())}
              placeholder="Invite code"
              autoCapitalize="characters"
              autoCorrect="off"
              spellCheck={false}
            />
            <input
              style={input}
              value={joinName}
              onChange={event => setJoinName(event.target.value)}
              placeholder="Display name"
              autoComplete="name"
            />
            <button style={button} type="submit" disabled={busy || !cleanInput(inviteCode)}>
              Join
            </button>
          </form>
        )}

        {error && <p style={errorText}>{error}</p>}
      </div>
    </div>
  )
}

function cleanInput(value: string): string {
  return value.trim().toUpperCase()
}

function upsertMembership(memberships: SessionMembership[], membership: SessionMembership): SessionMembership[] {
  return [
    membership,
    ...memberships.filter(existing => existing.sessionId !== membership.sessionId),
  ]
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'rgba(0,0,0,0.45)',
  zIndex: 10,
  padding: 16,
}

const card: React.CSSProperties = {
  background: '#fff',
  borderRadius: 8,
  padding: '1.25rem',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.8rem',
  width: 'min(380px, 94vw)',
  maxHeight: '82vh',
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
}

const heading: React.CSSProperties = {
  fontSize: '1.25rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const notice: React.CSSProperties = {
  margin: 0,
  fontSize: '0.85rem',
  color: '#92400e',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
}

const tabs: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(3, 1fr)',
  border: '1px solid #d0d7de',
  borderRadius: 6,
  overflow: 'hidden',
}

function tabStyle(active: boolean): React.CSSProperties {
  return {
    border: 0,
    borderRight: '1px solid #d0d7de',
    background: active ? '#1f6feb' : '#fff',
    color: active ? '#fff' : '#24292f',
    padding: '0.55rem 0.4rem',
    cursor: 'pointer',
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 600,
  }
}

const form: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '0.65rem',
}

const input: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.65rem 0.75rem',
  borderRadius: 6,
  border: '1.5px solid #d0d7de',
  fontFamily: 'system-ui, sans-serif',
  outline: 'none',
}

const button: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.65rem',
  borderRadius: 6,
  border: 'none',
  background: '#1f6feb',
  color: '#fff',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 600,
}

const sessionList: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
  overflowY: 'auto',
}

const sessionButton: React.CSSProperties = {
  width: '100%',
  padding: '0.7rem 0.75rem',
  borderRadius: 6,
  border: '1px solid #d0d7de',
  background: '#f6f8fa',
  color: '#24292f',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  cursor: 'pointer',
}

const sessionNameStyle: React.CSSProperties = {
  display: 'block',
  fontFamily: 'monospace',
  fontWeight: 700,
}

const inviteCodeText: React.CSSProperties = {
  display: 'block',
  marginTop: 3,
  fontFamily: 'monospace',
  fontSize: '0.75rem',
  color: '#57606a',
}

const role: React.CSSProperties = {
  fontSize: '0.75rem',
  fontFamily: 'system-ui, sans-serif',
  color: '#57606a',
  textTransform: 'uppercase',
}

const empty: React.CSSProperties = {
  margin: 0,
  color: '#57606a',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}

const errorText: React.CSSProperties = {
  margin: 0,
  color: '#dc2626',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}
