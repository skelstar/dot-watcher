import { Fragment, useEffect } from 'react'
import { MapContainer, TileLayer, Marker, Polyline, useMap } from 'react-leaflet'
import 'leaflet/dist/leaflet.css'
import { convergenceIcon, phoneMarkerIcon, runnerColour } from './lib/icons'
import type { LatLon, PhoneSnapshot } from './lib/types'

const WELLINGTON: LatLon = { lat: -41.2865, lon: 174.7762 }

// Same re-measure-after-layout-settles fix as MapPickerModal — this map sits in a normal (not
// modal) panel, but it's still laid out by its flex/grid parent, so the same first-paint sizing
// race can happen.
function InvalidateSizeOnSettle() {
  const map = useMap()
  useEffect(() => {
    const raf = requestAnimationFrame(() => map.invalidateSize())
    return () => cancelAnimationFrame(raf)
  }, [map])
  return null
}

type Props = {
  convergencePoint: LatLon | null
  phones: PhoneSnapshot[]
  routes: Record<number, LatLon[]>
}

// A persistent (non-modal) overview of the whole simulated scene: the convergence point, every
// phone's current position, and any GPX route it's following — each phone in its own colour, the
// same hash-based palette used for its card border and map marker elsewhere in this tool. Doesn't
// itself support picking points; that stays on MapPickerModal, this is read-only context.
export default function SimulatorMap({ convergencePoint, phones, routes }: Props) {
  const center: [number, number] = convergencePoint
    ? [convergencePoint.lat, convergencePoint.lon]
    : [WELLINGTON.lat, WELLINGTON.lon]

  return (
    <MapContainer center={center} zoom={13} style={mapStyle}>
      <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" attribution='&copy; OpenStreetMap contributors' />
      <InvalidateSizeOnSettle />
      {convergencePoint && <Marker position={[convergencePoint.lat, convergencePoint.lon]} icon={convergenceIcon()} />}
      {phones.map(phone => {
        const route = routes[phone.id] ?? null
        const color = runnerColour(phone.displayName)
        return (
          <Fragment key={phone.id}>
            {route && route.length > 1 && (
              <Polyline positions={route.map(p => [p.lat, p.lon])} pathOptions={{ color, weight: 4, opacity: 0.85 }} />
            )}
            {phone.position && (
              <Marker
                position={[phone.position.lat, phone.position.lon]}
                icon={phoneMarkerIcon({ ...phone, convergencePoint, route })}
              />
            )}
          </Fragment>
        )
      })}
    </MapContainer>
  )
}

const mapStyle: React.CSSProperties = { height: 360, width: '100%', border: '1px solid #e2e8f0', borderRadius: 8 }
