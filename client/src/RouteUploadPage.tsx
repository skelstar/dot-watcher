import { useEffect, useRef, useState } from 'react'
import { MapLibreMap } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { MAP_STYLES, DEFAULT_MAP_STYLE_ID, LINZ_ATTRIBUTION } from './map/mapStyle.ts'
import { useRouteLayer } from './useRouteLayer.ts'
import { parseGpxCoordinates } from './gpx.ts'
import { apiHeaders } from './apiHeaders.ts'

interface Props {
  serverUrl: string
  sessionId: string
}

type UploadState = 'idle' | 'uploading' | 'done'

// Opened by the iOS app (in an in-app browser) with a short-lived, single-session upload token in
// the URL fragment — the fragment never reaches the server or its logs. The web client has no
// sign-in, so that token is the only credential. It's moved into state and stripped from the
// address bar straight away.
export default function RouteUploadPage({ serverUrl, sessionId }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const [token] = useState<string | null>(() => {
    const value = new URLSearchParams(window.location.hash.replace(/^#/, '')).get('token')
    return value || null
  })
  const [gpx, setGpx] = useState<string | null>(null)
  const [fileName, setFileName] = useState<string | null>(null)
  const [coordinates, setCoordinates] = useState<[number, number][] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [state, setState] = useState<UploadState>('idle')

  useEffect(() => {
    if (window.location.hash) window.history.replaceState(null, '', window.location.pathname)
  }, [])

  useEffect(() => {
    if (!containerRef.current) return
    const map = new MapLibreMap({
      container: containerRef.current,
      style: MAP_STYLES[DEFAULT_MAP_STYLE_ID].url,
      center: [174.7762, -41.2865],
      zoom: 5,
      attributionControl: { customAttribution: LINZ_ATTRIBUTION },
    })
    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [])

  useRouteLayer(mapRef, coordinates, true)

  async function handleFile(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return

    setError(null)
    setState('idle')
    setGpx(null)
    setCoordinates(null)
    setFileName(file.name)

    try {
      const text = await file.text()
      const parsed = parseGpxCoordinates(text)
      if (parsed.length < 2) throw new Error('No route found — this GPX file has no track points.')
      setGpx(text)
      setCoordinates(parsed)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not read that file.')
    }
  }

  async function handleUpload() {
    if (!gpx || !token) return
    setError(null)
    setState('uploading')
    try {
      const response = await fetch(`${serverUrl}/sessions/${sessionId}/route`, {
        method: 'POST',
        headers: { ...apiHeaders(token), 'Content-Type': 'application/gpx+xml' },
        body: gpx,
      })
      if (response.status === 401 || response.status === 403) {
        throw new Error('This upload link has expired. Go back to the app and tap “Add route” again.')
      }
      if (!response.ok) throw new Error('Upload failed. Please try again.')
      setState('done')
    } catch (e) {
      setState('idle')
      setError(e instanceof Error ? e.message : 'Upload failed. Please try again.')
    }
  }

  return (
    <div style={page}>
      <div style={header}>
        <h1 style={title}>Add route</h1>
        {!token ? (
          <p style={errorText}>This link is missing its upload token. Go back to the app and tap “Add route” again.</p>
        ) : state === 'done' ? (
          <p style={successText}>Route added. You can close this page and return to the app.</p>
        ) : (
          <>
            <p style={hint}>Choose a GPX file for your session. You&rsquo;ll see a preview before it&rsquo;s uploaded.</p>
            <div style={actions}>
              <label style={pickButton}>
                {fileName ? 'Choose a different file' : 'Choose GPX file'}
                <input
                  type="file"
                  accept=".gpx,application/gpx+xml,application/xml,text/xml"
                  onChange={event => void handleFile(event)}
                  style={{ display: 'none' }}
                />
              </label>
              <button
                type="button"
                style={{ ...uploadButton, opacity: gpx && state !== 'uploading' ? 1 : 0.4 }}
                disabled={!gpx || state === 'uploading'}
                onClick={() => void handleUpload()}
              >
                {state === 'uploading' ? 'Uploading…' : 'Upload route'}
              </button>
            </div>
            {fileName && !error && <p style={fileLabel}>{fileName}</p>}
          </>
        )}
        {error && <p style={errorText}>{error}</p>}
      </div>
      <div ref={containerRef} style={mapBox} />
    </div>
  )
}

const page: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  width: '100%',
  height: '100%',
  fontFamily: 'system-ui, -apple-system, sans-serif',
  background: '#fff',
}

const header: React.CSSProperties = {
  padding: '16px 16px 12px',
  borderBottom: '1px solid #e5e5e5',
}

const title: React.CSSProperties = { margin: '0 0 4px', fontSize: 20 }
const hint: React.CSSProperties = { margin: '0 0 12px', fontSize: 14, color: '#555' }
const actions: React.CSSProperties = { display: 'flex', gap: 8, flexWrap: 'wrap' }
const fileLabel: React.CSSProperties = { margin: '8px 0 0', fontSize: 13, color: '#555' }
const errorText: React.CSSProperties = { margin: '8px 0 0', fontSize: 14, color: '#b42318' }
const successText: React.CSSProperties = { margin: '4px 0 0', fontSize: 15, color: '#16a34a', fontWeight: 600 }

const buttonBase: React.CSSProperties = {
  padding: '10px 16px',
  borderRadius: 8,
  fontSize: 15,
  fontWeight: 600,
  border: '1px solid #1f6feb',
  cursor: 'pointer',
}

const pickButton: React.CSSProperties = { ...buttonBase, background: '#fff', color: '#1f6feb' }
const uploadButton: React.CSSProperties = { ...buttonBase, background: '#1f6feb', color: '#fff' }

const mapBox: React.CSSProperties = { flex: 1, minHeight: 0 }
