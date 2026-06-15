const ARROW_SIZE = 32

interface Props {
  name: string
  heading: number | null
  colour: string
  label?: string  // overrides display name; '' hides the label
  stationary?: boolean
}

export { ARROW_SIZE }

export default function Arrow({ name, heading, colour, label, stationary }: Props) {
  const displayLabel = label !== undefined ? label : name
  if (stationary) {
    return (
      <div style={{ position: 'relative', width: 20, height: 20 }}>
        <div style={{
          width: 20,
          height: 20,
          borderRadius: '50%',
          background: colour,
          border: '1.5px solid white',
          boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
          opacity: 0.6,
        }} />
        {displayLabel && (
          <div style={{
            position: 'absolute',
            top: 24,
            left: '50%',
            transform: 'translateX(-50%)',
            fontSize: 11,
            fontFamily: 'system-ui, sans-serif',
            fontWeight: 600,
            color: '#1e293b',
            background: 'rgba(255,255,255,0.85)',
            padding: '1px 5px',
            borderRadius: 4,
            whiteSpace: 'nowrap',
            boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
          }}>
            {displayLabel}
          </div>
        )}
      </div>
    )
  }

  return (
    <div style={{ position: 'relative', width: ARROW_SIZE, height: ARROW_SIZE }}>
      <svg
        width={ARROW_SIZE}
        height={ARROW_SIZE}
        viewBox="0 0 24 24"
        style={{
          display: 'block',
          filter: 'drop-shadow(0 1px 3px rgba(0,0,0,0.35))',
          transform: `rotate(${heading ?? 0}deg)`,
          transformOrigin: '50% 50%',
        }}
      >
        <path
          d="M12 2 L20 20 L12 15 L4 20 Z"
          fill={colour}
          stroke="white"
          strokeWidth="1.5"
          strokeLinejoin="round"
        />
      </svg>

      {displayLabel && (
        <div style={{
          position: 'absolute',
          top: ARROW_SIZE + 4,
          left: '50%',
          transform: 'translateX(-50%)',
          fontSize: 11,
          fontFamily: 'system-ui, sans-serif',
          fontWeight: 600,
          color: '#1e293b',
          background: 'rgba(255,255,255,0.85)',
          padding: '1px 5px',
          borderRadius: 4,
          whiteSpace: 'nowrap',
          boxShadow: '0 1px 3px rgba(0,0,0,0.2)',
        }}>
          {displayLabel}
        </div>
      )}

    </div>
  )
}
