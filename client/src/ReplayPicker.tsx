import { useState } from 'react'

interface Props {
  onSelect: (sessionCode: string) => void
}

export default function ReplayPicker({ onSelect }: Props) {
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
        <form style={form} onSubmit={submit}>
          <input
            value={code}
            onChange={event => setCode(event.target.value)}
            placeholder="Session code"
            autoCapitalize="characters"
            style={input}
          />
          <button type="submit" style={item}>Open Replay</button>
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

const form: React.CSSProperties = {
  margin: 0,
  padding: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
}

const input: React.CSSProperties = {
  width: '100%',
  padding: '0.7rem 0.8rem',
  border: '1px solid #d0d7de',
  borderRadius: 8,
  fontFamily: 'system-ui, sans-serif',
  fontSize: '1rem',
  textTransform: 'uppercase',
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
