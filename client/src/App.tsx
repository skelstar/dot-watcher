import { useEffect, useRef, useState } from 'react'
// maplibre-gl has no default export (unlike mapbox-gl) — named imports only.
import { MapLibreMap, NavigationControl, GeolocateControl, setWorkerUrl } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
// v6 no longer auto-detects its worker URL under a bundler (only plain CDN <script type=module>
// loading gets that for free) — without this, every vector/GeoJSON source hangs forever waiting
// on a worker that never starts: tile.json/style.json/sprite resolve fine (main thread), but zero
// .pbf tile requests ever fire and isSourceLoaded() stays false. `?worker&url` (not plain `?url`)
// is required so Vite emits the worker as a self-contained chunk — the raw dist file imports a
// sibling module that a plain `?url` copy wouldn't bring along. See
// https://maplibre.org/maplibre-gl-js/docs/guides/v5-to-v6-migration-guide/ and
// https://github.com/maplibre/maplibre-gl-js/issues/8018.
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
import { MAP_STYLES, DEFAULT_MAP_STYLE_ID, LINZ_ATTRIBUTION, type MapStyleId } from './map/mapStyle.ts'
import MapStyleToggle from './MapStyleToggle.tsx'
import Terrain3DToggle from './Terrain3DToggle.tsx'
import InvalidInvitePrompt from './InvalidInvitePrompt.tsx'
import Legend from './Legend.tsx'
import LegendHelp from './LegendHelp.tsx'
import ReplayControls from './ReplayControls.tsx'
import MapPlayButton from './MapPlayButton.tsx'
import LegalPage from './LegalPage.tsx'
import AdminPanel from './AdminPanel.tsx'
import RouteUploadPage from './RouteUploadPage.tsx'
import LandingPage from './LandingPage.tsx'
import InitialsBadge from './components/InitialsBadge.tsx'
import { useRunnerMarkers } from './useRunnerMarkers.ts'
import { useRouteLayer } from './useRouteLayer.ts'
import { useSimulatorRouteOverlay } from './useSimulatorRouteOverlay.ts'
import { useSessionTimeline } from './useSessionTimeline.ts'
import { parseGpxCoordinates } from './gpx.ts'
import { apiHeaders } from './apiHeaders.ts'

setWorkerUrl(maplibreWorkerUrl)

const SERVER_URL: string = import.meta.env.VITE_SERVER_URL ?? '/api'
const APP_VERSION = import.meta.env.VITE_APP_VERSION ?? 'v-local'
const APP_UPDATED_AT = import.meta.env.VITE_APP_UPDATED_AT ?? 'Updated local'
const VERSION_LABEL = `${APP_VERSION} · ${APP_UPDATED_AT}`

interface RouteState {
  inviteCode: string | null
  legalPage: 'privacy' | 'terms' | null
  isAdmin: boolean
  isLanding: boolean
  routeUploadSessionId: string | null
}

