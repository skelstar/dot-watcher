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
        // Countdown wins over GPS/sleeping when both apply: an adaptive (satellite-cadence)
        // countdown is the more actionable state, and a stationary runner's heading naturally
        // reads as GPS signal-loss (CoreLocation reports no course when not moving) — without
        // this, that false-positive "GPS" badge would permanently mask the countdown any time
        // the runner is standing still, which is exactly when someone wants to watch the
        // countdown to verify satellite mode is working.
        // Signal-loss gets the short "GPS" label rather than a full sentence — the "!" badge
        // beside it already carries the warning, so the text only needs to name what's wrong,
        // not restate that something is.
        const message = missing
          ? 'Missing location'
          : countdown
          ? null
          : signalLoss
          ? 'GPS'
          : sleeping
          ? 'Sleeping'
          : null
        const tone: LabelTone = missing ? 'muted' : 'warning'
        const showCountdown = !message && countdown
        const active = showCountdown || message // either fills the pill — never both at once
        const title = showCountdown
          ? countdown.status === 'counting-down'
            ? `Next update in ${formatCountdownSeconds(countdown.remainingMs)}`
            : 'Update due any moment'
          : undefined

        // "Missing location"/"Sleeping" are long enough to size themselves; only the short
        // signal-loss label ("GPS") gets padded out to match the countdown pill's usual width.
        const trailingMinWidth = showCountdown || signalLoss ? PILL_CONTENT_MIN_WIDTH : undefined

        return (
          <div key={name} style={row}>
            {/* Dot plus whichever single trailing indicator applies (countdown ring+number, or a
                short status label) share one pill so they read as one unit and match widths. */}
            <div style={pill(active)} title={title}>
              <div
                style={dot(runnerColour(name), missing, sleeping)}
                onClick={() => onRunnerClick(name)}
              >
                {name}
              </div>
              {active && (
                <span style={trailingContent(trailingMinWidth)}>
                  {showCountdown && <CountdownRing countdown={countdown} />}
                  {message && (
                    <span style={issueLabel(tone)}>
                      {/* Same red "!" the map marker's own signal-loss badge uses (Arrow.tsx) —
                          inline here rather than a sentence, so the label reads as "⚠ GPS" not
                          prose. */}
                      {signalLoss && <span style={inlineWarningBadge}>!</span>}
                      {message}
                    </span>
                  )}
                </span>
              )}
            </div>
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

// Minimum width for the pill's trailing content (countdown or status label) once it's active, so
// a short one ("GPS", "5s") and a longer one ("1:30") still land on close to the same overall
// pill width instead of each hugging its own text.
const PILL_CONTENT_MIN_WIDTH = 34

function trailingContent(minWidth: number | undefined): React.CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: 4,
    minWidth,
  }
}

// Wraps the dot plus whichever single trailing indicator applies (countdown ring+number, or a
// status label like "GPS"/"Sleeping") in one pill so they read as a unit. Only gets the
// translucent-white background/padding when something's actually showing — otherwise the dot
// sits bare, same as before this existed.
function pill(active: boolean | string | undefined): React.CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: active ? 4 : 0,
    background: active ? 'rgba(255,255,255,0.6)' : 'transparent',
    borderRadius: 20,
    padding: active ? '1px 8px 1px 1px' : 0,
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

// The runner's GPS-signal-loss warning — a small red "!" badge, inline just before "GPS" in the
// label. No longer duplicated on the dot's corner (previously signalLossBadge, since removed):
// one indicator in the pill is enough, and keeps the dot itself reading purely as "who".
const inlineWarningBadge: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 12,
  height: 12,
  borderRadius: '50%',
  background: '#dc2626',
  fontSize: 8,
  fontFamily: 'Arial, sans-serif',
  fontWeight: 700,
  color: '#ffffff',
  lineHeight: 1,
  marginRight: 4,
  flexShrink: 0,
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

// Nested inside the shared pill (see `pill()`), which already supplies the background/shadow —
// this only needs its own type styling.
function issueLabel(tone: LabelTone): React.CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    fontSize: '0.8rem',
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 600,
    color: tone === 'warning' ? '#dc2626' : '#57606a',
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
