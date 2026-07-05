import { useRef } from 'react'
import type { SessionTimelineState } from './useSessionTimeline.ts'

interface Props {
  timeline: SessionTimelineState
}

function formatTime(ms: number): string {
  const totalSec = Math.floor(ms / 1000)
  const h = Math.floor(totalSec / 3600)
  const m = Math.floor((totalSec % 3600) / 60)
  const s = totalSec % 60
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

export default function ReplayControls({ timeline }: Props) {
  const { following, scrubTimeMs, runStartMs, nowMs, dragTo, dragEnd, goLive } = timeline
  const trackRef = useRef<HTMLDivElement>(null)

  const rangeStart = runStartMs ?? nowMs
  const rangeEnd = nowMs
  const durationMs = Math.max(rangeEnd - rangeStart, 1)
  const currentMs = scrubTimeMs ?? nowMs
  const fraction = Math.max(0, Math.min(1, (currentMs - rangeStart) / durationMs))

  function timeFromClientX(clientX: number): number {
    const track = trackRef.current
    if (!track) return currentMs
    const rect = track.getBoundingClientRect()
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
    return rangeStart + ratio * durationMs
  }

  function handlePointerDown(event: React.PointerEvent<HTMLDivElement>) {
    event.currentTarget.setPointerCapture(event.pointerId)
    dragTo(timeFromClientX(event.clientX))
  }

  function handlePointerMove(event: React.PointerEvent<HTMLDivElement>) {
    if (event.buttons === 0) return
    dragTo(timeFromClientX(event.clientX))
  }

  function handlePointerUp() {
    dragEnd()
  }

  if (runStartMs === null) {
    return (
      <div style={bar}>
        <button onClick={goLive} style={{ ...liveBtn, ...(following ? liveBtnActive : liveBtnInactive) }} title="Go live">
          <span style={{ ...liveDot, background: following ? '#fff' : '#94a3b8' }} />
          LIVE
        </button>
      </div>
    )
  }

  return (
    <div style={bar}>
      <span style={timeLabel}>{formatTime(currentMs - rangeStart)}</span>

      <div
        ref={trackRef}
        style={track}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={handlePointerUp}
      >
        <div style={{ ...trackFill, width: `${fraction * 100}%` }} />
        <div style={{ ...dot, left: `${fraction * 100}%` }} />
      </div>

      <span style={timeLabel}>{formatTime(durationMs)}</span>

      <button onClick={goLive} style={{ ...liveBtn, ...(following ? liveBtnActive : liveBtnInactive) }} title="Go live">
        <span style={{ ...liveDot, background: following ? '#fff' : '#94a3b8' }} />
        LIVE
      </button>
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
  gap: 8,
  background: 'rgba(255,255,255,0.95)',
  borderRadius: 10,
  padding: '8px 8px',
  boxShadow: '0 2px 12px rgba(0,0,0,0.2)',
  zIndex: 10,
  maxWidth: 'calc(100vw - 32px)',
  boxSizing: 'border-box',
}

const track: React.CSSProperties = {
  position: 'relative',
  flex: 1,
  minWidth: 80,
  height: 34,
  display: 'flex',
  alignItems: 'center',
  cursor: 'pointer',
  touchAction: 'none',
}

const trackFill: React.CSSProperties = {
  position: 'absolute',
  left: 0,
  height: 3,
  borderRadius: 2,
  background: '#ef4444',
}

const dot: React.CSSProperties = {
  position: 'absolute',
  width: 14,
  height: 14,
  borderRadius: '50%',
  background: '#ef4444',
  border: '2px solid #fff',
  boxShadow: '0 0 0 1px rgba(0,0,0,0.2)',
  transform: 'translateX(-50%)',
}

const timeLabel: React.CSSProperties = {
  fontSize: 12,
  fontFamily: 'monospace',
  color: '#334155',
  flexShrink: 0,
  minWidth: 38,
  textAlign: 'center',
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
  padding: '0 12px',
}

const liveBtnActive: React.CSSProperties = {
  background: '#ef4444',
  color: '#fff',
}

const liveBtnInactive: React.CSSProperties = {
  background: '#e2e8f0',
  color: '#334155',
}

const liveDot: React.CSSProperties = {
  width: 6,
  height: 6,
  borderRadius: '50%',
}