// The only routes the app generates: '/' (landing) and '/code/{code}' (shared via the iOS
// ShareLink), plus /admin, the legal pages and /route-upload/{sessionId} (opened by the iOS app
// with an upload token in the URL fragment). Watching is invite-code only — anything else
// falls back to the landing page.
function parseUrl(): RouteState {
  const parts = window.location.pathname.replace(/^\//, '').split('/')
  const norm = (s: string) => s.toUpperCase() || null
  const base = { inviteCode: null, legalPage: null, isAdmin: false, isLanding: false, routeUploadSessionId: null }
  if (parts[0] === 'admin') return { ...base, isAdmin: true }
  if (parts[0] === 'privacy') return { ...base, legalPage: 'privacy' }
  if (parts[0] === 'terms') return { ...base, legalPage: 'terms' }
  if (parts[0] === 'route-upload' && parts[1]) return { ...base, routeUploadSessionId: parts[1] }
  if (parts[0] === 'code') return { ...base, inviteCode: norm(parts[1] ?? '') }
  return { ...base, isLanding: true }
}

export default function App() {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const { inviteCode, legalPage, isAdmin, isLanding, routeUploadSessionId } = parseUrl()
  const [routeCoordinates, setRouteCoordinates] = useState<[number, number][] | null>(null)
  const [mapStyleId, setMapStyleId] = useState<MapStyleId>(DEFAULT_MAP_STYLE_ID)

  // Imperative: swaps the live map's style in place rather than recreating the Map instance.
  // useRouteLayer/useSimulatorRouteOverlay re-add their sources/layers/images on the resulting
  // 'style.load' event; runner markers are plain DOM overlays untouched by a style change.
  function handleToggleMapStyle(next: MapStyleId) {
    setMapStyleId(next)
    mapRef.current?.setStyle(MAP_STYLES[next].url)
  }

  // Experimental 3D terrain: LINZ's 1m DEM (declared as the 'LINZ-Terrain' raster-dem source in
  // both styles) draped under the imagery, with a pitched camera. See client/plans/linz-topo-migration.md.
  const [terrain3d, setTerrain3d] = useState(false)
  const terrain3dRef = useRef(false)

  function handleToggleTerrain3d() {
    const map = mapRef.current
    const next = !terrain3d
    terrain3dRef.current = next
    setTerrain3d(next)
    if (!map) return
    if (next) {
      map.setMaxPitch(85)
      map.setTerrain({ source: 'LINZ-Terrain', exaggeration: 1 })
      map.easeTo({ pitch: 60, duration: 800 })
    } else {
      map.setTerrain(null)
      map.easeTo({ pitch: 0, bearing: 0, duration: 800 })
      // Lock pitch again once the flatten animation finishes (clamping earlier would snap it).
      map.once('moveend', () => { if (!terrain3dRef.current) map.setMaxPitch(0) })
    }
  }

  useEffect(() => {
    if (legalPage || isLanding || !containerRef.current) return

    const map = new MapLibreMap({
      container: containerRef.current,
      style: MAP_STYLES[DEFAULT_MAP_STYLE_ID].url,
      center: [174.7762, -41.2865], // Wellington, NZ - default before any session/positions load
      zoom: 13,
      maxPitch: 0, // flat until the 3D toggle raises it
      attributionControl: { customAttribution: LINZ_ATTRIBUTION },
    })

    map.addControl(new NavigationControl(), 'top-right')
    map.addControl(new GeolocateControl({
      positionOptions: { enableHighAccuracy: true },
      trackUserLocation: true,
    }), 'top-right')

    // setStyle() wipes the terrain along with everything else, so re-apply it on every style load.
    map.on('style.load', () => {
      if (terrain3dRef.current) map.setTerrain({ source: 'LINZ-Terrain', exaggeration: 1 })
    })

    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
    }
  }, [legalPage])

  const timeline = useSessionTimeline(SERVER_URL, inviteCode)

  const { allRunners, followRunner, fitAll } = useRunnerMarkers(
    mapRef,
    timeline.positions,
    timeline.virtualNowMs,
    timeline.runnersWithGpsSignalLoss,
    timeline.runnersWithGap,
    timeline.runnersSleeping,
  )
  useRouteLayer(mapRef, routeCoordinates, timeline.runStartMs === null)
  useSimulatorRouteOverlay(mapRef)

  const routeBase = inviteCode ? `${SERVER_URL}/session-invites/${inviteCode}` : null

  useEffect(() => {
    setRouteCoordinates(null)
    if (!routeBase) return

    let cancelled = false
    async function loadRoute() {
      const response = await fetch(`${routeBase}/route`, {
        headers: apiHeaders(),
      })
      if (cancelled || !response.ok) return
      const gpx = await response.text()
      setRouteCoordinates(parseGpxCoordinates(gpx))
    }

    void loadRoute()
    return () => { cancelled = true }
  }, [routeBase])

  // Who created the session, for the "hasn't started yet" toast — from the tokenless
  // session-info endpoint, the same invite-code-only surface everything else here uses.
  const [ownerDisplayName, setOwnerDisplayName] = useState<string | null>(null)
  useEffect(() => {
    setOwnerDisplayName(null)
    if (!inviteCode) return

    let cancelled = false
    fetch(`${SERVER_URL}/session-invites/${inviteCode}`)
      .then(res => (res.ok ? res.json() as Promise<{ ownerDisplayName: string }> : null))
      .then(info => { if (!cancelled && info) setOwnerDisplayName(info.ownerDisplayName) })
      .catch(() => undefined)
    return () => { cancelled = true }
  }, [inviteCode])

  if (isAdmin) {
    return <AdminPanel serverUrl={SERVER_URL} />
  }

  if (routeUploadSessionId) {
    return <RouteUploadPage serverUrl={SERVER_URL} sessionId={routeUploadSessionId} />
  }

  if (legalPage) {
    return <LegalPage kind={legalPage} />
  }

  if (isLanding) {
    return <LandingPage />
  }

  return (
    <>
      <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      <div style={versionBadge}>
        <span>{VERSION_LABEL}</span>
        <a href="/privacy" style={legalLink}>Privacy</a>
        <a href="/terms" style={legalLink}>Terms</a>
      </div>
      <LegendHelp />
      <MapStyleToggle styleId={mapStyleId} onToggle={handleToggleMapStyle} />
      <Terrain3DToggle enabled={terrain3d} onToggle={handleToggleTerrain3d} />
      <Legend
        runners={allRunners}
        onRunnerClick={followRunner}
        onFitAll={fitAll}
        runnersWithGap={timeline.runnersWithGap}
        runnersSleeping={timeline.runnersSleeping}
        runnersWithGpsSignalLoss={timeline.runnersWithGpsSignalLoss}
        runnersUltraConstrained={timeline.runnersUltraConstrained}
        runnerCountdowns={timeline.runnerCountdowns}
        runnerLastSeenMs={timeline.runnerLastSeenMs}
      />
      {!timeline.invalidInvite && timeline.runStartMs !== null && inviteCode && (
        <>
          <ReplayControls timeline={timeline} />
          {/* "following" alone would hide this on a finished session's very first load: scrubTimeMs
              starts null (following=true) same as it does mid-live-watching, but there's no live
              edge to follow once the session's over — isLive tells those two states apart. */}
          {!timeline.playing && (!timeline.isLive || !timeline.following) && (
            <MapPlayButton onPlay={timeline.play} />
          )}
        </>
      )}
      {!timeline.invalidInvite && !timeline.error && timeline.runStartMs === null && inviteCode && (
        <div style={notStartedToast}>
          {ownerDisplayName && (
            <div style={notStartedToastLine}>
              <span>Created by </span>
              <InitialsBadge name={ownerDisplayName} />
            </div>
          )}
          <div style={notStartedToastLine}>The session hasn&rsquo;t started yet.</div>
        </div>
      )}
      {timeline.error && !timeline.invalidInvite && inviteCode && (
        <div style={statusToast}>{timeline.error}</div>
      )}
      {timeline.invalidInvite && (
        <InvalidInvitePrompt
          message={
            inviteCode
              ? `Invite code '${inviteCode}' not found. Session might have expired or been deleted.`
              : timeline.error ?? 'Invite not found.'
          }
        />
      )}
    </>
  )
}


