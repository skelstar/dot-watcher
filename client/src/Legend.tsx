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
        <div key={name} style={row} onClick={() => onRunnerClick(name)}>
          <svg width="16" height="16" viewBox="0 0 24 24" style={{ flexShrink: 0 }}>
            <path
              d="M12 2 L20 20 L12 15 L4 20 Z"
              fill={runnerColour(name)}
              stroke="white"
              strokeWidth="1.5"
              strokeLinejoin="round"
            />
          </svg>
          <span style={label}>{name}</span>
        </div>
      ))}
    </div>
  )
}

const container: React.CSSProperties = {
  position: 'absolute',
  top: 10,
  left: 10,
  background: 'rgba(255,255,255,0.92)',
  borderRadius: 8,
  padding: '6px 10px',
  display: 'flex',
  flexDirection: 'column',
  gap: 6,
  boxShadow: '0 1px 4px rgba(0,0,0,0.2)',
  zIndex: 1,
  pointerEvents: 'auto',
}

const row: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 7,
  cursor: 'pointer',
  pointerEvents: 'auto',
}

const label: React.CSSProperties = {
  fontSize: 13,
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 600,
  color: '#1e293b',
  whiteSpace: 'nowrap',
}
