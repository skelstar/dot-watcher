import { useEffect, useRef, useState } from 'react'
import type { SessionTimelineState } from './useSessionTimeline.ts'
import { formatTimeOfDay } from './useSessionTimelineLogic.ts'

interface Props {
  timeline: SessionTimelineState
}

const TIME_TOOLTIP_LINGER_MS = 800
// The tooltip is centred on the scrubber dot, so at the left end of the track it would spill off
// the screen. Keeping its centre at least this far from the track's left edge (about half the
// widest "12:34:56 PM" label) keeps it fully visible, at the cost of not being exactly over the dot
// there. No such clamp on the right: the play/skip buttons sit beyond the track's end, so a label
// centred on the dot there still lands on screen.
const TOOLTIP_LEFT_INSET_PX = 30
// Replay speeds the speed button cycles through, as multiples of real time. The first is the default.
const REPLAY_SPEEDS = [10, 60, 200]

export default function ReplayControls({ timeline }: Props) {
  const {
    following, scrubTimeMs, runStartMs, nowMs, isLive, lastActivityMs, pollIntervalMs,
    canSkip, skipping, skipToNextPosition, dragTo, dragEnd, goLive, playing, play, pause,
    speed, setSpeed,
  } = timeline
  const trackRef = useRef<HTMLDivElement>(null)
  const [showTooltip, setShowTooltip] = useState(false)
  const lingerTimerRef = useRef<ReturnType<typeof setTimeout>>()

  const rangeStart = runStartMs ?? nowMs
  // Once the session has gone stale, freeze the scrubbable range at the last real ping instead of
  // letting it ride wall-clock time forever — otherwise a 75-minute run from 4 days ago ends up
  // buried in the first sliver of a 4-day-wide bar, with everything after it "dead" replay.
  const rangeEnd = isLive ? nowMs : Math.max(lastActivityMs ?? rangeStart, rangeStart)
  const durationMs = Math.max(rangeEnd - rangeStart, 1)
  const currentMs = scrubTimeMs ?? nowMs
  const fraction = Math.max(0, Math.min(1, (currentMs - rangeStart) / durationMs))
  const trackColour = isLive ? '#ef4444' : '#64748b'

  // Time remaining until the next live-poll tick, clamped to the interval so a long gap since
  // the last successful poll (tab backgrounded, network blip) doesn't show a negative countdown.
  const secondsToNextUpdate = lastActivityMs === null
    ? null
    : Math.max(0, Math.ceil((pollIntervalMs - ((nowMs - lastActivityMs) % pollIntervalMs)) / 1000))

  useEffect(() => () => clearTimeout(lingerTimerRef.current), [])

  function timeFromClientX(clientX: number): number {
    const track = trackRef.current
    if (!track) return currentMs
    const rect = track.getBoundingClientRect()
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width))
    return rangeStart + ratio * durationMs
  }

  function cycleSpeed() {
    const index = REPLAY_SPEEDS.indexOf(speed)
    setSpeed(REPLAY_SPEEDS[(index + 1) % REPLAY_SPEEDS.length])
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
          <div style={{ ...trackFill, width: `${fraction * 100}%`, background: trackColour }} />
          {(showTooltip || fraction < 1) && (
            <span style={{ ...timeTooltip, left: `max(${TOOLTIP_LEFT_INSET_PX}px, ${fraction * 100}%)` }}>
              {formatTimeOfDay(currentMs)}
            </span>
          )}
          <div style={{ ...dot, left: `${fraction * 100}%`, background: trackColour }} />
        </div>
      )}

      {/* Finished sessions have no live edge to jump to, so instead of a duration badge this is a
          play/pause toggle, the same size as the skip button beside it. Live sessions keep the
          LIVE button below, which is also their only way back to the live edge. */}
      {runStartMs !== null && !isLive && (
        <button
          onClick={playing ? pause : play}
          style={{ ...skipBtn, marginLeft: 8 }}
          title={playing ? 'Pause' : 'Play'}
          aria-label={playing ? 'Pause' : 'Play'}
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            {playing
              ? <><rect x="6" y="5" width="4" height="14" rx="1" /><rect x="14" y="5" width="4" height="14" rx="1" /></>
              : <path d="M7 4l13 8-13 8V4z" />}
          </svg>
        </button>
      )}

      {/* Cycles the replay speed; only meaningful alongside the play button. */}
      {runStartMs !== null && !isLive && (
        <button
          onClick={cycleSpeed}
          style={{ ...skipBtn, marginLeft: 8, padding: '0 8px', fontSize: 12, fontWeight: 700, fontFamily: 'system-ui, sans-serif' }}
          title={`Replay speed ${speed}× (tap to change)`}
          aria-label={`Replay speed ${speed} times, tap to change`}
        >
          {speed}×
        </button>
      )}

      {/* Right next to the play/pause (or LIVE) button, at the bottom edge where a thumb already is, and
          always rendered (just dimmed when there's nothing ahead) so the track doesn't resize as
          you scrub. */}
      {runStartMs !== null && (
        <button
          onClick={skipToNextPosition}
          disabled={!canSkip || skipping}
          style={{ ...skipBtn, marginLeft: isLive ? 13 : 8, opacity: canSkip && !skipping ? 1 : 0.4 }}
          title="Skip to next position"
          aria-label="Skip to next position"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M5 5l9 7-9 7V5z" />
            <rect x="16" y="5" width="3" height="14" rx="1" />
          </svg>
        </button>
      )}

      {isLive && (
        <button
          onClick={goLive}
          style={{ ...liveBtn, ...(following ? liveBtnActive : liveBtnDimmed) }}
          title={following ? 'Live' : 'Jump to most recent position'}
        >
          <span style={{ ...liveDot, background: following ? '#fff' : '#ef4444' }} />
          LIVE
          {following && secondsToNextUpdate !== null && (
            <span style={countdownBadge}>{secondsToNextUpdate}s</span>
          )}
        </button>
      )}

    </div>
  )
}

const bar: React.CSSProperties = {
  position: 'absolute',
  bottom: 50,
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
  marginLeft: 8,
  padding: '0 12px',
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  pointerEvents: 'auto',
}

const skipBtn: React.CSSProperties = {
  height: 34,
  minWidth: 34,
  border: 'none',
  borderRadius: 6,
  background: '#fff',
  color: '#334155',
  cursor: 'pointer',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  marginLeft: 13,
  padding: 0,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  pointerEvents: 'auto',
}

const liveBtnActive: React.CSSProperties = {
  background: '#ef4444',
  color: '#fff',
}

const liveBtnDimmed: React.CSSProperties = {
  background: '#fff',
  color: '#ef4444',
}

const liveDot: React.CSSProperties = {
  width: 6,
  height: 6,
  borderRadius: '50%',
}

const countdownBadge: React.CSSProperties = {
  fontVariantNumeric: 'tabular-nums',
  background: 'rgba(255,255,255,0.22)',
  borderRadius: 4,
  padding: '1px 5px',
  fontSize: 11,
  fontWeight: 700,
  letterSpacing: 0,
  minWidth: 16,
  textAlign: 'center',
}
