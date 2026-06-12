import { useState, useMemo } from 'react'
import { type Position, compressToMinutes, computeBearing } from './lib/positions'

interface ParseResult {
  rawPositions: Position[]  // timestamps may be '' when file has none
  hasTimestamps: boolean
}

function defaultStartTime() {
  const d = new Date()
  d.setSeconds(0, 0)
  return d.toISOString().slice(0, 16)  // "YYYY-MM-DDTHH:MM" for datetime-local
}

export default function GpxConverterPage() {
  const [parseResult, setParseResult] = useState<ParseResult | null>(null)
  const [fileName, setFileName] = useState('')
  const [error, setError] = useState('')
  const [dragging, setDragging] = useState(false)
  const [copied, setCopied] = useState(false)
  const [startTime, setStartTime] = useState(defaultStartTime)
  const [intervalSec, setIntervalSec] = useState(1)
  const [saveStatus, setSaveStatus] = useState<'idle' | 'conflict' | 'saved' | 'error'>('idle')

  const positions = useMemo<Position[] | null>(() => {
    if (!parseResult) return null
    if (parseResult.hasTimestamps) return parseResult.rawPositions
    const origin = new Date(startTime).getTime()
    return parseResult.rawPositions.map((p, i) => ({
      ...p,
      timestamp: new Date(origin + i * intervalSec * 1000).toISOString(),
    }))
  }, [parseResult, startTime, intervalSec])

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
        saveRoute(file.name.replace(/\.gpx$/i, '.json'), result, false)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to parse GPX file.')
      }
    }
    reader.readAsText(file)
  }

  async function saveRoute(jsonName: string, result: ParseResult, force: boolean) {
    const pts = result.hasTimestamps
      ? result.rawPositions
      : result.rawPositions.map((p, i) => ({
          ...p,
          timestamp: new Date(new Date(startTime).getTime() + i * intervalSec * 1000).toISOString(),
        }))

    try {
      const res = await fetch('/api/save-route', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ filename: jsonName, content: JSON.stringify(pts, null, 2), force }),
      })
      if (res.status === 409) { setSaveStatus('conflict'); return }
      if (!res.ok) { setSaveStatus('error'); return }
      setSaveStatus('saved')
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
    const json = JSON.stringify(positions, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
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

      {error && <div style={errorBox}>{error}</div>}

      {parseResult && !parseResult.hasTimestamps && (
        <div style={noticeBox}>
          <strong>No timestamps in file</strong> — times will be generated from the settings below.
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
            <label style={genLabel}>
              Interval (seconds)
              <input
                type="number"
                min={1}
                value={intervalSec}
                onChange={e => setIntervalSec(Math.max(1, parseInt(e.target.value) || 1))}
                style={{ ...genInput, width: 70 }}
              />
            </label>
          </div>
        </div>
      )}

      {saveStatus === 'conflict' && (
        <div style={conflictBox}>
          <strong>{fileName.replace(/\.gpx$/i, '.json')}</strong> already exists in /data/routes.{' '}
          <button style={inlineBtn} onClick={() => parseResult && saveRoute(fileName.replace(/\.gpx$/i, '.json'), parseResult, true)}>
            Replace
          </button>
          <button style={{ ...inlineBtn, marginLeft: '0.4rem', color: '#64748b' }} onClick={() => setSaveStatus('idle')}>
            Keep existing
          </button>
        </div>
      )}
      {saveStatus === 'saved' && (
        <div style={savedBox}>Saved to /data/routes/{fileName.replace(/\.gpx$/i, '.json')}</div>
      )}
      {saveStatus === 'error' && (
        <div style={errorBox}>Could not save to /data/routes — is the dev server running?</div>
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

  if (rawPositions.length === 0) return { rawPositions: [], hasTimestamps: false }

  // If the file has real timestamps, thin to one point per minute
  const finalPositions = hasTimestamps ? compressToMinutes(rawPositions) : rawPositions

  // Compute headings on the final set
  finalPositions[0].heading = null
  for (let i = 1; i < finalPositions.length; i++) {
    const prev = finalPositions[i - 1]
    const curr = finalPositions[i]
    curr.heading = Math.round(computeBearing(prev.latitude, prev.longitude, curr.latitude, curr.longitude) * 10) / 10
  }

  return { rawPositions: finalPositions, hasTimestamps }
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

const errorBox: React.CSSProperties = {
  marginTop: '1rem',
  padding: '0.75rem 1rem',
  borderRadius: 6,
  background: '#fef2f2',
  border: '1px solid #fecaca',
  color: '#dc2626',
  fontSize: '0.85rem',
}

const conflictBox: React.CSSProperties = {
  marginTop: '1rem',
  padding: '0.75rem 1rem',
  borderRadius: 6,
  background: '#fff7ed',
  border: '1px solid #fed7aa',
  color: '#9a3412',
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

const inlineBtn: React.CSSProperties = {
  padding: '0.2rem 0.7rem',
  borderRadius: 4,
  border: '1px solid currentColor',
  background: 'transparent',
  cursor: 'pointer',
  fontSize: '0.82rem',
  fontWeight: 600,
  color: '#9a3412',
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
