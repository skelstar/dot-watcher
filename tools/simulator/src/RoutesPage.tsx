import { useState, useEffect } from 'react'

const SERVER_URL = import.meta.env.VITE_SERVER_URL ?? 'http://dot-watcher.skelstar.io/api'
const SESSION_CODE = import.meta.env.VITE_ROUTES_SESSION_CODE ?? 'sim-routes'
const BEARER_TOKEN = import.meta.env.VITE_BEARER_TOKEN as string

type Position = {
  latitude: number
  longitude: number
  heading: number | null
  timestamp: string
}

type RunnerRoute = {
  runnerName: string
  positions: Position[]
}

type SendStatus = 'idle' | 'sending' | 'sent' | 'error' | 'no-data'

const routeModules = import.meta.glob('../../../data/routes/*.json', { eager: true })

function extractRunnerName(filePath: string): string {
  const filename = filePath.split('/').pop()!.replace('.json', '')
  const dashIdx = filename.indexOf(' - ')
  return dashIdx !== -1 ? filename.slice(0, dashIdx) : filename
}

const routes: RunnerRoute[] = Object.entries(routeModules).map(([path, mod]) => ({
  runnerName: extractRunnerName(path),
  positions: (mod as { default: Position[] }).default,
}))

const allTimestamps = [
  ...new Set(routes.flatMap(r => r.positions.map(p => p.timestamp))),
].sort()

function formatNZST(isoTimestamp: string): string {
  return new Date(isoTimestamp).toLocaleTimeString('en-NZ', {
    timeZone: 'Pacific/Auckland',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  })
}

export default function RoutesPage() {
  const [timeIdx, setTimeIdx] = useState(0)
  const [statuses, setStatuses] = useState<Record<string, SendStatus>>(
    Object.fromEntries(routes.map(r => [r.runnerName, 'idle']))
  )

  const currentTime = allTimestamps[timeIdx] ?? null

  useEffect(() => {
    if (!currentTime) return
    sendPositionsAt(currentTime)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeIdx])

  async function handleReset() {
    try {
      await fetch(`${SERVER_URL}/sessions/${SESSION_CODE}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${BEARER_TOKEN}` },
      })
    } catch {
      // best-effort
    }
    setTimeIdx(0)
    setStatuses(Object.fromEntries(routes.map(r => [r.runnerName, 'idle'])))
  }

  async function sendPositionsAt(timestamp: string) {
    const toSend = routes.map(r => ({
      runner: r.runnerName,
      pos: r.positions.find(p => p.timestamp === timestamp) ?? null,
    }))

    setStatuses(
      Object.fromEntries(toSend.map(({ runner, pos }) => [runner, pos ? 'sending' : 'no-data']))
    )

    await Promise.all(
      toSend
        .filter(({ pos }) => pos !== null)
        .map(async ({ runner, pos }) => {
          try {
            const res = await fetch(`${SERVER_URL}/location`, {
              method: 'POST',
              headers: {
                'Content-Type': 'application/json',
                Authorization: `Bearer ${BEARER_TOKEN}`,
              },
              body: JSON.stringify({
                runnerName: runner,
                sessionCode: SESSION_CODE,
                latitude: pos!.latitude,
                longitude: pos!.longitude,
                heading: pos!.heading,
                timestamp: pos!.timestamp,
              }),
            })
            setStatuses(prev => ({ ...prev, [runner]: res.ok ? 'sent' : 'error' }))
          } catch {
            setStatuses(prev => ({ ...prev, [runner]: 'error' }))
          }
        })
    )
  }

  const atEnd = timeIdx >= allTimestamps.length - 1

  return (
    <div>
      <div style={subHeader}>
        <div style={metaStyle}>
          <span>Session: <strong>{SESSION_CODE}</strong></span>
          <span>Server: <strong>{SERVER_URL}</strong></span>
        </div>
        <button style={resetButton} onClick={handleReset}>Reset</button>
      </div>

      <div style={timeRow}>
        <div style={timeBox}>{currentTime ? formatNZST(currentTime) : '—'}</div>
        <button style={arrowBtn(atEnd)} onClick={() => setTimeIdx(i => i + 1)} disabled={atEnd}>
          →
        </button>
        <span style={stepLabel}>
          Step {timeIdx + 1} of {allTimestamps.length}
        </span>
      </div>

      <table style={table}>
        <thead>
          <tr>
            <th style={th}>Runner</th>
            <th style={th}>Status</th>
          </tr>
        </thead>
        <tbody>
          {routes.map((r, i) => (
            <tr key={r.runnerName} style={{ background: i % 2 === 0 ? '#fff' : '#f1f5f9' }}>
              <td style={td}>{r.runnerName}</td>
              <td style={td}>{statusBadge(statuses[r.runnerName])}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function statusBadge(status: SendStatus) {
  switch (status) {
    case 'sending': return <span style={badge('#94a3b8')}>Sending…</span>
    case 'sent':    return <span style={badge('#22c55e')}>Sent</span>
    case 'error':   return <span style={badge('#ef4444')}>Error</span>
    case 'no-data': return <span style={badge('#e2e8f0', '#94a3b8')}>No data</span>
    default:        return null
  }
}

const subHeader: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  marginBottom: '1.25rem',
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
}

const metaStyle: React.CSSProperties = {
  display: 'flex',
  gap: '1.5rem',
  fontSize: '0.85rem',
  color: '#64748b',
}

const timeRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.75rem',
  marginBottom: '1.5rem',
}

const timeBox: React.CSSProperties = {
  fontFamily: 'monospace',
  fontSize: '1.3rem',
  fontWeight: 700,
  padding: '0.4rem 1rem',
  border: '2px solid #e2e8f0',
  borderRadius: 8,
  background: '#f8fafc',
  minWidth: '10ch',
  textAlign: 'center',
}

const arrowBtn = (disabled: boolean): React.CSSProperties => ({
  fontSize: '1.4rem',
  lineHeight: 1,
  padding: '0.35rem 0.9rem',
  border: '2px solid #e2e8f0',
  borderRadius: 8,
  background: disabled ? '#f1f5f9' : '#1e293b',
  color: disabled ? '#cbd5e1' : '#fff',
  cursor: disabled ? 'default' : 'pointer',
  fontWeight: 700,
})

const stepLabel: React.CSSProperties = {
  fontSize: '0.8rem',
  color: '#94a3b8',
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

const badge = (bg: string, color = '#fff'): React.CSSProperties => ({
  background: bg,
  color,
  borderRadius: 4,
  padding: '2px 8px',
  fontSize: '0.78rem',
  fontWeight: 600,
})
