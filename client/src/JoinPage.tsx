import type { CSSProperties } from 'react'
import { APP_STORE_URL } from './appStore.ts'
import { page, card, icon, headline, body, codeStyle, storeLink, badge } from './cardPageStyles.ts'

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

const watchText: CSSProperties = {
  margin: '24px 0 0',
  fontSize: 14,
  color: '#5B5D68',
}

const watchLink: CSSProperties = {
  color: '#2f80f5',
  fontWeight: 600,
}
