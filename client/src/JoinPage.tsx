import { APP_STORE_URL } from './appStore.ts'

interface Props {
  code: string
}

// Shown at /join/{code} when the iOS app didn't claim the link (app not installed, or the link was
// opened on a desktop/Android device). iOS devices with the app never see this page.
export default function JoinPage({ code }: Props) {
  return (
    <main style={page}>
      <div style={card}>
        <img src="/landing/assets/app-icon.png" alt="" style={icon} />
        <h1 style={headline}>You're invited to a session</h1>
        <p style={body}>Open the Dot Watcher app and enter this invite code:</p>
        <p style={codeStyle} aria-label={`Invite code ${code.split('').join(' ')}`}>{code}</p>
        <a href={APP_STORE_URL} target="_blank" rel="noopener" style={storeLink}>
          <img src="/landing/assets/app-store-badge.svg" alt="Download on the App Store" style={badge} />
        </a>
        <p style={watchText}>
          Just want to watch? <a href={`/code/${code}`} style={watchLink}>Watch live on the web</a>
        </p>
      </div>
    </main>
  )
}

const page: React.CSSProperties = {
  minHeight: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  boxSizing: 'border-box',
  background: '#F0F1F5',
  fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Text", system-ui, sans-serif',
}

const card: React.CSSProperties = {
  width: '100%',
  maxWidth: 420,
  boxSizing: 'border-box',
  padding: '32px 24px',
  background: '#fff',
  borderRadius: 20,
  boxShadow: '0 2px 12px rgba(20,21,26,.08)',
  textAlign: 'center',
}

const icon: React.CSSProperties = {
  width: 64,
  height: 64,
  borderRadius: 16,
  boxShadow: '0 1px 3px rgba(20,21,26,.2)',
}

const headline: React.CSSProperties = {
  margin: '16px 0 8px',
  fontSize: 26,
  lineHeight: 1.15,
  fontWeight: 800,
  letterSpacing: '-0.5px',
  color: '#14151A',
}

const body: React.CSSProperties = {
  margin: '0 0 16px',
  fontSize: 16,
  lineHeight: 1.5,
  color: '#5B5D68',
}

const codeStyle: React.CSSProperties = {
  margin: '0 0 28px',
  padding: '14px 0',
  background: '#F0F1F5',
  borderRadius: 12,
  fontSize: 34,
  fontWeight: 800,
  letterSpacing: '8px',
  paddingLeft: 8,
  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  color: '#14151A',
  userSelect: 'all',
}

const storeLink: React.CSSProperties = {
  display: 'inline-block',
  lineHeight: 0,
}

const badge: React.CSSProperties = {
  height: 48,
  display: 'block',
}

const watchText: React.CSSProperties = {
  margin: '24px 0 0',
  fontSize: 14,
  color: '#5B5D68',
}

const watchLink: React.CSSProperties = {
  color: '#2f80f5',
  fontWeight: 600,
}
