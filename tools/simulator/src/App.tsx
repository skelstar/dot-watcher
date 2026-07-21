import { useState } from 'react'
import GpxConverterPage from './GpxConverterPage'
import ConvergencePage from './ConvergencePage'

type Tab = 'gpx' | 'session'

export default function App() {
  const [activeTab, setActiveTab] = useState<Tab>('session')

  return (
    <div style={page(activeTab)}>
      <header style={header}>
        <h1 style={title}>Dot Watcher — Simulator</h1>
        <nav style={tabBar}>
          <button style={tabBtn(activeTab === 'gpx')} onClick={() => setActiveTab('gpx')}>
            Importer
          </button>
          <button style={tabBtn(activeTab === 'session')} onClick={() => setActiveTab('session')}>
            Session
          </button>
        </nav>
      </header>
      {activeTab === 'gpx' && <GpxConverterPage />}
      {activeTab === 'session' && <ConvergencePage />}
    </div>
  )
}

const page = (tab: Tab): React.CSSProperties => ({
  maxWidth: tab === 'session' ? 1200 : 700,
  margin: '0 auto',
  padding: '1.5rem 1rem',
})

const header: React.CSSProperties = {
  marginBottom: '1.5rem',
}

const title: React.CSSProperties = {
  fontSize: '1.3rem',
  fontWeight: 700,
  marginBottom: '0.75rem',
}

const tabBar: React.CSSProperties = {
  display: 'flex',
  borderBottom: '2px solid #e2e8f0',
}

const tabBtn = (active: boolean): React.CSSProperties => ({
  padding: '0.5rem 1.25rem',
  border: 'none',
  background: 'none',
  cursor: 'pointer',
  fontSize: '0.9rem',
  fontWeight: active ? 700 : 500,
  color: active ? '#1e293b' : '#64748b',
  borderBottom: active ? '2px solid #1e293b' : '2px solid transparent',
  marginBottom: '-2px',
})
