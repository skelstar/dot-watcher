import type { ReplayState } from './useReplay.ts'

interface Props {
  replay: ReplayState
  onFitAll: () => void
}

function formatTime(ms: number): string {
  const totalSec = Math.floor(ms / 1000)
  const h = Math.floor(totalSec / 3600)
  const m = Math.floor((totalSec % 3600) / 60)
  const s = totalSec % 60
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

export default function ReplayControls({ replay, onFitAll }: Props) {
  const { currentTimeMs, durationMs, loaded, error, playing, speed, play, pause, seek, setSpeed } = replay

  function handleFast() {
    if (speed === 20) {
      setSpeed(10)
    } else {
      setSpeed(20)
      if (!playing) play()
    }
  }

  if (error) {
    return (
      <div style={{ ...bar, color: '#ef4444', fontSize: 13, fontFamily: 'system-ui, sans-serif' }}>
        Recording not found: {error}
      </div>
    )
  }

  if (!loaded) {
    return (
      <div style={{ ...bar, color: '#64748b', fontSize: 13, fontFamily: 'system-ui, sans-serif' }}>
        Loading recording…
      </div>
    )
  }

  return (
    <div style={bar}>
      <button
        onClick={playing ? pause : play}
        disabled={!loaded}
        style={playBtn}
        title={playing ? 'Pause' : 'Play'}
      >
        {playing
          ? '⏸'
          : <svg width="10" height="14" viewBox="0 0 10 14" fill="white"><polygon points="0,0 10,7 0,14" /></svg>
        }
      </button>

      <button
        onClick={handleFast}
        disabled={!loaded}
        style={playBtn}
        title="Fast forward (20×)"
      >
        <svg width="17" height="14" viewBox="0 0 17 14" fill="white">
          <polygon points="0,0 7,7 0,14" />
          <polygon points="10,0 17,7 10,14" />
        </svg>
      </button>

      <span style={timeLabel}>
        {formatTime(currentTimeMs)}
      </span>

      <input
        type="range"
        min={0}
        max={durationMs || 1}
        value={currentTimeMs}
        disabled={!loaded}
        onPointerDown={pause}
        onChange={e => seek(Number(e.target.value))}
        style={scrubber}
      />

      <span style={timeLabel}>
        {formatTime(durationMs)}
      </span>

      <button onClick={onFitAll} style={fitAllBtn} title="Fit all">⤢</button>
    </div>
  )
}

const bar: React.CSSProperties = {
  position: 'absolute',
  bottom: 36,
  left: '50%',
  transform: 'translateX(-50%)',
  display: 'flex',
  alignItems: 'center',
  gap: 10,
  background: 'rgba(255,255,255,0.95)',
  borderRadius: 10,
  padding: '8px 14px',
  boxShadow: '0 2px 12px rgba(0,0,0,0.2)',
  zIndex: 10,
  maxWidth: 'calc(100vw - 32px)',
  boxSizing: 'border-box',
}

const playBtn: React.CSSProperties = {
  width: 34,
  height: 34,
  border: 'none',
  borderRadius: 6,
  background: '#3b82f6',
  color: '#fff',
  fontSize: '1rem',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
}

const scrubber: React.CSSProperties = {
  flex: 1,
  minWidth: 80,
  cursor: 'pointer',
}

const timeLabel: React.CSSProperties = {
  fontSize: 12,
  fontFamily: 'monospace',
  color: '#334155',
  flexShrink: 0,
  minWidth: 38,
  textAlign: 'center',
}

const fitAllBtn: React.CSSProperties = {
  width: 34,
  height: 34,
  border: 'none',
  borderRadius: 6,
  background: '#e2e8f0',
  color: '#334155',
  fontSize: '1.1rem',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
}

