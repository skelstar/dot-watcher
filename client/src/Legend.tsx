import { runnerColour } from './useRunnerMarkers.ts'

interface Props {
  runners: string[]
  onRunnerClick: (name: string) => void
  onFitAll: () => void
  belowAccountBar?: boolean
}

export default function Legend({ runners, onRunnerClick, onFitAll, belowAccountBar }: Props) {
  if (runners.length === 0) return null

  return (
    <div style={container(belowAccountBar)}>
      {runners.map(name => (
        <div key={name} style={dot(runnerColour(name))} onClick={() => onRunnerClick(name)}>
          {name}
        </div>
      ))}
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
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: 6,
    zIndex: 1,
    pointerEvents: 'auto',
  }
}

function dot(colour: string): React.CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: 32,
    height: 32,
    paddingInline: 6,
    borderRadius: 16,
    background: colour,
    border: '2px solid white',
    boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
    fontSize: 11,
    fontFamily: 'system-ui, sans-serif',
    fontWeight: 700,
    color: '#ffffff',
    whiteSpace: 'nowrap',
    cursor: 'pointer',
    pointerEvents: 'auto',
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
