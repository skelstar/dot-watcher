import { useState, type FormEvent } from 'react'
import type { AuthResponse } from './types.ts'

interface Props {
  serverUrl: string
  onAuth: (auth: AuthResponse) => void
}

export default function AuthPrompt({ serverUrl, onAuth }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)

    const body = mode === 'login'
      ? { username, password }
      : { username, password, displayName }

    try {
      const response = await fetch(`${serverUrl}/auth/${mode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })

      if (!response.ok) {
        if (response.status === 429) {
          const retryAfter = response.headers.get('Retry-After')
          setError(retryAfter ? `Too many attempts. Try again in ${retryAfter} seconds.` : 'Too many attempts. Try again later.')
          return
        }

        setError(`Sign ${mode === 'login' ? 'in' : 'up'} failed.`)
        return
      }

      const result = await response.json() as AuthResponse
      onAuth(result)
    } catch {
      setError('Network error.')
    }
  }

  return (
    <div style={overlay}>
      <form onSubmit={submit} style={card}>
        <h1 style={heading}>Dot Watcher</h1>
        {mode === 'register' && (
          <input
            style={input}
            value={displayName}
            onChange={event => setDisplayName(event.target.value)}
            placeholder="Display name"
            autoComplete="name"
          />
        )}
        <input
          style={input}
          value={username}
          onChange={event => setUsername(event.target.value)}
          placeholder="Username"
          autoComplete="username"
        />
        <input
          style={input}
          value={password}
          onChange={event => setPassword(event.target.value)}
          placeholder="Password"
          type="password"
          autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
        />
        {error && <p style={errorText}>{error}</p>}
        <button style={button} type="submit">
          {mode === 'login' ? 'Sign In' : 'Create Account'}
        </button>
        <button
          style={switchButton}
          type="button"
          onClick={() => setMode(mode === 'login' ? 'register' : 'login')}
        >
          {mode === 'login' ? 'Create account' : 'Use existing account'}
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
  zIndex: 20,
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
  margin: 0,
}

const input: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.6rem 0.75rem',
  borderRadius: 8,
  border: '1.5px solid #ccc',
  fontFamily: 'system-ui, sans-serif',
  outline: 'none',
}

const errorText: React.CSSProperties = {
  margin: 0,
  color: '#dc2626',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
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

const switchButton: React.CSSProperties = {
  ...button,
  background: '#f8fafc',
  color: '#1e293b',
  border: '1px solid #e2e8f0',
}
