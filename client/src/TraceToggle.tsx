interface Props {
  on: boolean
  onToggle: () => void
}

// A small square button matching MapStyleToggle, stacked below it and the 3D terrain toggle. Turns the signal
// trace (every position up to the playhead, satellite badges and missed-read markers) on or off.
export default function TraceToggle({ on, onToggle }: Props) {
  const label = on ? 'Hide signal trace' : 'Show signal trace'

  return (
    <button
      type="button"
      onClick={onToggle}
      style={{ ...button, background: on ? '#dbeafe' : '#fff' }}
      title={label}
      aria-label={label}
      aria-pressed={on}
    >
      <TraceIcon />
    </button>
  )
}

// A dashed route with a dot at each end.
function TraceIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="2" strokeLinecap="round">
      <circle cx="5" cy="19" r="2" fill="#333" />
      <circle cx="19" cy="5" r="2" fill="#333" />
      <path d="M7 17 C 11 17, 8 7, 17 7" strokeDasharray="3 3" />
    </svg>
  )
}

const button: React.CSSProperties = {
  position: 'absolute',
  top: 225,
  right: 10,
  width: 29,
  height: 29,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  padding: 0,
  zIndex: 1,
  pointerEvents: 'auto',
}
