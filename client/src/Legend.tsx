import { runnerColour } from './useRunnerMarkers.ts'
import { formatCountdownSeconds, type RunnerCountdown } from './useSessionTimelineLogic.ts'

interface Props {
  runners: string[]
  onRunnerClick: (name: string) => void
  onFitAll: () => void
  belowAccountBar?: boolean
  runnersWithGap?: Set<string>
  runnersSleeping?: Set<string>
  runnersWithGpsSignalLoss?: Set<string>
  runnerCountdowns?: Map<string, RunnerCountdown>
}

const COUNTDOWN_RING_SIZE = 18

// Mirrors iOS's PostCountdownRing (PostCountdownRing.swift): a ring whose pie-slice fill grows
// clockwise from 12 o'clock as time elapses since the last post, emptying back to nothing the
// moment a fresh position (and next countdown) arrives. Built with conic-gradient rather than SVG
// arc math — "fraction of the circle filled" needs no path/trig here, just a CSS gradient. Paired
// with a plain numeric countdown alongside it (too small at 18px to fit text inside the ring itself).
function CountdownRing({ countdown }: { countdown: RunnerCountdown }) {
  const elapsedFraction = countdown.intervalMs > 0
    ? 1 - Math.max(0, Math.min(1, countdown.remainingMs / countdown.intervalMs))
    : 1
  const label = countdown.status === 'counting-down' ? formatCountdownSeconds(countdown.remainingMs) : 'now'
  return (
    <>
      <div
        style={{
          width: COUNTDOWN_RING_SIZE,
          height: COUNTDOWN_RING_SIZE,
          borderRadius: '50%',
          border: '1.5px solid #9ca3af',
          background: elapsedFraction <= 0
            ? 'transparent'
            : `conic-gradient(#6b7280 ${elapsedFraction * 360}deg, transparent ${elapsedFraction * 360}deg)`,
          flexShrink: 0,
        }}
      />
      <span style={countdownText}>{label}</span>
    </>
  )
}

export default function Legend({
  runners,
  onRunnerClick,
  onFitAll,
  belowAccountBar,
  runnersWithGap = new Set(),
  runnersSleeping = new Set(),
  runnersWithGpsSignalLoss = new Set(),
  runnerCountdowns = new Map(),
}: Props) {
  if (runners.length === 0) return null

  return (
    <div style={container(belowAccountBar)}>
      {runners.map(name => {
        // Mirrors the map marker's own state precedence (see Arrow.tsx / useRunnerMarkers.ts):
        // a runner is exactly one of missing/sleeping/normal, with signal-loss as a separate
        // overlay that can combine with either.
        const missing = runnersWithGap.has(name)
        const sleeping = !missing && runnersSleeping.has(name)
        const signalLoss = runnersWithGpsSignalLoss.has(name)
        // Countdown only ever shown for an actively-reporting, non-missing runner (per the plan:
        // "Only show the live countdown for actively-reporting runners") — a runner already
        // flagged missing shows that instead, not a stale/contradictory countdown alongside it.
        const countdown = !missing ? runnerCountdowns.get(name) : undefined
        const message = missing ? 'Missing location' : signalLoss ? 'Poor GPS signal' : sleeping ? 'Sleeping' : null
        const tone: LabelTone = missing ? 'muted' : 'warning'
        const showCountdown = !message && countdown
        const title = showCountdown
          ? countdown.status === 'counting-down'
            ? `Next update in ${formatCountdownSeconds(countdown.remainingMs)}`
            : 'Update due any moment'
          : undefined

        return (
          <div key={name} style={row}>
            {/* Dot, ring and countdown number all share one pill when a countdown is showing, so
                the ring reads as clearly attached to its runner rather than a separate chip. */}
            <div style={pill(showCountdown)} title={title}>
              <div style={{ position: 'relative' }}>
                <div style={dot(runnerColour(name), missing, sleeping)} onClick={() => onRunnerClick(name)}>
                  {name}
                </div>
                {signalLoss && <div style={signalLossBadge}>!</div>}
              </div>
              {showCountdown && <CountdownRing countdown={countdown} />}
            </div>
            {message && <span style={issueLabel(tone)}>{message}</span>}
          </div>
        )
      })}
      <button onClick={onFitAll} style={fitAllBtn} title="Fit all">⤢</button>
    </div>
  )
}

function container(belowAccountBar?: boolean): React.CSSProperties {
  return {
    position: 'absolute',
    top: belowAccountBar ? 58 : 12,
    left: 12,
    // Leaves a gutter clear of Mapbox's top-right NavigationControl (zoom +/-, compass), which
    // would otherwise sit at the same right edge and visually/z-index-cover our own button.
    right: 56,
    maxHeight: 'calc(100vh - 24px)',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: 6,
    zIndex: 1,
    pointerEvents: 'auto',
  }
}

const row: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
}

// Wraps the dot (and, while a countdown is live, the ring + number) in one pill so they read as
// a single unit. Only gets the translucent-white background/padding when a countdown is actually
// showing — otherwise the dot sits bare, same as before this existed.
function pill(active: boolean | undefined): React.CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: active ? 4 : 0,
    background: active ? 'rgba(255,255,255,0.5)' : 'transparent',
    borderRadius: 20,
    padding: active ? '3px 8px 3px 3px' : 0,
    boxShadow: active ? '0 1px 3px rgba(0,0,0,0.2)' : 'none',
  }
}

function dot(colour: string, missing: boolean, sleeping: boolean): React.CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 32,
    height: 32,
    paddingInline: 6,
    borderRadius: 16,
    background: missing || sleeping ? '#ffffff' : colour,
    border: missing ? `2px dashed ${colour}` : `2px solid ${colour}`,
    boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
    fontSize: 11,
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 700,
    color: missing || sleeping ? colour : '#ffffff',
    whiteSpace: 'nowrap',
    cursor: 'pointer',
    pointerEvents: 'auto',
  }
}

const signalLossBadge: React.CSSProperties = {
  position: 'absolute',
  bottom: -2,
  right: -2,
  width: 12,
  height: 12,
  borderRadius: '50%',
  background: '#dc2626',
  border: '1.5px solid #ffffff',
  boxShadow: '0 1px 2px rgba(0,0,0,0.4)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  fontSize: 8,
  fontFamily: 'Arial, sans-serif',
  fontWeight: 700,
  color: '#ffffff',
  lineHeight: 1,
  pointerEvents: 'none',
}

const countdownText: React.CSSProperties = {
  fontSize: '0.8rem',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 600,
  fontVariantNumeric: 'tabular-nums',
  color: '#0f172a',
  whiteSpace: 'nowrap',
}

type LabelTone = 'muted' | 'warning'

function issueLabel(tone: LabelTone): React.CSSProperties {
  return {
    fontSize: '0.8rem',
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 600,
    color: tone === 'warning' ? '#dc2626' : '#57606a',
    background: 'rgba(255,255,255,0.92)',
    borderRadius: 4,
    padding: '3px 6px',
    boxShadow: '0 1px 3px rgba(0,0,0,0.25)',
    whiteSpace: 'nowrap',
  }
}

const fitAllBtn: React.CSSProperties = {
  width: 32,
  height: 32,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 16,
  boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
  cursor: 'pointer',
  fontSize: '1.1rem',
  color: '#333',
  padding: 0,
  flexShrink: 0,
  pointerEvents: 'auto',
}
