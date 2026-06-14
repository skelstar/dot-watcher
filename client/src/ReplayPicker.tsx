import { useEffect, useState } from 'react'

interface Props {
  serverUrl: string
  onSelect: (sessionCode: string) => void
}

export default function ReplayPicker({ serverUrl, onSelect }: Props) {
  const [sessions, setSessions] = useState<string[] | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    fetch(`${serverUrl}/sessions`)
      .then(r => r.json() as Promise<string[]>)
      .then(setSessions)
      .catch(() => setError(true))
  }, [serverUrl])

  return (
    <div style={overlay}>
      <div style={card}>
        <h1 style={heading}>Replay a Run</h1>

        {error && <p style={errorText}>Could not load sessions.</p>}

        {!error && sessions === null && <p style={sub}>Loading…</p>}

        {sessions !== null && sessions.length === 0 && (
          <p style={sub}>No recorded sessions found.</p>
        )}

        {sessions !== null && sessions.length > 0 && (
          <ul style={list}>
            {sessions.map(code => (
              <li key={code}>
                <button style={item} onClick={() => onSelect(code)}>
                  {code}
                </button>
              </li>
            ))}
          </ul>
        )}
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
  borderRadius: 12,
  padding: '2rem 1.5rem',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.75rem',
  width: 'min(320px, 90vw)',
  maxHeight: '80vh',
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  overflow: 'hidden',
}

const heading: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const sub: React.CSSProperties = {
  fontSize: '0.9rem',
  color: '#555',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const errorText: React.CSSProperties = {
  ...sub,
  color: '#ef4444',
}

const list: React.CSSProperties = {
  listStyle: 'none',
  margin: 0,
  padding: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
  overflowY: 'auto',
}

const item: React.CSSProperties = {
  width: '100%',
  padding: '0.6rem 0.75rem',
  borderRadius: 8,
  border: '1.5px solid #e2e8f0',
  background: '#f8fafc',
  color: '#1e293b',
  fontSize: '1rem',
  fontFamily: 'monospace',
  fontWeight: 600,
  letterSpacing: '0.05em',
  textAlign: 'left',
  cursor: 'pointer',
}