const versionBadge: React.CSSProperties = {
  position: 'absolute',
  left: 12,
  bottom: 12,
  zIndex: 7,
  maxWidth: 'min(620px, calc(100vw - 96px))',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  background: 'rgba(255,255,255,0.92)',
  color: '#57606a',
  borderRadius: 4,
  boxShadow: '0 0 0 1px rgba(0,0,0,0.1)',
  padding: '4px 7px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.75rem',
}

const legalLink: React.CSSProperties = {
  flex: '0 0 auto',
  color: '#1f6feb',
  textDecoration: 'none',
}

const notStartedToast: React.CSSProperties = {
  position: 'absolute',
  left: '50%',
  bottom: 100,
  transform: 'translateX(-50%)',
  zIndex: 8,
  maxWidth: '92vw',
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  gap: 4,
  background: '#fff',
  color: '#57606a',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '8px 14px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}

const notStartedToastLine: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
  whiteSpace: 'nowrap',
}

const statusToast: React.CSSProperties = {
  position: 'absolute',
  left: '50%',
  bottom: 88,
  transform: 'translateX(-50%)',
  zIndex: 8,
  maxWidth: 'min(420px, 92vw)',
  background: '#fff',
  color: '#92400e',
  borderRadius: 6,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  padding: '8px 12px',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.9rem',
}
