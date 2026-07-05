import { useEffect, useRef, useState } from 'react'
import type { SessionTimelineState } from './useSessionTimeline.ts'

interface Props {
  timeline: SessionTimelineState
  onFitAll: () => void
}

const TIME_TOOLTIP_LINGER_MS = 800

function formatTime(ms: number): string {
  const totalSec = Math.floor(ms / 1000)
  const h = Math.floor(totalSec / 3600)
  const m = Math.floor((totalSec % 3600) / 60)
  const s = totalSec % 60
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

export default function ReplayControls({ timeline, onFitAll }: Props) {
  const { following, scrubTimeMs, runStartMs, nowMs, dragTo, dragEnd, goLive } = timeline
  const trackRef = useRef<HTMLDivElement>(null)
  const [showTooltip, setShowTooltip] = useState(false)
  const lingerTimerRef = useRef<ReturnType<typeof setTimeout>>()

  const rangeStart = runStartMs ?? nowMs
  const rangeEnd = nowMs
  const durationMs = Math.max(rangeEnd - rangeStart, 1)
  const currentMs = scrubTimeMs ?? nowMs
  const fraction = Math.max(0, Math.min(1, (currentMs - rangeStart) / durationMs))

  useEffect(() => () => clearTimeout(lingerTimerRef.current), [])

  function timeFromClientX(clientX: number): number {
    const track = trackRef.current
    if (!track) return currentMs
    const rect = track.getBoundingClientRect()
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
    return rangeStart + ratio * durationMs
  }

  function handlePointerDown(event: React.PointerEvent<HTMLDivElement>) {
    event.currentTarget.setPointerCapture(event.pointerId)
    clearTimeout(lingerTimerRef.current)
    setShowTooltip(true)
    dragTo(timeFromClientX(event.clientX))
  }

  function handlePointerMove(event: React.PointerEvent<HTMLDivElement>) {
    if (event.buttons === 0) return
    dragTo(timeFromClientX(event.clientX))
  }

  function handlePointerUp() {
    dragEnd()
    lingerTimerRef.current = setTimeout(() => setShowTooltip(false), TIME_TOOLTIP_LINGER_MS)
  }

  return (
    <div style={bar}>
      {runStartMs !== null && (
        <div
          ref={trackRef}
          style={track}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
        >
          <div style={{ ...trackFill, width: `${fraction * 100}%` }} />
          {showTooltip && (
            <span style={{ ...timeTooltip, left: `${fraction * 100}%` }}>
              {formatTime(currentMs - rangeStart)}
            </span>
          )}
          <div style={{ ...dot, left: `${fraction * 100}%` }} />
        </div>
      )}

      <button onClick={goLive} style={{ ...liveBtn, ...(following ? liveBtnActive : liveBtnInactive) }} title="Go live">
        <span style={{ ...liveDot, background: following ? '#fff' : '#94a3b8' }} />
        LIVE
      </button>

      <button onClick={onFitAll} style={fitAllBtn} title="Fit all">⤢</button>
    </div>
  )
}

const bar: React.CSSProperties = {
  position: 'absolute',
  bottom: 27,
  left: 0,
  right: 0,
  display: 'flex',
  alignItems: 'center',
  padding: '0 12px',
  zIndex: 10,
  boxSizing: 'border-box',
  pointerEvents: 'none',
}

const track: React.CSSProperties = {
  position: 'relative',
  flex: 1,
  minWidth: 40,
  height: 34,
  display: 'flex',
  alignItems: 'center',
  cursor: 'pointer',
  touchAction: 'none',
  pointerEvents: 'auto',
}

const trackFill: React.CSSProperties = {
  position: 'absolute',
  left: 0,
  height: 3,
  borderRadius: 2,
  background: '#ef4444',
  boxShadow: '0 0 3px rgba(0,0,0,0.4)',
}

const dot: React.CSSProperties = {
  position: 'absolute',
  width: 16,
  height: 16,
  borderRadius: '50%',
  background: '#ef4444',
  border: '2px solid #fff',
  boxShadow: '0 1px 4px rgba(0,0,0,0.5)',
  transform: 'translateX(-50%)',
}

const timeTooltip: React.CSSProperties = {
  position: 'absolute',
  bottom: '100%',
  marginBottom: 8,
  transform: 'translateX(-50%)',
  background: 'rgba(0,0,0,0.75)',
  color: '#fff',
  fontSize: 12,
  fontFamily: 'monospace',
  fontWeight: 600,
  borderRadius: 4,
  padding: '2px 6px',
  whiteSpace: 'nowrap',
  pointerEvents: 'none',
}

const liveBtn: React.CSSProperties = {
  height: 34,
  border: 'none',
  borderRadius: 6,
  fontSize: 12,
  fontWeight: 700,
  fontFamily: 'system-ui, sans-serif',
  letterSpacing: 0.5,
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 6,
  flexShrink: 0,
  marginLeft: 13,
  padding: '0 12px',
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  pointerEvents: 'auto',
}

const liveBtnActive: React.CSSProperties = {
  background: '#ef4444',
  color: '#fff',
}

const liveBtnInactive: React.CSSProperties = {
  background: '#fff',
  color: '#334155',
}

const liveDot: React.CSSProperties = {
  width: 6,
  height: 6,
  borderRadius: '50%',
}

const fitAllBtn: React.CSSProperties = {
  width: 34,
  height: 34,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  fontSize: '1.1rem',
  color: '#333',
  padding: 0,
  flexShrink: 0,
  marginLeft: 8,
  pointerEvents: 'auto',
}
