import { useEffect, useState } from 'react'
import type { SessionMember, SessionMembership } from './types.ts'
import { apiHeaders } from './apiHeaders.ts'

interface Props {
  serverUrl: string
  accessToken: string
  membership: SessionMembership
  onClose: () => void
}

export default function MemberManager({ serverUrl, accessToken, membership, onClose }: Props) {
  const [members, setMembers] = useState<SessionMember[]>([])
  const [busyUserId, setBusyUserId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function loadMembers() {
      setError(null)
      try {
        const response = await fetch(`${serverUrl}/sessions/${membership.sessionId}/members`, {
          headers: apiHeaders(accessToken),
        })
        if (cancelled) return
        if (!response.ok) {
          setError(`Load failed: HTTP ${response.status}`)
          return
        }
        setMembers(await response.json() as SessionMember[])
      } catch {
        if (!cancelled) setError('Network error.')
      }
    }

    void loadMembers()
    return () => { cancelled = true }
  }, [accessToken, membership.sessionId, serverUrl])

  async function updateRole(member: SessionMember, role: 'runner' | 'viewer') {
    setBusyUserId(member.userId)
    setError(null)
    try {
      const response = await fetch(
        `${serverUrl}/sessions/${membership.sessionId}/members/${member.userId}/role`,
        {
          method: 'POST',
          headers: { ...apiHeaders(accessToken), 'Content-Type': 'application/json' },
          body: JSON.stringify({ role }),
        },
      )

      if (!response.ok) {
        setError(`Update failed: HTTP ${response.status}`)
        return
      }

      const updated = await response.json() as SessionMember
      setMembers(existing => existing.map(item => item.userId === updated.userId ? updated : item))
    } catch {
      setError('Network error.')
    } finally {
      setBusyUserId(null)
    }
  }

  return (
    <div style={overlay}>
      <div style={dialog}>
        <div style={header}>
          <div>
            <h2 style={title}>{membership.sessionName}</h2>
            <p style={subtitle}>Invite {membership.inviteCode}</p>
          </div>
          <button type="button" style={closeButton} onClick={onClose}>Close</button>
        </div>

        <div style={memberList}>
          {members.map(member => (
            <div key={member.userId} style={memberRow}>
              <div style={memberText}>
                <span style={memberName}>{member.displayName}</span>
                <span style={roleText}>{member.role}</span>
              </div>
              {member.role !== 'owner' && (
                <button
                  type="button"
                  style={smallButton}
                  disabled={busyUserId === member.userId}
                  onClick={() => updateRole(member, member.role === 'runner' ? 'viewer' : 'runner')}
                >
                  {member.role === 'runner' ? 'Make viewer' : 'Make runner'}
                </button>
              )}
            </div>
          ))}
          {members.length === 0 && <p style={empty}>No members yet.</p>}
        </div>

        {error && <p style={errorText}>{error}</p>}
      </div>
    </div>
  )
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 12,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  background: 'rgba(0,0,0,0.45)',
}

const dialog: React.CSSProperties = {
  width: 'min(440px, 94vw)',
  maxHeight: '82vh',
  overflowY: 'auto',
  background: '#fff',
  color: '#24292f',
  borderRadius: 8,
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  padding: '1rem',
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
  fontFamily: 'system-ui, sans-serif',
}

const header: React.CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 12,
}

const title: React.CSSProperties = {
  margin: 0,
  fontSize: '1rem',
  fontFamily: 'monospace',
}

const subtitle: React.CSSProperties = {
  margin: '3px 0 0',
  color: '#57606a',
  fontSize: '0.8rem',
  fontFamily: 'monospace',
}

const closeButton: React.CSSProperties = {
  border: '1px solid #d0d7de',
  borderRadius: 4,
  background: '#f6f8fa',
  color: '#24292f',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.8rem',
  padding: '5px 8px',
}

const memberList: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
}

const memberRow: React.CSSProperties = {
  minHeight: 48,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 10,
  padding: '0.65rem 0.75rem',
  border: '1px solid #d0d7de',
  borderRadius: 6,
  background: '#f6f8fa',
}

const memberText: React.CSSProperties = {
  minWidth: 0,
}

const memberName: React.CSSProperties = {
  display: 'block',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600,
}

const roleText: React.CSSProperties = {
  display: 'block',
  color: '#57606a',
  fontSize: '0.75rem',
  textTransform: 'uppercase',
}

const smallButton: React.CSSProperties = {
  flex: '0 0 auto',
  border: 'none',
  borderRadius: 4,
  background: '#1f6feb',
  color: '#fff',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.8rem',
  padding: '6px 8px',
}

const empty: React.CSSProperties = {
  margin: 0,
  textAlign: 'center',
  color: '#57606a',
  fontSize: '0.9rem',
}

const errorText: React.CSSProperties = {
  margin: 0,
  color: '#dc2626',
  fontSize: '0.9rem',
}
