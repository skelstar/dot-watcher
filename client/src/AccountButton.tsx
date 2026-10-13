interface Props {
  signedIn: boolean
  onClick: () => void
}

// Optional viewer sign-in entry: a head-in-a-circle button at the top of the right-hand button
// column, above MapLibre's compass. Outline when browsing anonymously, filled when signed in.
export default function AccountButton({ signedIn, onClick }: Props) {
  const label = signedIn ? 'Account (signed in)' : 'Sign in or create an account'

  return (
    <button type="button" onClick={onClick} style={button} title={label} aria-label={label}>
      <AccountIcon filled={signedIn} />
    </button>
  )
}

function AccountIcon({ filled }: { filled: boolean }) {
  const fill = filled ? '#333' : 'none'
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="2" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <circle cx="12" cy="9.5" r="3" fill={fill} />
      <path d="M5.8 18.6c1-3 3.3-4.6 6.2-4.6s5.2 1.6 6.2 4.6z" fill={fill} />
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
