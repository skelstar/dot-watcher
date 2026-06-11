import { useState } from 'react'
import positions from './positions.json'

const SERVER_URL = import.meta.env.VITE_SERVER_URL ?? 'http://localhost:5000'
const SESSION_CODE = import.meta.env.VITE_SESSION_CODE as string
const BEARER_TOKEN = import.meta.env.VITE_BEARER_TOKEN as string

const DISPLAY_COUNT = 20
const candidates = positions.slice(0, DISPLAY_COUNT)

type Status = 'idle' | 'sending' | 'ok' | 'error'

interface RowState {
  status: Status
  error?: string
}

export default function App() {
  const [rows, setRows] = useState<RowState[]>(() =>
    candidates.map(() => ({ status: 'idle' }))
  )

  function setRow(index: number, update: Partial<RowState>) {
    setRows(prev => prev.map((r, i) => (i === index ? { ...r, ...update } : r)))
  }

  async function handleCheck(index: number) {
    const pos = candidates[index]
    setRow(index, { status: 'sending' })

    try {
      const res = await fetch(`${SERVER_URL}/location`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${BEARER_TOKEN}`,
        },
        body: JSON.stringify({
          runnerName: pos.runnerName,
          sessionCode: SESSION_CODE,
          latitude: pos.latitude,
          longitude: pos.longitude,
          heading: pos.heading,
          timestamp: new Date().toISOString(),
        }),
      })

      if (res.ok) {
        setRow(index, { status: 'ok' })
      } else {
        setRow(index, { status: 'error', error: `HTTP ${res.status}` })
      }
    } catch (e) {
      setRow(index, { status: 'error', error: 'Network error' })
    }
  }

  // Row N is enabled only when row N-1 is 'ok' (or N === 0)
  function isEnabled(index: number) {
    if (index === 0) return rows[0].status === 'idle' || rows[0].status === 'error'
    return rows[index - 1].status === 'ok' && rows[index].status !== 'ok'
  }

  async function handleReset() {
    try {
      await fetch(`${SERVER_URL}/sessions/${SESSION_CODE}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${BEARER_TOKEN}` },
      })
    } catch {
      // best-effort — reset the UI regardless
    }
    setRows(candidates.map(() => ({ status: 'idle' })))
  }

  return (
    <div style={page}>
      <header style={header}>
        <h1 style={title}>Dot Watcher — Simulator</h1>
        <div style={headerRow}>
          <div style={meta}>
            <span>Session: <strong>{SESSION_CODE ?? '⚠ not set'}</strong></span>
            <span>Server: <strong>{SERVER_URL}</strong></span>
          </div>
          <button style={resetButton} onClick={handleReset}>Reset</button>
        </div>
      </header>

      <table style={table}>
        <thead>
          <tr>
            <th style={th}>#</th>
            <th style={th}>Send</th>
            <th style={th}>Lat</th>
            <th style={th}>Lng</th>
            <th style={th}>Heading</th>
            <th style={th}>Original timestamp</th>
            <th style={th}>Result</th>
          </tr>
        </thead>
        <tbody>
          {candidates.map((pos, i) => {
            const row = rows[i]
            const enabled = isEnabled(i)
            return (
              <tr key={i} style={{ background: i % 2 === 0 ? '#fff' : '#f1f5f9' }}>
                <td style={td}>{i + 1}</td>
                <td style={td}>
                  <input
                    type="checkbox"
                    disabled={!enabled}
                    checked={row.status === 'ok'}
                    onChange={() => enabled && handleCheck(i)}
                    style={{ width: 18, height: 18, cursor: enabled ? 'pointer' : 'default' }}
                  />
                </td>
                <td style={tdMono}>{pos.latitude.toFixed(6)}</td>
                <td style={tdMono}>{pos.longitude.toFixed(6)}</td>
                <td style={tdMono}>{pos.heading ?? '—'}</td>
                <td style={tdMono}>{pos.timestamp}</td>
                <td style={td}>{statusBadge(row)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function statusBadge(row: RowState) {
  if (row.status === 'idle') return null
  if (row.status === 'sending') return <span style={badge('#94a3b8')}>Sending…</span>
  if (row.status === 'ok') return <span style={badge('#22c55e')}>Sent</span>
  return <span style={badge('#ef4444')} title={row.error}>Error</span>
}

const page: React.CSSProperties = {
  maxWidth: 860,
  margin: '0 auto',
  padding: '1.5rem 1rem',
}

const header: React.CSSProperties = {
  marginBottom: '1.25rem',
}

const headerRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '1rem',
}

const resetButton: React.CSSProperties = {
  fontSize: '0.85rem',
  padding: '0.4rem 1rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  color: '#1e293b',
  cursor: 'pointer',
  fontWeight: 600,
  whiteSpace: 'nowrap',
}

const title: React.CSSProperties = {
  fontSize: '1.3rem',
  fontWeight: 700,
  marginBottom: '0.4rem',
}

const meta: React.CSSProperties = {
  display: 'flex',
  gap: '1.5rem',
  fontSize: '0.85rem',
  color: '#64748b',
}

const table: React.CSSProperties = {
  width: '100%',
  borderCollapse: 'collapse',
  fontSize: '0.85rem',
  boxShadow: '0 1px 4px rgba(0,0,0,0.08)',
  borderRadius: 8,
  overflow: 'hidden',
}

const th: React.CSSProperties = {
  background: '#1e293b',
  color: '#fff',
  padding: '0.6rem 0.75rem',
  textAlign: 'left',
  fontWeight: 600,
}

const td: React.CSSProperties = {
  padding: '0.5rem 0.75rem',
  verticalAlign: 'middle',
}

const tdMono: React.CSSProperties = {
  ...td,
  fontFamily: 'monospace',
}

const badge = (bg: string): React.CSSProperties => ({
  background: bg,
  color: '#fff',
  borderRadius: 4,
  padding: '2px 8px',
  fontSize: '0.78rem',
  fontWeight: 600,
})
