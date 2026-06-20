// Circle centre of the marker shape in SVG path coordinates
const CX = 11.589949
const CY = 20.171708

// ViewBox sized so (CX, CY) is exactly at the element's 50% 50%,
// giving enough room for the tip above and the circle below (plus stroke padding)
const VB_W = 30
const VB_H = 40
const VB_X = CX - VB_W / 2   // -3.410051
const VB_Y = CY - VB_H / 2   //  0.171708

const MARKER_W = VB_W
const MARKER_H = VB_H

// Legacy export — useRunnerMarkers imports this but only uses it for label offset
const ARROW_SIZE = MARKER_W

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
  // Only show the label when it's a cluster label (multiple runners merged)
  const showLabel = displayLabel !== '' && displayLabel !== name
  const h = heading ?? 0

  if (stationary) {
    return (
      <div style={{ position: 'relative', width: 28, height: 28 }}>
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
    <div style={{ position: 'relative', width: MARKER_W, height: MARKER_H }}>
      <svg
        width={MARKER_W}
        height={MARKER_H}
        viewBox={`${VB_X} ${VB_Y} ${VB_W} ${VB_H}`}
        style={{
          display: 'block',
          filter: 'drop-shadow(0 1px 4px rgba(0,0,0,0.45))',
          transform: `rotate(${h}deg)`,
          transformOrigin: '50% 50%',
        }}
      >
        <path
          d="m 2.397561,10.97932 a 13,13 0 0 0 0,18.384776 13,13 0 0 0 18.384776,0 13,13 0 0 0 0,-18.384776 L 11.589949,1.7869317 Z"
          fill="#ffffff"
          stroke="#ffffff"
          strokeWidth="1.6"
        />
        <circle cx={CX} cy={CY} r={12} fill={colour} />
        <text
          x={CX}
          y={CY + 4}
          fontFamily="Arial, 'Helvetica Neue', sans-serif"
          fontWeight="bold"
          fontSize="11"
          fill="#ffffff"
          textAnchor="middle"
          transform={`rotate(${-h}, ${CX}, ${CY})`}
        >
          {name}
        </text>
      </svg>

      {showLabel && (
        <div style={{
          position: 'absolute',
          top: MARKER_H + 4,
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
