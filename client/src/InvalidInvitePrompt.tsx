import { useState, type FormEvent } from 'react'

interface Props {
  message: string
  isReplay: boolean
}

export default function InvalidInvitePrompt({ message, isReplay }: Props) {
  const [code, setCode] = useState('')

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const cleaned = code.trim().toUpperCase()
    if (!cleaned) return
    window.location.href = isReplay ? `/code/${cleaned}/replay` : `/code/${cleaned}`
  }

  return (
    <div style={overlay}>
      <div style={card}>
        <h1 style={heading}>Dot Watcher</h1>
        <p style={errorText}>{message}</p>
        <form style={form} onSubmit={handleSubmit}>
          <label style={fieldLabel} htmlFor="fix-invite-code-input">Enter invite code</label>
          <input
            id="fix-invite-code-input"
            style={input}
            value={code}
            onChange={event => setCode(event.target.value.toUpperCase())}
            placeholder="ABCD12"
            autoCapitalize="characters"
            autoCorrect="off"
            spellCheck={false}
            autoFocus
          />
          <button style={button} type="submit" disabled={!code.trim()}>
            Go
          </button>
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
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
}

const heading: React.CSSProperties = {
  fontSize: '1.25rem',
  fontWeight: 700,
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
  margin: 0,
}

const errorText: React.CSSProperties = {
  margin: 0,
  fontSize: '0.9rem',
  color: '#dc2626',
  textAlign: 'center',
  fontFamily: 'system-ui, sans-serif',
}

const form: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '0.65rem',
}

const fieldLabel: React.CSSProperties = {
  fontSize: '0.8rem',
  fontWeight: 600,
  color: '#57606a',
  fontFamily: 'system-ui, sans-serif',
}

const input: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  letterSpacing: '0.08em',
  textAlign: 'center',
  padding: '0.75rem',
  borderRadius: 6,
  border: '2px solid #1f6feb',
  fontFamily: 'monospace',
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
