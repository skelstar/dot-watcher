import { useState, type FormEvent } from 'react'
import { apiHeaders } from './apiHeaders.ts'

const contactEmail = 'dotwatchr@skelstar.io'

type Step =
  | { kind: 'form' }
  | { kind: 'confirm'; token: string; displayName: string }
  | { kind: 'deleting' }
  | { kind: 'done' }
  | { kind: 'error'; message: string }

/**
 * Public, unauthenticated-entry self-service account deletion — the page Google Play's Data
 * Safety section requires a link to (store listing "Delete account URL"). Signs the user in with
 * their own credentials first (there's no other session on this page to reuse), then calls
 * `DELETE /me` (server/Controllers/AuthController.cs's DeleteAccount, which already cascades to
 * owned sessions and memberships — see client/src/LegalPage.tsx's privacy policy, "Retention and
 * Deletion"). Mirrors ios/DotWatcher/DotWatcher/AuthSheet.swift's delete-account flow, which until
 * now was the only client exposing this.
 */
export default function DeleteAccountPage({ serverUrl }: { serverUrl: string }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [step, setStep] = useState<Step>({ kind: 'form' })

  async function handleSignIn(event: FormEvent) {
    event.preventDefault()
    setStep({ kind: 'deleting' })
    try {
      const response = await fetch(`${serverUrl}/auth/login`, {
        method: 'POST',
        headers: { ...apiHeaders(null, 'web-delete-account'), 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      })
      if (!response.ok) {
        setStep({ kind: 'error', message: 'Username or password is incorrect.' })
        return
      }
      const auth = await response.json() as { accessToken: string; user: { displayName: string } }
      setStep({ kind: 'confirm', token: auth.accessToken, displayName: auth.user.displayName })
    } catch {
      setStep({ kind: 'error', message: 'Network error. Please try again.' })
    }
  }

  async function handleConfirmDelete(token: string) {
    setStep({ kind: 'deleting' })
    try {
      const response = await fetch(`${serverUrl}/me`, {
        method: 'DELETE',
        headers: apiHeaders(token, 'web-delete-account'),
      })
      if (!response.ok) {
        setStep({ kind: 'error', message: 'Could not delete the account. Please try again.' })
        return
      }
      setStep({ kind: 'done' })
    } catch {
      setStep({ kind: 'error', message: 'Network error. Please try again.' })
    }
  }

  return (
    <main style={pageShell}>
      <nav style={topNav}>
        <a href="/" style={brandLink}>Dot Watcher</a>
        <span style={navLinks}>
          <a href="/privacy" style={navLink}>Privacy</a>
          <a href="/terms" style={navLink}>Terms</a>
        </span>
      </nav>
      <article style={content}>
        <h1 style={title}>Delete your account</h1>

        {step.kind === 'form' || step.kind === 'error' ? (
          <>
            <p style={paragraphStyle}>
              Sign in below to permanently delete your Dot Watcher account. This removes your account,
              any sessions you own, your memberships in other sessions, and your stored location data.
              This cannot be undone.
            </p>
            <form onSubmit={handleSignIn} style={formStyle}>
              <label style={labelStyle}>
                Username
                <input
                  style={inputStyle}
                  value={username}
                  onChange={e => setUsername(e.target.value)}
                  autoComplete="username"
                  required
                />
              </label>
              <label style={labelStyle}>
                Password
                <input
                  style={inputStyle}
                  type="password"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  autoComplete="current-password"
                  required
                />
              </label>
              <button type="submit" style={dangerButton}>Sign in to continue</button>
            </form>
            {step.kind === 'error' && <p style={errorStyle}>{step.message}</p>}
            <p style={paragraphStyle}>
              Prefer not to sign in here? Email {contactEmail} from the address or with the details on
              your account and we'll process the deletion for you.
            </p>
          </>
        ) : null}

        {step.kind === 'confirm' && (
          <>
            <p style={paragraphStyle}>
              Signed in as <strong>{step.displayName}</strong>. This is the last step — deleting your
              account is permanent and cannot be undone.
            </p>
            <button type="button" style={dangerButton} onClick={() => handleConfirmDelete(step.token)}>
              Permanently delete my account
            </button>
          </>
        )}

        {step.kind === 'deleting' && <p style={paragraphStyle}>Working…</p>}

        {step.kind === 'done' && (
          <p style={paragraphStyle}>
            Your account has been deleted. You can close this page.
          </p>
        )}
      </article>
    </main>
  )
}

const pageShell: React.CSSProperties = {
  minHeight: '100%',
  overflowY: 'auto',
  background: '#f6f8fa',
  color: '#24292f',
  fontFamily: 'system-ui, sans-serif',
}

const topNav: React.CSSProperties = {
  position: 'sticky',
  top: 0,
  zIndex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 16,
  padding: '12px 18px',
  background: '#fff',
  boxShadow: '0 1px 0 rgba(0,0,0,0.08)',
}

const brandLink: React.CSSProperties = {
  color: '#24292f',
  textDecoration: 'none',
  fontWeight: 700,
}

const navLinks: React.CSSProperties = {
  display: 'flex',
  gap: 12,
}

const navLink: React.CSSProperties = {
  color: '#1f6feb',
  textDecoration: 'none',
  fontSize: '0.9rem',
}

const content: React.CSSProperties = {
  width: 'min(480px, 100%)',
  margin: '0 auto',
  padding: '32px 18px 56px',
}

const title: React.CSSProperties = {
  margin: '0 0 16px',
  fontSize: '2rem',
}

const paragraphStyle: React.CSSProperties = {
  margin: '0 0 16px',
  lineHeight: 1.55,
}

const formStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
  marginBottom: 16,
}

const labelStyle: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 4,
  fontSize: '0.9rem',
}

const inputStyle: React.CSSProperties = {
  padding: '10px 12px',
  fontSize: '1rem',
  border: '1px solid #d0d7de',
  borderRadius: 6,
}

const dangerButton: React.CSSProperties = {
  padding: '10px 16px',
  fontSize: '1rem',
  fontWeight: 600,
  color: '#fff',
  background: '#cf222e',
  border: 'none',
  borderRadius: 6,
  cursor: 'pointer',
}

const errorStyle: React.CSSProperties = {
  margin: '0 0 16px',
  color: '#cf222e',
}
