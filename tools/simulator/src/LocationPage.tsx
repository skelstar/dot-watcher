import { useState, useEffect, useRef, useCallback } from 'react'
import { MapContainer, TileLayer, Marker, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import markerIcon2x from 'leaflet/dist/images/marker-icon-2x.png'
import markerIcon from 'leaflet/dist/images/marker-icon.png'
import markerShadow from 'leaflet/dist/images/marker-shadow.png'

delete (L.Icon.Default.prototype as unknown as Record<string, unknown>)._getIconUrl
L.Icon.Default.mergeOptions({ iconRetinaUrl: markerIcon2x, iconUrl: markerIcon, shadowUrl: markerShadow })

type Simulator = { udid: string; name: string; runtime: string }
type LatLon = { lat: number; lon: number }

function ClickMarker({ onPick }: { onPick: (lat: number, lon: number) => void }) {
  const [pos, setPos] = useState<[number, number] | null>(null)
  // Keep a ref so the map event listener always calls the latest onPick,
  // even if useMapEvents captures a stale closure on first mount.
  const onPickRef = useRef(onPick)
  useEffect(() => { onPickRef.current = onPick })
  useMapEvents({
    click(e) {
      setPos([e.latlng.lat, e.latlng.lng])
      onPickRef.current(e.latlng.lat, e.latlng.lng)
    },
  })
  return pos ? <Marker position={pos} /> : null
}

export default function LocationPage() {
  const [simulators, setSimulators] = useState<Simulator[]>([])
  const [selectedUdid, setSelectedUdid] = useState('')
  const [status, setStatus] = useState<'idle' | 'setting' | 'set' | 'error'>('idle')
  const [lastLoc, setLastLoc] = useState<LatLon | null>(null)

  // Keep selectedUdid in a ref so handlePick (passed as stable callback) always sees the latest value.
  const selectedUdidRef = useRef(selectedUdid)
  useEffect(() => { selectedUdidRef.current = selectedUdid }, [selectedUdid])

  useEffect(() => {
    fetch('/api/simulators')
      .then(r => r.json())
      .then((data: Simulator[]) => {
        setSimulators(data)
        if (data.length > 0) setSelectedUdid(data[0].udid)
      })
      .catch(() => setSimulators([]))
  }, [])

  const handlePick = useCallback(async (lat: number, lon: number) => {
    const udid = selectedUdidRef.current
    if (!udid) return
    setStatus('setting')
    setLastLoc({ lat, lon })
    try {
      const res = await fetch(`/api/simulators/${udid}/location`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ lat, lon }),
      })
      setStatus(res.ok ? 'set' : 'error')
    } catch {
      setStatus('error')
    }
  }, [])

  return (
    <div>
      <div style={row}>
        <label style={label}>Simulator</label>
        <select value={selectedUdid} onChange={e => { setSelectedUdid(e.target.value); setStatus('idle') }} style={select}>
          {simulators.length === 0
            ? <option>No booted simulators</option>
            : simulators.map(s => <option key={s.udid} value={s.udid}>{s.name}</option>)
          }
        </select>
      </div>

      <p style={hint}>Click the map to teleport the simulator to that location.</p>

      <MapContainer center={[37.7749, -122.4194]} zoom={12} style={mapStyle}>
        <TileLayer
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        />
        <ClickMarker onPick={handlePick} />
      </MapContainer>

      <div style={statusBar}>
        {status === 'setting' && <span style={{ color: '#64748b' }}>Setting location…</span>}
        {status === 'set' && lastLoc && (
          <span style={{ color: '#16a34a' }}>
            Set: {lastLoc.lat.toFixed(5)}, {lastLoc.lon.toFixed(5)}
          </span>
        )}
        {status === 'error' && <span style={{ color: '#dc2626' }}>Failed to set location — is xcrun available?</span>}
      </div>
    </div>
  )
}

const row: React.CSSProperties = { marginBottom: '1rem' }
const label: React.CSSProperties = { display: 'block', fontWeight: 600, fontSize: '0.9rem', marginBottom: '0.4rem' }
const select: React.CSSProperties = { padding: '0.4rem 0.6rem', fontSize: '0.9rem', borderRadius: 6, border: '1px solid #cbd5e1', minWidth: 220 }
const hint: React.CSSProperties = { fontSize: '0.85rem', color: '#64748b', marginBottom: '0.75rem' }
const mapStyle: React.CSSProperties = { height: 460, borderRadius: 8, border: '1px solid #e2e8f0' }
const statusBar: React.CSSProperties = { marginTop: '0.75rem', fontSize: '0.85rem', minHeight: '1.2em' }
