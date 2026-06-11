import { useState, type FormEvent } from 'react'

interface Props {
  onSubmit: (code: string) => void
}

export default function SessionPrompt({ onSubmit }: Props) {
  const [value, setValue] = useState('')

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (value.trim()) onSubmit(value)
  }

  return (
    <div style={overlay}>
      <form onSubmit={handleSubmit} style={card}>
        <h1 style={heading}>Dot Watcher</h1>
        <p style={label}>Enter a session code to start watching</p>
        <input
          style={input}
          type="text"
          placeholder="e.g. SUNSET23"
          value={value}
          onChange={e => setValue(e.target.value.toUpperCase())}
          autoFocus
          autoCapitalize="characters"
          autoCorrect="off"
          spellCheck={false}
        />
        <button style={button} type="submit" disabled={!value.trim()}>
          Watch
        </button>
      </form>
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
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
}

const heading: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
}

const label: React.CSSProperties = {
  fontSize: '0.9rem',
  color: '#555',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
}

const input: React.CSSProperties = {
  fontSize: '1.2rem',
  padding: '0.6rem 0.75rem',
  borderRadius: 8,
  border: '1.5px solid #ccc',
  textTransform: 'uppercase',
  letterSpacing: '0.1em',
  textAlign: 'center',
  fontFamily: 'monospace',
  outline: 'none',
}

const button: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.65rem',
  borderRadius: 8,
  border: 'none',
  background: '#3b82f6',
  color: '#fff',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 600,
}
