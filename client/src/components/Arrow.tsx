// Chevron-satellite marker (concept 1d)
// The dot stays upright at all times — only the chevron orbits to show heading.

// Circle centre in SVG/element coordinates (element anchors at its centre)
const CX = 24
const CY = 24

const MARKER_W = 48
const MARKER_H = 48

// Legacy export — useRunnerMarkers imports this but only uses it for label offset
const ARROW_SIZE = MARKER_W

interface Props {
  name: string
  heading: number | null
  colour: string
  label?: string  // overrides display name; '' hides the label
  stationary?: boolean
  onClick?: () => void
}

export { ARROW_SIZE }

export default function Arrow({ name, heading, colour, label, stationary, onClick }: Props) {
  const displayLabel = label !== undefined ? label : name
  // Only show the label when it's a cluster label (multiple runners merged)
  const showLabel = displayLabel !== '' && displayLabel !== name

  if (stationary) {
    return (
      <div style={{ position: 'relative', width: 28, height: 28 }} onClick={onClick}>
        <div className="dot-sleep" style={{
          width: 28,
          height: 28,
          borderRadius: '50%',
          background: '#ffffff',
          border: `2px solid ${colour}`,
          boxShadow: '0 1px 4px rgba(0,0,0,0.45)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontFamily: 'system-ui, sans-serif',
          fontWeight: 700,
          color: colour,
          cursor: onClick ? 'pointer' : undefined,
        }}>
          {name}
        </div>
        {showLabel && (
          <div style={{
            position: 'absolute',
            top: 32,
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
    <div style={{ position: 'relative', width: MARKER_W, height: MARKER_H, cursor: onClick ? 'pointer' : undefined }} onClick={onClick}>
      <svg
        width={MARKER_W}
        height={MARKER_H}
        viewBox={`0 0 ${MARKER_W} ${MARKER_H}`}
        style={{ display: 'block', overflow: 'visible' }}
      >
        {/* Upright dot — never rotates, so ring and initials stay crisp */}
        <g style={{ filter: 'drop-shadow(0 1px 4px rgba(0,0,0,0.45))' }}>
          <circle cx={CX} cy={CY} r={12} fill={colour} stroke="#ffffff" strokeWidth="3" />
        </g>
        <text
          x={CX}
          y={CY + 4}
          fontFamily="Arial, 'Helvetica Neue', sans-serif"
          fontWeight="bold"
          fontSize="11"
          fill="#ffffff"
          textAnchor="middle"
        >
          {name}
        </text>

        {/* Orbiting chevron — the only part that rotates with heading. Rendered after the dot
            so its shadow isn't hidden underneath the dot's own shadow/fill. */}
        {heading !== null && (
          <g
            transform={`rotate(${heading}, ${CX}, ${CY})`}
            style={{ filter: 'drop-shadow(0 1px 2px rgba(0,0,0,0.4))' }}
          >
            <path
              d={`M ${CX - 6.5},11 L ${CX},4.5 L ${CX + 6.5},11`}
              fill="none"
              stroke="#ffffff"
              strokeWidth="4"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </g>
        )}
      </svg>

      {showLabel && (
        <div style={{
          position: 'absolute',
          top: CY + 18,
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
