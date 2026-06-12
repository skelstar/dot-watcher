const ARROW_SIZE = 32
const CROSS_SIZE = 12

interface Props {
  name: string
  heading: number | null
  colour: string
  label?: string  // overrides display name; '' hides the label
}

export { ARROW_SIZE }

export default function Arrow({ name, heading, colour, label }: Props) {
  const displayLabel = label !== undefined ? label : name
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

      <svg
        width={CROSS_SIZE}
        height={CROSS_SIZE}
        viewBox={`0 0 ${CROSS_SIZE} ${CROSS_SIZE}`}
        style={{
          position: 'absolute',
          top: ARROW_SIZE / 2,
          left: '50%',
          transform: 'translate(-50%, -50%)',
          pointerEvents: 'none',
        }}
      >
        <line x1="0" y1={CROSS_SIZE / 2} x2={CROSS_SIZE} y2={CROSS_SIZE / 2} stroke="red" strokeWidth="2" />
        <line x1={CROSS_SIZE / 2} y1="0" x2={CROSS_SIZE / 2} y2={CROSS_SIZE} stroke="red" strokeWidth="2" />
      </svg>
    </div>
  )
}
