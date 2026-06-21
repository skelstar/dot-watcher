import { useState } from 'react'

interface Props {
  displayName: string
  onClose: () => void
  onDeleteAccount: () => Promise<void>
}

export default function AccountSettings({ displayName, onClose, onDeleteAccount }: Props) {
  const [isDeleting, setIsDeleting] = useState(false)

  async function deleteAccount() {
    const confirmed = window.confirm(
      'Delete your Dot Watcher account? This removes your account, memberships, owned sessions, and stored location rows linked to your account. This cannot be undone.',
    )
    if (!confirmed) return

    setIsDeleting(true)
    try {
      await onDeleteAccount()
    } catch {
      setIsDeleting(false)
    }
  }

  return (
    <div style={overlay}>
      <div style={dialog}>
        <div style={header}>
          <div>
            <h2 style={title}>Account</h2>
            <p style={subtitle}>{displayName}</p>
          </div>
          <button type="button" style={secondaryButton} onClick={onClose}>Close</button>
        </div>

        <div style={section}>
          <a href="/privacy" style={link}>Privacy Policy</a>
          <a href="/terms" style={link}>Terms of Use</a>
        </div>

        <div style={dangerSection}>
          <h3 style={dangerTitle}>Delete Account</h3>
          <p style={bodyText}>
            Deletes your account, removes your memberships, deletes sessions you own, and removes stored location rows linked to your account. Historical rows without account attribution, admin uploads, or operational logs may need manual deletion.
          </p>
          <button type="button" style={dangerButton} onClick={deleteAccount} disabled={isDeleting}>
            {isDeleting ? 'Deleting...' : 'Delete account'}
          </button>
        </div>
      </div>
    </div>
  )
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 12,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  background: 'rgba(0,0,0,0.45)',
}

const dialog: React.CSSProperties = {
  width: 'min(440px, 94vw)',
  background: '#fff',
  color: '#24292f',
  borderRadius: 8,
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  padding: '1rem',
  display: 'flex',
  flexDirection: 'column',
  gap: 16,
  fontFamily: 'system-ui, sans-serif',
}

const header: React.CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 12,
}

const title: React.CSSProperties = {
  margin: 0,
  fontSize: '1.1rem',
}

const subtitle: React.CSSProperties = {
  margin: '3px 0 0',
  color: '#57606a',
  fontSize: '0.85rem',
}

const section: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 8,
}

const link: React.CSSProperties = {
  color: '#1f6feb',
  textDecoration: 'none',
}

const dangerSection: React.CSSProperties = {
  borderTop: '1px solid #d0d7de',
  paddingTop: 14,
}

const dangerTitle: React.CSSProperties = {
  margin: '0 0 6px',
  color: '#b42318',
  fontSize: '0.95rem',
}

const bodyText: React.CSSProperties = {
  margin: '0 0 10px',
  color: '#57606a',
  fontSize: '0.85rem',
  lineHeight: 1.4,
}

const secondaryButton: React.CSSProperties = {
  border: '1px solid #d0d7de',
  borderRadius: 4,
  background: '#f6f8fa',
  color: '#24292f',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.8rem',
  padding: '5px 8px',
}

const dangerButton: React.CSSProperties = {
  border: 'none',
  borderRadius: 4,
  background: '#b42318',
  color: '#fff',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.85rem',
  padding: '8px 10px',
}
