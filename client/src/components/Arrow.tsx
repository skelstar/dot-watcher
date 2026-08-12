// Chevron-satellite marker (concept 1d)
// The dot stays upright at all times — only the chevron orbits to show heading.

import { initialsFor } from './InitialsBadge.tsx'

// Circle centre in SVG/element coordinates (element anchors at its centre)
const CX = 24
const CY = 24

const MARKER_W = 48
const MARKER_H = 48

// Legacy export — useRunnerMarkers imports this but only uses it for label offset
const ARROW_SIZE = MARKER_W

// The heading chevron's position/size, all measured as distance from the dot's centre (CX, CY)
// before the rotate() transform is applied. Play with these to see what reads best:
//   - CHEVRON_BASE_OFFSET: how far out the chevron's two base points sit — raise this to push
//     the whole chevron further from the dot; lower it to tuck it in closer/overlapping.
//   - CHEVRON_TIP_OFFSET: how far out the tip sits — raise/lower together with BASE_OFFSET to
//     move the chevron without changing its length, or independently to change how long it is.
//   - CHEVRON_HALF_WIDTH: half the wing span (how wide the chevron's "V" opens).
const CHEVRON_BASE_OFFSET = 15.5
const CHEVRON_TIP_OFFSET = 22
const CHEVRON_HALF_WIDTH = 6.5

interface Props {
  name: string
  heading: number | null
  colour: string
  label?: string  // overrides display name; '' hides the label
  stationary?: boolean
  missing?: boolean
  signalLoss?: boolean
  onClick?: () => void
}

export { ARROW_SIZE }

// Small red exclamation badge, bottom-right of the dot, flagging that this runner's position may
// currently be unreliable (see findGpsSignalLoss in useSessionTimelineLogic.ts).
function SignalLossBadge({ cx, cy, r }: { cx: number; cy: number; r: number }) {
  return (
    <g style={{ filter: 'drop-shadow(0 1px 2px rgba(0,0,0,0.4))' }}>
      <circle cx={cx} cy={cy} r={r} fill="#dc2626" stroke="#ffffff" strokeWidth="1.5" />
      <text
        x={cx}
        y={cy + r * 0.38}
        fontFamily="Arial, 'Helvetica Neue', sans-serif"
        fontWeight="bold"
        fontSize={r * 1.3}
        fill="#ffffff"
        textAnchor="middle"
      >
        !
      </text>
    </g>
  )
}

export default function Arrow({ name, heading, colour, label, stationary, missing, signalLoss, onClick }: Props) {
  const displayLabel = label !== undefined ? label : name
  // Only show the label when it's a cluster label (multiple runners merged)
  const showLabel = displayLabel !== '' && displayLabel !== name

  // No position at the current playhead despite the runner having reported both before and
  // after it (see findRunnersWithGap) — a real gap in the track, not just an old-but-valid fix.
  // Drawn hollow/dashed rather than filled so it reads as "unknown right now", not as data.
  if (missing) {
    return (
      <div style={{ position: 'relative', width: 28, height: 28 }} onClick={onClick}>
        <div style={{
          width: 28,
          height: 28,
          borderRadius: '50%',
          background: '#ffffff',
          border: `2px dashed ${colour}`,
          boxShadow: '0 1px 4px rgba(0,0,0,0.45)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 10,
          fontFamily: 'system-ui, sans-serif',
          fontWeight: 700,
          color: colour,
          cursor: onClick ? 'pointer' : undefined,
        }}>
          {initialsFor(name)}
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
        {signalLoss && (
          <div style={{
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
          }}>
            !
          </div>
        )}
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
          <circle cx={CX} cy={CY} r={13.5} fill={colour} />
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
              d={`M ${CX - CHEVRON_HALF_WIDTH},${CY - CHEVRON_BASE_OFFSET} L ${CX},${CY - CHEVRON_TIP_OFFSET} L ${CX + CHEVRON_HALF_WIDTH},${CY - CHEVRON_BASE_OFFSET}`}
              fill="none"
              stroke= "#ffffff" // { colour }
              strokeWidth="4"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </g>
        )}

        {/* Bottom-right badge flagging unreliable GPS — rendered last so it sits above the
            chevron if the two ever overlap. */}
        {signalLoss && <SignalLossBadge cx={CX + 8.5} cy={CY + 8.5} r={6} />}
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
