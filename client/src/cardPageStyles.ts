import type { CSSProperties } from 'react'

// Shared look of the small "open the app" fallback pages (JoinPage, CrewLinkPage).
export const page: CSSProperties = {
  minHeight: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  boxSizing: 'border-box',
  background: '#F0F1F5',
  fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Text", system-ui, sans-serif',
}

export const card: CSSProperties = {
  width: '100%',
  maxWidth: 420,
  boxSizing: 'border-box',
  padding: '32px 24px',
  background: '#fff',
  borderRadius: 20,
  boxShadow: '0 2px 12px rgba(20,21,26,.08)',
  textAlign: 'center',
}

export const icon: CSSProperties = {
  width: 64,
  height: 64,
  borderRadius: 16,
  boxShadow: '0 1px 3px rgba(20,21,26,.2)',
}

export const headline: CSSProperties = {
  margin: '16px 0 8px',
  fontSize: 26,
  lineHeight: 1.15,
  fontWeight: 800,
  letterSpacing: '-0.5px',
  color: '#14151A',
}

export const body: CSSProperties = {
  margin: '0 0 16px',
  fontSize: 16,
  lineHeight: 1.5,
  color: '#5B5D68',
}

export const codeStyle: CSSProperties = {
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

export const storeLink: CSSProperties = {
  display: 'inline-block',
  lineHeight: 0,
}

export const badge: CSSProperties = {
  height: 48,
  display: 'block',
}
