import { useState, useEffect, useRef } from 'react'

const INTERVAL_OPTIONS: { label: string; seconds: number }[] = [
  { label: '0.5s', seconds: 0.5 },
  { label: '1s', seconds: 1 },
  { label: '2s', seconds: 2 },
]

const SERVER_URL = import.meta.env.VITE_SERVER_URL ?? 'http://dot-watcher.skelstar.io/api'
const DEFAULT_SESSION_CODE = (import.meta.env.VITE_ROUTES_SESSION_CODE ?? '') as string
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

const routeModules = import.meta.glob('../data/current_route/*.json', { eager: true })

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

function formatUTC(isoTimestamp: string): string {
  return isoTimestamp.slice(11, 19)
}

export default function RoutesPage() {
  const [sessionCode, setSessionCode] = useState(DEFAULT_SESSION_CODE)
  const [codeFocused, setCodeFocused] = useState(false)
  const codeInputRef = useRef<HTMLInputElement>(null)

  const [timeIdx, setTimeIdx] = useState(0)
  const [statuses, setStatuses] = useState<Record<string, SendStatus>>(
    Object.fromEntries(routes.map(r => [r.runnerName, 'idle']))
  )
  const [postInterval, setPostInterval] = useState(1)
  const [isPlaying, setIsPlaying] = useState(false)
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const timeIdxRef = useRef(timeIdx)
  timeIdxRef.current = timeIdx

  const currentTime = allTimestamps[timeIdx] ?? null
  const atEnd = timeIdx >= allTimestamps.length - 1

  useEffect(() => {
    codeInputRef.current?.focus()
  }, [])

  useEffect(() => {
    if (!currentTime) return
    sendPositionsAt(currentTime)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeIdx])

  function handleCodeChange(e: React.ChangeEvent<HTMLInputElement>) {
    const filtered = e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 6)
    setSessionCode(filtered)
  }

  function startPlay(intervalSec: number) {
    if (timerRef.current) clearInterval(timerRef.current)
    timerRef.current = setInterval(() => {
      const next = timeIdxRef.current + 1
      if (next >= allTimestamps.length) {
        clearInterval(timerRef.current!)
        timerRef.current = null
        setIsPlaying(false)
        return
      }
      setTimeIdx(next)
    }, intervalSec * 1000)
    setIsPlaying(true)
  }

  function stopPlay() {
    if (timerRef.current) { clearInterval(timerRef.current); timerRef.current = null }
    setIsPlaying(false)
  }

  async function handleReset() {
    try {
      await fetch(`${SERVER_URL}/sessions/${sessionCode}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${BEARER_TOKEN}` },
      })
    } catch {
      // best-effort
    }
    stopPlay()
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
                sessionCode,
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

  return (
    <div>
      <div style={sessionEntry}>
        <label style={sessionLabel}>Session Name</label>
        <div
          style={codeBoxRow}
          onClick={() => { if (!isPlaying) codeInputRef.current?.focus() }}
        >
          <input
            ref={codeInputRef}
            value={sessionCode}
            onChange={handleCodeChange}
            onFocus={() => setCodeFocused(true)}
            onBlur={() => setCodeFocused(false)}
            disabled={isPlaying}
            style={hiddenInput}
            autoComplete="off"
            autoCapitalize="characters"
          />
          {Array.from({ length: 6 }, (_, i) => (
            <CodeBox
              key={i}
              char={sessionCode[i] ?? null}
              isActive={codeFocused && !isPlaying && sessionCode.length === i}
            />
          ))}
        </div>
      </div>

      <div style={subHeader}>
        <span style={metaServer}>Server: <strong>{SERVER_URL}</strong></span>
        <button style={resetButton} onClick={handleReset}>Reset</button>
      </div>

      <div style={timeRow}>
        <button style={arrowBtn(timeIdx === 0 || isPlaying)} onClick={() => setTimeIdx(i => i - 1)} disabled={timeIdx === 0 || isPlaying}>
          ←
        </button>
        <div style={timeBox}>{currentTime ? formatUTC(currentTime) : '—'}</div>
        <button style={arrowBtn(atEnd || isPlaying)} onClick={() => setTimeIdx(i => i + 1)} disabled={atEnd || isPlaying}>
          →
        </button>
        <button
          style={playBtn(isPlaying, atEnd || sessionCode.length < 6)}
          onClick={() => isPlaying ? stopPlay() : startPlay(postInterval)}
          disabled={atEnd || sessionCode.length < 6}
        >
          {isPlaying ? 'Pause' : 'Play'}
        </button>
        <select
          value={postInterval}
          onChange={e => { const v = Number(e.target.value); setPostInterval(v); if (isPlaying) startPlay(v) }}
          style={intervalSelect}
          disabled={isPlaying}
        >
          {INTERVAL_OPTIONS.map(opt => (
            <option key={opt.seconds} value={opt.seconds}>{opt.label}</option>
          ))}
        </select>
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

function CodeBox({ char, isActive }: { char: string | null; isActive: boolean }) {
  return (
    <div style={codeBox(isActive)}>
      {char
        ? <span style={codeChar}>{char}</span>
        : isActive
          ? <span style={cursor} />
          : null
      }
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

const sessionEntry: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  gap: '0.5rem',
  marginBottom: '1.5rem',
}

const sessionLabel: React.CSSProperties = {
  fontSize: '0.85rem',
  color: '#64748b',
  fontWeight: 500,
}

const codeBoxRow: React.CSSProperties = {
  display: 'flex',
  gap: '0.5rem',
  position: 'relative',
  cursor: 'text',
}

const hiddenInput: React.CSSProperties = {
  position: 'absolute',
  opacity: 0,
  width: 1,
  height: 1,
  pointerEvents: 'none',
}

const codeBox = (active: boolean): React.CSSProperties => ({
  width: 44,
  height: 54,
  borderRadius: 8,
  border: `${active ? 2 : 1.5}px solid ${active ? '#3b82f6' : '#cbd5e1'}`,
  background: '#fff',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
})

const codeChar: React.CSSProperties = {
  fontSize: '1.4rem',
  fontWeight: 700,
  color: '#1e293b',
  fontFamily: 'monospace',
}

const cursor: React.CSSProperties = {
  width: 2,
  height: 22,
  background: '#3b82f6',
  animation: 'blink 1s step-start infinite',
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

const metaServer: React.CSSProperties = {
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

const playBtn = (playing: boolean, disabled: boolean): React.CSSProperties => ({
  padding: '0.35rem 0.9rem',
  borderRadius: 8,
  border: '2px solid #e2e8f0',
  background: disabled ? '#f1f5f9' : playing ? '#dc2626' : '#16a34a',
  color: disabled ? '#cbd5e1' : '#fff',
  cursor: disabled ? 'default' : 'pointer',
  fontWeight: 700,
  fontSize: '0.85rem',
})

const intervalSelect: React.CSSProperties = {
  padding: '0.3rem 0.6rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  fontSize: '0.85rem',
  color: '#1e293b',
  cursor: 'pointer',
}

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
