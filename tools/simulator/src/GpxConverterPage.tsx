import { useState, useMemo, useEffect } from 'react'
import { type Position, compressToInterval, filterByDistance, computeBearing } from './lib/positions'

const DISTANCE_FILTER_METERS = 10

const INTERVAL_OPTIONS: { label: string; seconds: number }[] = [
  { label: '15 seconds', seconds: 15 },
  { label: '30 seconds', seconds: 30 },
  { label: '60 seconds', seconds: 60 },
  { label: '5 minutes', seconds: 300 },
]

interface ParseResult {
  rawPositions: Position[]
  hasTimestamps: boolean
}

function defaultStartTime() {
  const d = new Date()
  d.setSeconds(0, 0)
  return d.toISOString().slice(0, 16)
}

export default function GpxConverterPage() {
  const [parseResult, setParseResult] = useState<ParseResult | null>(null)
  const [fileName, setFileName] = useState('')
  const [error, setError] = useState('')
  const [dragging, setDragging] = useState(false)
  const [copied, setCopied] = useState(false)
  const [startTime, setStartTime] = useState(defaultStartTime)
  const [postInterval, setPostInterval] = useState(15)
  const [shift12h, setShift12h] = useState(false)
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saved' | 'error'>('idle')

  const positions = useMemo<Position[] | null>(() => {
    if (!parseResult) return null

    let pts: Position[]
    if (parseResult.hasTimestamps) {
      pts = compressToInterval(parseResult.rawPositions, postInterval)
      pts = filterByDistance(pts, DISTANCE_FILTER_METERS)
    } else {
      const filtered = filterByDistance(parseResult.rawPositions, DISTANCE_FILTER_METERS)
      const origin = new Date(startTime).getTime()
      pts = filtered.map((p, i) => ({
        ...p,
        timestamp: new Date(origin + i * postInterval * 1000).toISOString(),
      }))
    }

    if (shift12h) {
      pts = pts.map(p => ({
        ...p,
        timestamp: new Date(new Date(p.timestamp).getTime() + 12 * 60 * 60 * 1000).toISOString(),
      }))
    }

    if (pts.length > 0) pts[0].heading = null
    for (let i = 1; i < pts.length; i++) {
      const prev = pts[i - 1]
      pts[i].heading = Math.round(
        computeBearing(prev.latitude, prev.longitude, pts[i].latitude, pts[i].longitude) * 10
      ) / 10
    }

    return pts
  }, [parseResult, postInterval, startTime, shift12h])

  useEffect(() => {
    if (!positions || !fileName) return
    const jsonName = fileName.replace(/\.gpx$/i, '.json')
    saveRoute(jsonName, positions)
  }, [positions, fileName])

  function processFile(file: File) {
    setError('')
    setParseResult(null)
    setCopied(false)
    setSaveStatus('idle')
    setFileName(file.name)

    const reader = new FileReader()
    reader.onload = (e) => {
      try {
        const xml = e.target?.result as string
        const result = parseGpx(xml)
        if (result.rawPositions.length === 0) {
          setError('No track points found in this GPX file.')
          return
        }
        setParseResult(result)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to parse GPX file.')
      }
    }
    reader.readAsText(file)
  }

  async function saveRoute(jsonName: string, pts: Position[]) {
    try {
      const res = await fetch('/api/save-route', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ filename: jsonName, content: JSON.stringify(pts, null, 2), force: true }),
      })
      setSaveStatus(res.ok ? 'saved' : 'error')
    } catch {
      setSaveStatus('error')
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault()
    setDragging(false)
    const file = e.dataTransfer.files[0]
    if (!file) return
    if (!file.name.toLowerCase().endsWith('.gpx')) {
      setError('Please drop a .gpx file.')
      return
    }
    processFile(file)
  }

  function handleFileInput(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) processFile(file)
    e.target.value = ''
  }

  function downloadJson() {
    if (!positions) return
    const blob = new Blob([JSON.stringify(positions, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = fileName.replace(/\.gpx$/i, '.json')
    a.click()
    URL.revokeObjectURL(url)
  }

  async function copyJson() {
    if (!positions) return
    await navigator.clipboard.writeText(JSON.stringify(positions, null, 2))
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div>
      <div
        onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        style={dropZone(dragging)}
      >
        <div style={dropIcon}>📂</div>
        <p style={dropText}>Drag a .gpx file here</p>
        <p style={dropOr}>or</p>
        <label style={fileLabel}>
          Choose file
          <input type="file" accept=".gpx" onChange={handleFileInput} style={{ display: 'none' }} />
        </label>
      </div>

      <div style={intervalRow}>
        <label style={intervalLabel}>
          POST interval
          <select
            value={postInterval}
            onChange={e => setPostInterval(Number(e.target.value))}
            style={intervalSelect}
          >
            {INTERVAL_OPTIONS.map(opt => (
              <option key={opt.seconds} value={opt.seconds}>{opt.label}</option>
            ))}
          </select>
        </label>
        <span style={filterNote}>Distance filter: {DISTANCE_FILTER_METERS} m</span>
        <label style={checkLabel}>
          <input
            type="checkbox"
            checked={shift12h}
            onChange={e => setShift12h(e.target.checked)}
          />
          +12h (UTC fix)
        </label>
      </div>

      {positions && (
        <p style={startTimeNote}>
          Start time: {new Date(positions[0].timestamp).toLocaleString(undefined, {
            weekday: 'short', day: 'numeric', month: 'short',
            hour: '2-digit', minute: '2-digit', second: '2-digit',
            timeZone: 'UTC', hour12: false,
          })} (UTC)
        </p>
      )}

      {error && <div style={errorBox}>{error}</div>}

      {parseResult && !parseResult.hasTimestamps && (
        <div style={noticeBox}>
          <strong>No timestamps in file</strong> — times will be generated from the start time below.
          <div style={genControls}>
            <label style={genLabel}>
              Start time
              <input
                type="datetime-local"
                value={startTime}
                onChange={e => setStartTime(e.target.value)}
                style={genInput}
              />
            </label>
          </div>
        </div>
      )}

      {saveStatus === 'saved' && (
        <div style={savedBox}>
          Saved to /tools/simulator/data/current_route/{fileName.replace(/\.gpx$/i, '.json')}
        </div>
      )}
      {saveStatus === 'error' && (
        <div style={errorBox}>Could not save to current_route — is the dev server running?</div>
      )}

      {positions && (
        <div style={{ marginTop: '1.5rem' }}>
          <div style={resultsHeader}>
            <span style={resultsTitle}>
              {fileName} <span style={{ color: '#64748b', fontWeight: 400 }}>— {positions.length} points</span>
            </span>
            <div style={{ display: 'flex', gap: '0.5rem' }}>
              <button style={actionBtn} onClick={copyJson}>
                {copied ? 'Copied!' : 'Copy JSON'}
              </button>
              <button style={actionBtn} onClick={downloadJson}>Download JSON</button>
            </div>
          </div>

          <table style={table}>
            <thead>
              <tr>
                <th style={th}>#</th>
                <th style={th}>Latitude</th>
                <th style={th}>Longitude</th>
                <th style={th}>Heading</th>
                <th style={th}>Time (UTC)</th>
              </tr>
            </thead>
            <tbody>
              {positions.slice(0, 20).map((pos, i) => (
                <tr key={i} style={{ background: i % 2 === 0 ? '#fff' : '#f1f5f9' }}>
                  <td style={td}>{i + 1}</td>
                  <td style={td}>{pos.latitude.toFixed(6)}</td>
                  <td style={td}>{pos.longitude.toFixed(6)}</td>
                  <td style={td}>{pos.heading !== null ? pos.heading.toFixed(1) + '°' : '—'}</td>
                  <td style={{ ...td, color: '#64748b', fontSize: '0.78rem' }}>{pos.timestamp.slice(11, 19)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {positions.length > 20 && (
            <p style={truncNote}>Showing first 20 of {positions.length} points — download to get all</p>
          )}
        </div>
      )}
    </div>
  )
}

function parseGpx(xml: string): ParseResult {
  const check = new DOMParser().parseFromString(xml, 'application/xml')
  if (check.querySelector('parsererror')) throw new Error('Invalid GPX file — could not parse XML.')

  const trkptRe = /<(?:[^:>\s]+:)?trkpt\b([^>]*)>([\s\S]*?)<\/(?:[^:>]+:)?trkpt>/g
  const latRe = /lat="([^"]+)"/
  const lonRe = /lon="([^"]+)"/
  const timeRe = /<(?:[^:>\s]+:)?time[^>]*>([^<]+)<\/(?:[^:>]+:)?time>/

  const rawPositions: Position[] = []
  let hasTimestamps = false
  let m: RegExpExecArray | null

  while ((m = trkptRe.exec(xml)) !== null) {
    const attrs = m[1]
    const inner = m[2]
    const lat = parseFloat(latRe.exec(attrs)?.[1] ?? '0')
    const lon = parseFloat(lonRe.exec(attrs)?.[1] ?? '0')
    const timestamp = timeRe.exec(inner)?.[1]?.trim() ?? ''
    if (timestamp) hasTimestamps = true
    rawPositions.push({ latitude: lat, longitude: lon, heading: null, timestamp })
  }

  return { rawPositions, hasTimestamps }
}


const dropZone = (active: boolean): React.CSSProperties => ({
  border: `2px dashed ${active ? '#1e293b' : '#cbd5e1'}`,
  borderRadius: 12,
  padding: '2.5rem 1rem',
  textAlign: 'center',
  background: active ? '#f1f5f9' : '#fff',
  transition: 'border-color 0.15s, background 0.15s',
  cursor: 'default',
})

const dropIcon: React.CSSProperties = {
  fontSize: '2.5rem',
  marginBottom: '0.5rem',
}

const dropText: React.CSSProperties = {
  fontSize: '1rem',
  fontWeight: 600,
  color: '#1e293b',
  margin: '0 0 0.25rem',
}

const dropOr: React.CSSProperties = {
  fontSize: '0.85rem',
  color: '#94a3b8',
  margin: '0.25rem 0',
}

const fileLabel: React.CSSProperties = {
  display: 'inline-block',
  marginTop: '0.4rem',
  padding: '0.4rem 1.2rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  color: '#1e293b',
  cursor: 'pointer',
  fontSize: '0.85rem',
  fontWeight: 600,
}

const intervalRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '1.5rem',
  marginTop: '1rem',
  flexWrap: 'wrap',
}

const intervalLabel: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.6rem',
  fontSize: '0.85rem',
  fontWeight: 600,
  color: '#1e293b',
}

