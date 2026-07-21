import { runnerColour } from './useRunnerMarkers.ts'

interface Props {
  runners: string[]
  onRunnerClick: (name: string) => void
  onFitAll: () => void
  belowAccountBar?: boolean
  runnersWithGap?: Set<string>
  runnersSleeping?: Set<string>
  runnersWithGpsSignalLoss?: Set<string>
}

export default function Legend({
  runners,
  onRunnerClick,
  onFitAll,
  belowAccountBar,
  runnersWithGap = new Set(),
  runnersSleeping = new Set(),
  runnersWithGpsSignalLoss = new Set(),
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
        const message = missing
          ? 'Missing location'
          : signalLoss
          ? 'Poor GPS signal'
          : sleeping
          ? 'Sleeping'
          : null

        return (
          <div key={name} style={row}>
            <div style={{ position: 'relative' }}>
              <div style={dot(runnerColour(name), missing, sleeping)} onClick={() => onRunnerClick(name)}>
                {name}
              </div>
              {signalLoss && <div style={signalLossBadge}>!</div>}
            </div>
            {message && <span style={issueLabel(missing)}>{message}</span>}
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

function issueLabel(missing: boolean): React.CSSProperties {
  return {
    fontSize: '0.8rem',
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 600,
    color: missing ? '#57606a' : '#dc2626',
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
