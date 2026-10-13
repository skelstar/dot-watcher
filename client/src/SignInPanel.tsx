import { useEffect, useState, type FormEvent } from 'react'
import { register, signIn, signOut } from './auth.ts'
import { ACCOUNT_BLUE, initialsText } from './AccountButton.tsx'
import { useAuthSession } from './useAuthSession.ts'

interface Props {
  serverUrl: string
  onClose: () => void
}

type Mode = 'signIn' | 'register'

// Opened from the small account icon in the map attribution. Never shown on its own: anonymous
// viewing is unchanged, this is only for people who choose to sign in or create an account.
export default function SignInPanel({ serverUrl, onClose }: Props) {
  const session = useAuthSession()
  const [mode, setMode] = useState<Mode>('signIn')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [initials, setInitials] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError(null)
    const result = mode === 'signIn'
      ? await signIn(serverUrl, username, password)
      : await register(serverUrl, username, password, initials)
    setBusy(false)
    if (result.ok) {
      setPassword('')
      onClose()
    } else {
      setError(result.error)
    }
  }

  function switchMode(next: Mode) {
    setMode(next)
    setError(null)
  }

  const isRegister = mode === 'register'
  const canSubmit = username.trim() !== '' && password !== '' && (!isRegister || initials.trim().length === 2)

  return (
    <div style={overlay} onClick={onClose}>
      <div style={card} role="dialog" aria-modal="true" aria-label="Account" onClick={event => event.stopPropagation()}>
        {session ? (
          <>
            <h1 style={heading}>Signed in</h1>
            <p style={bodyText}>
              {session.user.displayName} <span style={muted}>({session.user.username})</span>
            </p>
            <button style={secondaryButton} type="button" onClick={() => { void signOut(serverUrl); onClose() }}>
              Sign out
            </button>
            <button style={linkButton} type="button" onClick={onClose}>Close</button>
          </>
        ) : (
          <form style={form} onSubmit={event => { void handleSubmit(event) }}>
            <h1 style={heading}>{isRegister ? 'Create account' : 'Sign in'}</h1>
            {isRegister && (
              <p style={bodyText}>An account lets you follow runners from any device.</p>
            )}

            <label style={fieldLabel} htmlFor="dw-account-username">Username</label>
            <input
              id="dw-account-username"
              style={input}
              value={username}
              onChange={event => setUsername(event.target.value)}
              autoComplete="username"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              autoFocus
            />

            <label style={fieldLabel} htmlFor="dw-account-password">Password</label>
            <input
              id="dw-account-password"
              style={input}
              type="password"
              value={password}
              onChange={event => setPassword(event.target.value)}
              autoComplete={isRegister ? 'new-password' : 'current-password'}
            />
            {isRegister && <span style={hint}>At least 8 characters.</span>}

            {isRegister && (
              <>
                <label style={fieldLabel} htmlFor="dw-account-initials">Your initials</label>
                <div style={initialsRow}>
                  <input
                    id="dw-account-initials"
                    style={{ ...input, ...initialsInput }}
                    value={initials}
                    onChange={event => setInitials(event.target.value.slice(0, 2))}
                    maxLength={2}
                    autoComplete="off"
                    autoCapitalize="characters"
                    autoCorrect="off"
                    spellCheck={false}
                  />
                  {/* Preview of the signed-in account button: "??" until each initial is typed. */}
                  <span style={initialsPreview} aria-hidden="true">
                    <span style={initialsText}>{previewInitials(initials)}</span>
                  </span>
                </div>
                <span style={hint}>Two letters, shown on your account button.</span>
              </>
            )}

            {error && <p style={errorText} role="alert">{error}</p>}

            <button
              style={canSubmit && !busy ? primaryButton : { ...primaryButton, ...primaryButtonDisabled }}
              type="submit"
              disabled={!canSubmit || busy}
            >
              {busy ? 'Please wait…' : isRegister ? 'Create account' : 'Sign in'}
            </button>
            <button style={linkButton} type="button" onClick={() => switchMode(isRegister ? 'signIn' : 'register')}>
              {isRegister ? 'I already have an account' : 'Create an account'}
            </button>
            <button style={linkButton} type="button" onClick={onClose}>Cancel</button>
          </form>
        )}
      </div>
    </div>
  )
}

// "ST" typed -> "ST"; "S" -> "S?"; nothing -> "??".
function previewInitials(value: string): string {
  const typed = value.trim().toUpperCase()
  return [typed[0] ?? '?', typed[1] ?? '?'].join('')
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'rgba(0,0,0,0.45)',
  zIndex: 11,
  padding: 16,
}

const card: React.CSSProperties = {
  background: '#fff',
  borderRadius: 8,
  padding: '1.25rem',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.65rem',
  width: 'min(340px, 94vw)',
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  fontFamily: 'system-ui, sans-serif',
}

const form: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '0.5rem',
}

const heading: React.CSSProperties = {
  fontSize: '1.15rem',
  fontWeight: 700,
  textAlign: 'center',
  margin: 0,
}

const bodyText: React.CSSProperties = {
  margin: 0,
  fontSize: '0.9rem',
  color: '#57606a',
  textAlign: 'center',
}

const muted: React.CSSProperties = {
  color: '#8b949e',
}

const fieldLabel: React.CSSProperties = {
  fontSize: '0.8rem',
  fontWeight: 600,
  color: '#57606a',
}

const input: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.6rem',
  borderRadius: 6,
  border: '1px solid #d0d7de',
  outline: 'none',
}

const initialsRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 10,
}

const initialsInput: React.CSSProperties = {
  flex: 1,
  minWidth: 0,
  textTransform: 'uppercase',
}

// Same size, colour and corners as the signed-in AccountButton in the map's button column.
const initialsPreview: React.CSSProperties = {
  flex: 'none',
  width: 29,
  height: 29,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: ACCOUNT_BLUE,
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
}

const hint: React.CSSProperties = {
  fontSize: '0.75rem',
  color: '#8b949e',
}

const errorText: React.CSSProperties = {
  margin: 0,
  fontSize: '0.85rem',
  color: '#dc2626',
}

const primaryButton: React.CSSProperties = {
  fontSize: '1rem',
  padding: '0.65rem',
  marginTop: '0.25rem',
  borderRadius: 6,
  border: 'none',
  background: '#1f6feb',
  color: '#fff',
  cursor: 'pointer',
  fontWeight: 600,
}

// Inline styles can't use :disabled, so the form picks this when the submit button is not usable.
const primaryButtonDisabled: React.CSSProperties = {
  background: '#d0d7de',
  color: '#8b949e',
  cursor: 'not-allowed',
}

const secondaryButton: React.CSSProperties = {
  ...primaryButton,
  background: '#eaeef2',
  color: '#24292f',
}

const linkButton: React.CSSProperties = {
  fontSize: '0.85rem',
  padding: '0.25rem',
  border: 'none',
  background: 'none',
  color: '#57606a',
  cursor: 'pointer',
  textDecoration: 'underline',
}
