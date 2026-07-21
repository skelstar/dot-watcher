import { useEffect, useState } from 'react'
import { MapContainer, TileLayer, Marker, useMap, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import type { LatLon } from './lib/types'

// Leaflet measures its container's size the instant it mounts. Inside a freshly-opened modal,
// that first measurement can land before the browser has finished laying out the dialog (its
// size depends on content that includes the map itself), so tiles get positioned for the wrong
// dimensions — the classic "scattered/oversized tiles" bug. Forcing a re-measure a frame later,
// once layout has actually settled, fixes it.
function InvalidateSizeOnSettle() {
  const map = useMap()
  useEffect(() => {
    const raf = requestAnimationFrame(() => map.invalidateSize())
    return () => cancelAnimationFrame(raf)
  }, [map])
  return null
}

type Props = {
  title: string
  hint?: string
  center: LatLon
  initialValue?: LatLon | null
  pickIcon: L.DivIcon
  referencePoint?: LatLon | null
  referenceIcon?: L.DivIcon
  confirmLabel?: string
  onConfirm: (point: LatLon) => void
  onCancel: () => void
}

function ClickMarker({ position, icon, onPick }: {
  position: LatLon | null
  icon: L.DivIcon
  onPick: (lat: number, lon: number) => void
}) {
  useMapEvents({
    click(e) { onPick(e.latlng.lat, e.latlng.lng) },
  })
  return position ? <Marker position={[position.lat, position.lon]} icon={icon} /> : null
}

export default function MapPickerModal({
  title, hint, center, initialValue, pickIcon, referencePoint, referenceIcon, confirmLabel = 'Confirm', onConfirm, onCancel,
}: Props) {
  const [point, setPoint] = useState<LatLon | null>(initialValue ?? null)
  const mapCenter: [number, number] = point ? [point.lat, point.lon] : [center.lat, center.lon]

  return (
    <div style={backdrop} onClick={onCancel}>
      <div style={dialog} onClick={e => e.stopPropagation()}>
        <h3 style={dialogTitle}>{title}</h3>
        <p style={dialogHint}>{hint ?? 'Click the map to choose a point.'}</p>

        <MapContainer center={mapCenter} zoom={13} style={dialogMapStyle}>
          <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" attribution='&copy; OpenStreetMap contributors' />
          {referencePoint && referenceIcon && <Marker position={[referencePoint.lat, referencePoint.lon]} icon={referenceIcon} />}
          <ClickMarker position={point} icon={pickIcon} onPick={(lat, lon) => setPoint({ lat, lon })} />
          <InvalidateSizeOnSettle />
        </MapContainer>

        <div style={dialogActions}>
          {point && <span style={pointReadout}>{point.lat.toFixed(5)}, {point.lon.toFixed(5)}</span>}
          <span style={{ flex: 1 }} />
          <button style={cancelBtn} onClick={onCancel}>Cancel</button>
          <button style={confirmBtn(!point)} disabled={!point} onClick={() => point && onConfirm(point)}>{confirmLabel}</button>
        </div>
      </div>
    </div>
  )
}

const backdrop: React.CSSProperties = {
  position: 'fixed', inset: 0, background: 'rgba(15, 23, 42, 0.5)',
  display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000, padding: '1rem',
}
const dialog: React.CSSProperties = {
  background: '#fff', borderRadius: 10, padding: '1.25rem', width: '100%', maxWidth: 560,
  boxShadow: '0 10px 40px rgba(0,0,0,0.3)',
}
const dialogTitle: React.CSSProperties = { fontSize: '1.05rem', fontWeight: 700, marginBottom: '0.35rem' }
const dialogHint: React.CSSProperties = { fontSize: '0.82rem', color: '#64748b', marginBottom: '0.75rem' }
const dialogMapStyle: React.CSSProperties = { height: 380, borderRadius: 8, border: '1px solid #e2e8f0' }
const dialogActions: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: '0.6rem', marginTop: '0.85rem' }
const pointReadout: React.CSSProperties = { fontSize: '0.78rem', color: '#64748b', fontFamily: 'monospace' }
const cancelBtn: React.CSSProperties = {
  padding: '0.45rem 1rem', borderRadius: 6, border: '1px solid #cbd5e1', background: '#fff',
  color: '#475569', fontWeight: 600, fontSize: '0.85rem', cursor: 'pointer',
}
const confirmBtn = (disabled: boolean): React.CSSProperties => ({
  padding: '0.45rem 1.1rem', borderRadius: 6, border: 'none', fontWeight: 700, fontSize: '0.85rem',
  background: disabled ? '#e2e8f0' : '#16a34a', color: disabled ? '#94a3b8' : '#fff', cursor: disabled ? 'default' : 'pointer',
})
