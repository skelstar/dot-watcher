import type { MapStyleId } from './map/mapStyle.ts'
import { MAP_STYLES, otherMapStyleId } from './map/mapStyle.ts'

interface Props {
  styleId: MapStyleId
  onToggle: (next: MapStyleId) => void
}

// A small square button matching LegendHelp's "?" button, stacked directly below it — both sit
// under MapLibre's top-right NavigationControl/GeolocateControl column (see LegendHelp.tsx).
// Shows the style you'd switch *to*, not the one currently active.
export default function MapStyleToggle({ styleId, onToggle }: Props) {
  const nextId = otherMapStyleId(styleId)
  const nextLabel = MAP_STYLES[nextId].label

  return (
    <button
      type="button"
      onClick={() => onToggle(nextId)}
      style={button}
      title={`Switch to ${nextLabel} map`}
      aria-label={`Switch to ${nextLabel} map`}
    >
      <LayersIcon />
    </button>
  )
}

function LayersIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="2" strokeLinejoin="round">
      <polygon points="12,3 22,9 12,15 2,9" />
      <polyline points="2,14 12,20 22,14" />
    </svg>
  )
}

const button: React.CSSProperties = {
  position: 'absolute',
  top: 195,
  right: 10,
  width: 29,
  height: 29,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  padding: 0,
  zIndex: 1,
  pointerEvents: 'auto',
}
