import { useState, useMemo } from 'react'
import { type Position, compressToInterval, filterByDistance, computeBearing } from './lib/positions'

const DISTANCE_FILTER_METERS = 10

const INTERVAL_OPTIONS = [
  { label: '15s', seconds: 15 },
  { label: '30s', seconds: 30 },
  { label: '1 min', seconds: 60 },
  { label: '5 min', seconds: 300 },
]

interface ParseResult {
  rawPositions: Position[]
  hasTimestamps: boolean
}

interface RunnerSlot {
  id: string
  runnerName: string
  parseResult: ParseResult | null
  fileName: string
  startTime: string
  shift12h: boolean
  error: string
}

function newSlot(): RunnerSlot {
  const d = new Date()
  d.setSeconds(0, 0)
  return {
    id: crypto.randomUUID(),
    runnerName: '',
    parseResult: null,
    fileName: '',
    startTime: d.toISOString().slice(0, 16),
    shift12h: false,
    error: '',
  }
}

function computePositions(slot: RunnerSlot, intervalSec: number): Position[] | null {
  if (!slot.parseResult) return null

  let pts: Position[]
  if (slot.parseResult.hasTimestamps) {
    pts = compressToInterval(slot.parseResult.rawPositions, intervalSec)
    pts = filterByDistance(pts, DISTANCE_FILTER_METERS)
  } else {
    const filtered = filterByDistance(slot.parseResult.rawPositions, DISTANCE_FILTER_METERS)
    const origin = new Date(slot.startTime).getTime()
    pts = filtered.map((p, i) => ({
      ...p,
      timestamp: new Date(origin + i * intervalSec * 1000).toISOString(),
    }))
  }

  if (slot.shift12h) {
    pts = pts.map(p => ({
      ...p,
      timestamp: new Date(new Date(p.timestamp).getTime() + 12 * 3600 * 1000).toISOString(),
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

export default function GpxConverterPage() {
  const [slots, setSlots] = useState<RunnerSlot[]>([newSlot()])
  const [sessionCode, setSessionCode] = useState('')
  const [postInterval, setPostInterval] = useState(15)

  const processedSlots = useMemo(
    () => slots.map(slot => ({ slot, positions: computePositions(slot, postInterval) })),
    [slots, postInterval],
  )

  const readySlots = processedSlots.filter(({ slot, positions }) => positions && slot.runnerName.trim())
  const totalPoints = readySlots.reduce((n, { positions }) => n + (positions?.length ?? 0), 0)
  const canDownload = sessionCode.trim().length > 0 && totalPoints > 0

  function updateSlot(id: string, patch: Partial<RunnerSlot>) {
    setSlots(prev => prev.map(s => s.id === id ? { ...s, ...patch } : s))
  }

  function removeSlot(id: string) {
    setSlots(prev => prev.filter(s => s.id !== id))
  }

  function processFile(id: string, file: File) {
    updateSlot(id, { error: '', parseResult: null, fileName: file.name })
    const reader = new FileReader()
    reader.onload = e => {
      try {
        const result = parseGpx(e.target?.result as string)
        if (result.rawPositions.length === 0) {
          updateSlot(id, { error: 'No track points found.' })
        } else {
          updateSlot(id, { parseResult: result, error: '' })
        }
      } catch (err) {
        updateSlot(id, { error: err instanceof Error ? err.message : 'Failed to parse GPX.' })
      }
    }
    reader.readAsText(file)
  }

  function downloadNdjson() {
    if (!canDownload) return
    const code = sessionCode.trim().toUpperCase()

    const entries: { runnerName: string; pos: Position }[] = []
    for (const { slot, positions } of processedSlots) {
      if (!positions || !slot.runnerName.trim()) continue
      for (const pos of positions) entries.push({ runnerName: slot.runnerName.trim(), pos })
    }
    entries.sort((a, b) => (a.pos.timestamp < b.pos.timestamp ? -1 : 1))

    const lines = entries.map(({ runnerName, pos }) =>
      JSON.stringify({
        runnerName,
        sessionCode: code,
        latitude: pos.latitude,
        longitude: pos.longitude,
        heading: pos.heading,
        timestamp: pos.timestamp,
      }),
    )

    const blob = new Blob([lines.join('\n') + '\n'], { type: 'application/x-ndjson' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${code}.ndjson`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div>
      <div style={topRow}>
        <label style={fieldLabel}>
          Session code
          <input
            type="text"
            placeholder="e.g. WAINOT"
            value={sessionCode}
            onChange={e => setSessionCode(e.target.value.toUpperCase())}
            style={codeInput}
          />
        </label>
        <label style={fieldLabel}>
          Interval
          <select
            value={postInterval}
            onChange={e => setPostInterval(Number(e.target.value))}
            style={selectStyle}
          >
            {INTERVAL_OPTIONS.map(o => (
              <option key={o.seconds} value={o.seconds}>{o.label}</option>
            ))}
          </select>
        </label>
        <span style={filterNote}>Distance filter: {DISTANCE_FILTER_METERS} m</span>
      </div>

      <div style={slotList}>
        {slots.map((slot, idx) => (
          <RunnerSlotCard
            key={slot.id}
            slot={slot}
            positions={processedSlots[idx].positions}
            showRemove={slots.length > 1}
            onUpdate={patch => updateSlot(slot.id, patch)}
            onRemove={() => removeSlot(slot.id)}
            onFile={file => processFile(slot.id, file)}
          />
        ))}
      </div>

      <button style={addBtn} onClick={() => setSlots(prev => [...prev, newSlot()])}>
        + Add runner
      </button>

      <div style={downloadRow}>
        <button
          style={canDownload ? dlBtnActive : dlBtn}
          disabled={!canDownload}
          onClick={downloadNdjson}
        >
          Download NDJSON{totalPoints > 0 ? ` (${totalPoints} points)` : ''}
        </button>
        {processedSlots.some(({ slot, positions }) => positions && !slot.runnerName.trim()) && (
          <span style={dlWarning}>Runners without a name will be excluded.</span>
        )}
      </div>
    </div>
  )
}

// ── Runner slot card ────────────────────────────────────────────────────────

interface SlotCardProps {
  slot: RunnerSlot
  positions: Position[] | null
  showRemove: boolean
  onUpdate: (patch: Partial<RunnerSlot>) => void
  onRemove: () => void
  onFile: (file: File) => void
}

function RunnerSlotCard({ slot, positions, showRemove, onUpdate, onRemove, onFile }: SlotCardProps) {
  const [dragging, setDragging] = useState(false)

  function handleDrop(e: React.DragEvent) {
    e.preventDefault()
    setDragging(false)
    const file = e.dataTransfer.files[0]
    if (file?.name.toLowerCase().endsWith('.gpx')) onFile(file)
    else onUpdate({ error: 'Please drop a .gpx file.' })
  }

  function handleFileInput(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) onFile(file)
    e.target.value = ''
  }

  const timeRange = positions && positions.length > 0
    ? `${fmt(positions[0].timestamp)} → ${fmt(positions[positions.length - 1].timestamp)} UTC`
    : null

  return (
    <div style={slotCard}>
      <div style={slotHeader}>
        <input
          type="text"
          placeholder="Runner name"
          value={slot.runnerName}
          onChange={e => onUpdate({ runnerName: e.target.value })}
          style={nameInput}
        />
        {showRemove && (
          <button onClick={onRemove} style={removeBtn} title="Remove runner">✕</button>
        )}
      </div>

      <div
        onDragOver={e => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        style={dropZone(dragging, !!slot.parseResult)}
      >
        {slot.parseResult ? (
          <span style={dropLoaded}>
            ✓ {slot.fileName}
            {positions && ` — ${positions.length} points`}
            <label style={changeLink}>
              {' '}(change<input type="file" accept=".gpx" onChange={handleFileInput} style={{ display: 'none' }} />)
            </label>
          </span>
        ) : (
          <label style={dropPrompt}>
            Drop a .gpx file or{' '}
            <span style={{ color: '#2563eb', textDecoration: 'underline' }}>browse</span>
            <input type="file" accept=".gpx" onChange={handleFileInput} style={{ display: 'none' }} />
          </label>
        )}
      </div>

      {slot.error && <div style={slotError}>{slot.error}</div>}

      {slot.parseResult && (
        <div style={slotOptions}>
          {!slot.parseResult.hasTimestamps && (
            <label style={optLabel}>
              Start time
              <input
                type="datetime-local"
                value={slot.startTime}
                onChange={e => onUpdate({ startTime: e.target.value })}
                style={optInput}
              />
            </label>
          )}
          <label style={checkLabel}>
            <input
              type="checkbox"
              checked={slot.shift12h}
              onChange={e => onUpdate({ shift12h: e.target.checked })}
            />
            +12h (UTC fix)
          </label>
          {timeRange && <span style={timeRangeNote}>{timeRange}</span>}
        </div>
      )}
    </div>
  )
}

function fmt(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    timeZone: 'UTC', hour12: false,
    weekday: 'short', day: 'numeric', month: 'short',
    hour: '2-digit', minute: '2-digit',
  })
}

// ── Styles ──────────────────────────────────────────────────────────────────

const topRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '1.5rem',
  flexWrap: 'wrap',
  marginBottom: '1rem',
}

const fieldLabel: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.5rem',
  fontSize: '0.85rem',
  fontWeight: 600,
  color: '#1e293b',
}

const codeInput: React.CSSProperties = {
  padding: '0.3rem 0.6rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  background: '#fff',
  fontSize: '0.9rem',
  fontFamily: 'monospace',
  color: '#1e293b',
  width: 140,
  letterSpacing: '0.05em',
}

const selectStyle: React.CSSProperties = {
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

const slotList: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '0.75rem',
}

const slotCard: React.CSSProperties = {
  border: '1.5px solid #e2e8f0',
  borderRadius: 10,
  padding: '0.875rem 1rem',
  background: '#fff',
  display: 'flex',
  flexDirection: 'column',
  gap: '0.6rem',
}

const slotHeader: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.6rem',
}

const nameInput: React.CSSProperties = {
  flex: 1,
  padding: '0.35rem 0.65rem',
  borderRadius: 6,
  border: '1.5px solid #cbd5e1',
  fontSize: '0.9rem',
  color: '#1e293b',
  fontWeight: 600,
}

const removeBtn: React.CSSProperties = {
  border: 'none',
  background: 'none',
  color: '#94a3b8',
  fontSize: '1rem',
  cursor: 'pointer',
  padding: '0 4px',
  lineHeight: 1,
}

const dropZone = (active: boolean, loaded: boolean): React.CSSProperties => ({
  border: `1.5px dashed ${active ? '#1e293b' : loaded ? '#bbf7d0' : '#cbd5e1'}`,
  borderRadius: 7,
  padding: '0.6rem 0.85rem',
  background: active ? '#f1f5f9' : loaded ? '#f0fdf4' : '#f8fafc',
  transition: 'all 0.15s',
  fontSize: '0.82rem',
})

const dropLoaded: React.CSSProperties = {
  color: '#166534',
}

const changeLink: React.CSSProperties = {
  color: '#64748b',
  cursor: 'pointer',
  textDecoration: 'underline',
}

const dropPrompt: React.CSSProperties = {
  cursor: 'pointer',
  color: '#64748b',
}

const slotError: React.CSSProperties = {
  padding: '0.4rem 0.65rem',
  borderRadius: 5,
  background: '#fef2f2',
  border: '1px solid #fecaca',
  color: '#dc2626',
  fontSize: '0.8rem',
}

const slotOptions: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '1rem',
  flexWrap: 'wrap',
}

const optLabel: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.4rem',
  fontSize: '0.8rem',
  fontWeight: 600,
  color: '#475569',
}

const optInput: React.CSSProperties = {
  padding: '0.25rem 0.45rem',
  borderRadius: 5,
  border: '1px solid #cbd5e1',
  fontSize: '0.8rem',
  color: '#1e293b',
}

const checkLabel: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '0.35rem',
  fontSize: '0.8rem',
  fontWeight: 600,
  color: '#475569',
  cursor: 'pointer',
}

const timeRangeNote: React.CSSProperties = {
  fontSize: '0.75rem',
  color: '#64748b',
}

const addBtn: React.CSSProperties = {
  marginTop: '0.75rem',
  padding: '0.4rem 1rem',
  borderRadius: 6,
  border: '1.5px dashed #cbd5e1',
  background: '#fff',
  color: '#475569',
  fontSize: '0.85rem',
  fontWeight: 600,
  cursor: 'pointer',
}

const downloadRow: React.CSSProperties = {
  marginTop: '1.25rem',
  display: 'flex',
  alignItems: 'center',
  gap: '1rem',
  flexWrap: 'wrap',
}

const dlBtn: React.CSSProperties = {
  padding: '0.5rem 1.25rem',
  borderRadius: 7,
  border: '1.5px solid #cbd5e1',
  background: '#f1f5f9',
  color: '#94a3b8',
  fontSize: '0.9rem',
  fontWeight: 600,
  cursor: 'default',
}

const dlBtnActive: React.CSSProperties = {
  ...dlBtn,
  background: '#1e293b',
  borderColor: '#1e293b',
  color: '#fff',
  cursor: 'pointer',
}

const dlWarning: React.CSSProperties = {
  fontSize: '0.8rem',
  color: '#92400e',
}
