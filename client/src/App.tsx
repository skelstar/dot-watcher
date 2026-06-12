import { useEffect, useRef, useState } from 'react'
import mapboxgl from 'mapbox-gl'
import 'mapbox-gl/dist/mapbox-gl.css'
import SessionPrompt from './SessionPrompt.tsx'
import Legend from './Legend.tsx'
import MapMenu from './MapMenu.tsx'
import { useRunnerMarkers } from './useRunnerMarkers.ts'

mapboxgl.accessToken = import.meta.env.VITE_MAPBOX_TOKEN as string

const POLL_INTERVAL_MS: number = parseInt(import.meta.env.VITE_POLL_INTERVAL_MS ?? '10000', 10)
const SERVER_URL: string = import.meta.env.VITE_SERVER_URL ?? 'http://localhost:5000'

function sessionCodeFromPath(): string | null {
  const code = window.location.pathname.replace(/^\//, '').trim()
  return code || null
}

export default function App() {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<mapboxgl.Map | null>(null)
  const [sessionCode, setSessionCode] = useState<string | null>(sessionCodeFromPath)
  const [menu, setMenu] = useState<{ x: number; y: number; lng: number; lat: number } | null>(null)

  useEffect(() => {
    const map = new mapboxgl.Map({
      container: containerRef.current!,
      style: 'mapbox://styles/mapbox/streets-v12',
      center: [151.2093, -33.8688],
      zoom: 13,
    })

    map.doubleClickZoom.disable()
    map.on('dblclick', (e) => {
      setMenu({ x: e.point.x, y: e.point.y, lng: e.lngLat.lng, lat: e.lngLat.lat })
    })

    map.addControl(new mapboxgl.NavigationControl(), 'top-right')
    map.addControl(new mapboxgl.GeolocateControl({
      positionOptions: { enableHighAccuracy: true },
      trackUserLocation: true,
    }), 'top-right')

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [])

  const { offScreenRunners, centerOnRunner, fitAll } = useRunnerMarkers(mapRef, sessionCode, SERVER_URL, POLL_INTERVAL_MS)

  async function sendChester(lng: number, lat: number) {
    if (!sessionCode) return
    await fetch(`${SERVER_URL}/location`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${import.meta.env.VITE_BEARER_TOKEN}`,
      },
      body: JSON.stringify({
        runnerName: 'Chester',
        sessionCode,
        latitude: lat,
        longitude: lng,
        heading: null,
        timestamp: new Date().toISOString(),
      }),
    })
  }

  function handleSessionSubmit(code: string) {
    const upper = code.trim().toUpperCase()
    window.history.replaceState(null, '', `/${upper}`)
    setSessionCode(upper)
  }

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      <button onClick={fitAll} style={fitAllBtn} title="Fit all">⤢</button>
      <Legend runners={offScreenRunners} onRunnerClick={centerOnRunner} />
      {menu && (
        <MapMenu
          x={menu.x}
          y={menu.y}
          onSendChester={() => sendChester(menu.lng, menu.lat)}
          onClose={() => setMenu(null)}
        />
      )}
      {!sessionCode && <SessionPrompt onSubmit={handleSessionSubmit} />}
    </>
  )
}

const fitAllBtn: React.CSSProperties = {
  position: 'absolute',
  bottom: 32,
  right: 12,
  width: 36,
  height: 36,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  fontSize: '1.1rem',
  color: '#333',
  padding: 0,
}
