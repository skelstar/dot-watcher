import { useState } from 'react'
import type { SessionMembership } from './types.ts'

interface Props {
  memberships: SessionMembership[]
  onSelect: (sessionCode: string) => void
}

export default function ReplayPicker({ memberships, onSelect }: Props) {
  const [code, setCode] = useState('')

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const upper = code.trim().toUpperCase()
    if (upper) onSelect(upper)
  }

  return (
    <div style={overlay}>
      <div style={card}>
        <h1 style={heading}>Replay a Run</h1>
        <div style={list}>
          {memberships.length === 0 ? (
            <p style={empty}>No sessions yet.</p>
          ) : memberships.map(membership => (
            <button
              key={membership.sessionCode}
              type="button"
              style={item}
              onClick={() => onSelect(membership.sessionCode)}
            >
              <span>{membership.sessionCode}</span>
              <small style={role}>{membership.role}</small>
            </button>
          ))}
        </div>
        <form style={form} onSubmit={submit}>
          <input
            value={code}
            onChange={event => setCode(event.target.value)}
            placeholder="Session code"
            autoCapitalize="characters"
            style={input}
          />
          <button type="submit" style={openButton}>Open Replay</button>
        </form>
      </div>
    </div>
  )
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'rgba(0,0,0,0.45)',
  zIndex: 10,
}

const card: React.CSSProperties = {
  background: '#fff',
  borderRadius: 8,
  padding: '1.25rem',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.75rem',
  width: 'min(360px, 94vw)',
  maxHeight: '80vh',
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  overflow: 'hidden',
}

const heading: React.CSSProperties = {
  fontSize: '1.25rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const list: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
  overflowY: 'auto',
}

const form: React.CSSProperties = {
  margin: 0,
  padding: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
}

const input: React.CSSProperties = {
  width: '100%',
  padding: '0.65rem 0.75rem',
  border: '1px solid #d0d7de',
  borderRadius: 6,
  fontFamily: 'system-ui, sans-serif',
  fontSize: '1rem',
  textTransform: 'uppercase',
}

const item: React.CSSProperties = {
  width: '100%',
  padding: '0.65rem 0.75rem',
  borderRadius: 6,
  border: '1px solid #d0d7de',
  background: '#f6f8fa',
  color: '#24292f',
  fontSize: '1rem',
  fontFamily: 'monospace',
  fontWeight: 700,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  cursor: 'pointer',
}

const role: React.CSSProperties = {
  color: '#57606a',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.7rem',
  fontWeight: 700,
  textTransform: 'uppercase',
}

const openButton: React.CSSProperties = {
  width: '100%',
  padding: '0.65rem 0.75rem',
  borderRadius: 6,
  border: 'none',
  background: '#1f6feb',
  color: '#fff',
  fontSize: '1rem',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 600,
  cursor: 'pointer',
}

const empty: React.CSSProperties = {
  margin: 0,
  color: '#57606a',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}
