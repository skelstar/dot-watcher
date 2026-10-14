import { initialsFor } from './components/InitialsBadge.tsx'

// Signed-in button colour; SignInPanel's initials preview reuses it so the two match.
export const ACCOUNT_BLUE = '#1f6feb'

interface Props {
  // Signed-in user's display name (the 2-letter initials the account was created with), or null when
  // browsing anonymously.
  displayName: string | null
  onClick: () => void
}

// Optional viewer sign-in entry at the top of the right-hand button column, above MapLibre's compass.
// A plain person outline when anonymous. Signed in, the whole button turns blue and shows the user's
// initials, the same "on" treatment as Terrain3DToggle, so it can't be mistaken for a runner dot.
export default function AccountButton({ displayName, onClick }: Props) {
  const initials = displayName === null ? '' : initialsFor(displayName)
  const signedIn = initials !== ''
  const label = signedIn ? `Account (signed in as ${displayName})` : 'Sign in or create an account'

  return (
    <button
      type="button"
      onClick={onClick}
      style={{ ...button, background: signedIn ? ACCOUNT_BLUE : '#fff' }}
      title={label}
      aria-label={label}
    >
      {signedIn ? <span style={initialsText}>{initials}</span> : <PersonIcon />}
    </button>
  )
}

function PersonIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="8" r="4" />
      <path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7" />
    </svg>
  )
}

const button: React.CSSProperties = {
  position: 'absolute',
  top: 10,
  right: 10,
  width: 29,
  height: 29,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  padding: 0,
  zIndex: 1,
  pointerEvents: 'auto',
}

export const initialsText: React.CSSProperties = {
  color: '#fff',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 700,
  fontSize: 12,
  lineHeight: 1,
  letterSpacing: 0.3,
}
