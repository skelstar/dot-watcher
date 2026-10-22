import { useState, type CSSProperties } from 'react'
import { APP_STORE_URL } from './appStore.ts'
import { page, card, icon, headline, body, codeStyle, storeLink, badge } from './cardPageStyles.ts'

interface Props {
  code: string
}

// The characters a crew code can contain (server: SessionStore.CrewCodeAlphabet — no 0/O/1/I/L).
const CREW_CODE = /^[A-HJKMNP-Z2-9]{8}$/

// Shown at /crew/{code} when the app didn't claim the link (app not installed, an Android phone, or
// a desktop browser). A phone with the app installed opens the app instead and never sees this page.
// The page only displays the code: adding someone happens in the app, after a confirmation, and
// nothing here talks to the server.
export default function CrewLinkPage({ code }: Props) {
  const [copied, setCopied] = useState(false)
  const valid = CREW_CODE.test(code)

  async function copy() {
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
    } catch {
      // Clipboard can be unavailable (insecure context, denied); the code is selectable on tap anyway.
    }
  }

  return (
    <main style={page}>
      <div style={card}>
        <img src="/landing/assets/app-icon.png" alt="" style={icon} />
        {valid ? (
          <>
            <h1 style={headline}>Join a crew on Dot Watcher</h1>
            <p style={body}>
              Open the Dot Watcher app, choose <strong>Your crew</strong> then <strong>Add someone</strong>, and
              enter this code:
            </p>
            <p style={{ ...codeStyle, margin: '0 0 12px' }} aria-label={`Crew code ${code.split('').join(' ')}`}>{code}</p>
            <button type="button" onClick={copy} style={copyButton}>
              {copied ? 'Copied' : 'Copy code'}
            </button>
            <p style={note}>It was sent to you personally, works once, and expires 24 hours after it was made.</p>
            <a href={APP_STORE_URL} target="_blank" rel="noopener" style={storeLink}>
              <img src="/landing/assets/app-store-badge.svg" alt="Download on the App Store" style={badge} />
            </a>
          </>
        ) : (
          <>
            <h1 style={headline}>That doesn't look like a crew code</h1>
            <p style={body}>
              Check the link you were sent, or ask the person who sent it to make a new code in the Dot Watcher app.
            </p>
          </>
        )}
      </div>
    </main>
  )
}

const copyButton: CSSProperties = {
  margin: '0 0 20px',
  padding: '10px 20px',
  border: 0,
  borderRadius: 10,
  background: '#2f80f5',
  color: '#fff',
  fontSize: 15,
  fontWeight: 600,
  cursor: 'pointer',
}

const note: CSSProperties = {
  margin: '0 0 24px',
  fontSize: 13,
  lineHeight: 1.4,
  color: '#5B5D68',
}
