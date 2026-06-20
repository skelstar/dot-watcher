import { runnerColour } from './useRunnerMarkers.ts'

interface Props {
  runners: string[]
  onRunnerClick: (name: string) => void
}

export default function Legend({ runners, onRunnerClick }: Props) {
  if (runners.length === 0) return null

  return (
    <div style={container}>
      {runners.map(name => (
        <div key={name} style={dot(runnerColour(name))} onClick={() => onRunnerClick(name)}>
          {name}
        </div>
      ))}
    </div>
  )
}

const container: React.CSSProperties = {
  position: 'absolute',
  top: 10,
  left: 10,
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
  zIndex: 1,
  pointerEvents: 'auto',
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
