import { useEffect, useRef, useState } from 'react'
import mapboxgl from 'mapbox-gl'
import 'mapbox-gl/dist/mapbox-gl.css'
import SessionPrompt from './SessionPrompt.tsx'
import Legend from './Legend.tsx'
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

  useEffect(() => {
    const map = new mapboxgl.Map({
      container: containerRef.current!,
      style: 'mapbox://styles/mapbox/streets-v12',
      center: [151.2093, -33.8688],
      zoom: 13,
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

  const { visibleRunners, centerOnRunner } = useRunnerMarkers(mapRef, sessionCode, SERVER_URL, POLL_INTERVAL_MS)

  function handleSessionSubmit(code: string) {
    const upper = code.trim().toUpperCase()
    window.history.replaceState(null, '', `/${upper}`)
    setSessionCode(upper)
  }

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      <Legend runners={visibleRunners} onRunnerClick={centerOnRunner} />
      {!sessionCode && <SessionPrompt onSubmit={handleSessionSubmit} />}
    </>
  )
}