const intervalSelect: React.CSSProperties = {
  padding: '0.3rem 0.6rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  fontSize: '0.85rem',
  color: '#1e293b',
  cursor: 'pointer',
}

const filterNote: React.CSSProperties = {
  fontSize: '0.8rem',
  color: '#94a3b8',
}

const startTimeNote: React.CSSProperties = {
  margin: '0.5rem 0 0',
  fontSize: '0.8rem',
  color: '#64748b',
}

const checkLabel: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.4rem',
  fontSize: '0.85rem',
  fontWeight: 600,
  color: '#1e293b',
  cursor: 'pointer',
}

const errorBox: React.CSSProperties = {
  marginTop: '1rem',
  padding: '0.75rem 1rem',
  borderRadius: 6,
  background: '#fef2f2',
  border: '1px solid #fecaca',
  color: '#dc2626',
  fontSize: '0.85rem',
}

const savedBox: React.CSSProperties = {
  marginTop: '1rem',
  padding: '0.75rem 1rem',
  borderRadius: 6,
  background: '#f0fdf4',
  border: '1px solid #bbf7d0',
  color: '#166534',
  fontSize: '0.85rem',
}

const noticeBox: React.CSSProperties = {
  marginTop: '1rem',
  padding: '0.75rem 1rem',
  borderRadius: 6,
  background: '#fefce8',
  border: '1px solid #fde68a',
  color: '#92400e',
  fontSize: '0.85rem',
}

const genControls: React.CSSProperties = {
  display: 'flex',
  gap: '1.5rem',
  marginTop: '0.6rem',
  flexWrap: 'wrap',
}

const genLabel: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '0.25rem',
  fontSize: '0.8rem',
  fontWeight: 600,
  color: '#78350f',
}

const genInput: React.CSSProperties = {
  padding: '0.3rem 0.5rem',
  borderRadius: 5,
  border: '1px solid #fcd34d',
  background: '#fff',
  fontSize: '0.85rem',
  color: '#1e293b',
}

const resultsHeader: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '1rem',
  marginBottom: '0.75rem',
}

const resultsTitle: React.CSSProperties = {
  fontWeight: 600,
  fontSize: '0.9rem',
}

const actionBtn: React.CSSProperties = {
  fontSize: '0.82rem',
  padding: '0.35rem 0.9rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  color: '#1e293b',
  cursor: 'pointer',
  fontWeight: 600,
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

const truncNote: React.CSSProperties = {
  fontSize: '0.78rem',
  color: '#94a3b8',
  textAlign: 'center',
  marginTop: '0.5rem',
}
