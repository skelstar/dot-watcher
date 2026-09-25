interface Props {
  enabled: boolean
  onToggle: () => void
}

// Experimental 3D terrain toggle, stacked directly below MapStyleToggle in the top-right column.
export default function Terrain3DToggle({ enabled, onToggle }: Props) {
  const label = enabled ? 'Switch to flat 2D map' : 'Switch to 3D terrain'

  return (
    <button
      type="button"
      onClick={onToggle}
      style={{ ...button, background: enabled ? '#fc4c02' : '#fff' }}
      title={label}
      aria-label={label}
      aria-pressed={enabled}
    >
      <MountainIcon color={enabled ? '#fff' : '#333'} />
    </button>
  )
}

function MountainIcon({ color }: { color: string }) {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2" strokeLinejoin="round">
      <polyline points="2,20 9,6 14,14 17,10 22,20 2,20" />
    </svg>
  )
}

const button: React.CSSProperties = {
  position: 'absolute',
  top: 230,
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
