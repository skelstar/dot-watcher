import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { MapLibreMap, Marker, NavigationControl } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'
import { MAP_STYLES, DEFAULT_MAP_STYLE_ID, LINZ_ATTRIBUTION, LEGAL_LINKS_ATTRIBUTION } from './map/mapStyle.ts'
import { page, card, icon, headline, body } from './cardPageStyles.ts'
import { initialsFor } from './components/InitialsBadge.tsx'
import { statusLine, watchState, type WatchViewDto } from './watchView.ts'

interface Props {
  serverUrl: string
  token: string
}

const POLL_MS = 10_000

type Load = { kind: 'loading' } | { kind: 'notFound' } | { kind: 'ok'; view: WatchViewDto }

// Shown at /watch/{token}: one casual runner on a map, for someone who was sent the link. No sign-in and
// no app. The secret in the address is the whole credential, so the page keeps it out of referrers and
// search indexes. The server answers only with that runner's position (see WatchController).
export default function WatchPage({ serverUrl, token }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const markerRef = useRef<{ marker: Marker; arrow: HTMLElement; badge: HTMLElement } | null>(null)
  const followingRef = useRef(true)
  const [load, setLoad] = useState<Load>({ kind: 'loading' })
  const [following, setFollowing] = useState(true)
  const [offline, setOffline] = useState(false)
  // A browser with no WebGL can't draw the map; the page then falls back to the position as text.
  const [mapFailed, setMapFailed] = useState(false)
  const [nowMs, setNowMs] = useState(Date.now())

  // Keeps the link out of search results and out of the Referer header sent to other sites.
  useEffect(() => {
    const robots = document.createElement('meta')
    robots.name = 'robots'
    robots.content = 'noindex'
    const referrer = document.createElement('meta')
    referrer.name = 'referrer'
    referrer.content = 'no-referrer'
    document.head.append(robots, referrer)
    return () => { robots.remove(); referrer.remove() }
  }, [])

  // Poll while the page is visible; a failed poll keeps the last answer on screen.
  useEffect(() => {
    let cancelled = false
    let timer: number | undefined

    async function poll() {
      if (document.visibilityState === 'hidden') { schedule(); return }
      try {
        const response = await fetch(`${serverUrl}/watch/${encodeURIComponent(token)}`, { cache: 'no-store' })
        if (cancelled) return
        if (response.status === 404) {
          setLoad({ kind: 'notFound' })
          return // a link that doesn't exist won't start to; stop asking
        }
        if (response.ok) {
          setLoad({ kind: 'ok', view: (await response.json()) as WatchViewDto })
          setOffline(false)
        } else {
          setOffline(true)
        }
      } catch {
        if (!cancelled) setOffline(true)
      }
      schedule()
    }

    function schedule() {
      if (!cancelled) timer = window.setTimeout(poll, POLL_MS)
    }

    void poll()
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [serverUrl, token])

  // Once a second, so "updated 12 s ago" keeps counting between polls.
  useEffect(() => {
    const id = window.setInterval(() => setNowMs(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const view = load.kind === 'ok' ? load.view : null
  const hasMap = load.kind !== 'notFound'

  useEffect(() => {
    document.title = view ? `${view.label} on Dot Watcher` : 'Dot Watcher'
  }, [view?.label])

  // The map exists once there is something to show it for (not for an unknown link).
  useEffect(() => {
    if (!hasMap || !containerRef.current) return
    let map: MapLibreMap
    try {
      map = new MapLibreMap({
        container: containerRef.current,
        style: MAP_STYLES[DEFAULT_MAP_STYLE_ID].url,
        center: [174.7762, -41.2865],
        zoom: 15,
        attributionControl: { compact: false, customAttribution: [LEGAL_LINKS_ATTRIBUTION, LINZ_ATTRIBUTION] },
      })
    } catch {
      setMapFailed(true)
      return
    }
    map.addControl(new NavigationControl({ showZoom: false }), 'top-right')
    // Only a person's own gesture carries an originalEvent; the camera following the runner does not.
    map.on('dragstart', e => {
      if (!e.originalEvent) return
      followingRef.current = false
      setFollowing(false)
    })
    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
      markerRef.current = null
    }
  }, [hasMap])

  // Move the runner's dot, and the camera while following.
  const position = view?.position ?? null
  useEffect(() => {
    const map = mapRef.current
    if (!map || !position || !view) return
    const lngLat: [number, number] = [position.longitude, position.latitude]
    if (!markerRef.current) {
      const element = document.createElement('div')
      const arrow = document.createElement('div')
      Object.assign(arrow.style, arrowStyle)
      const badge = document.createElement('div')
      Object.assign(badge.style, badgeStyle)
      // MapLibre positions the marker element itself (absolutely), so only its size is set here.
      element.style.width = '36px'
      element.style.height = '36px'
      element.append(arrow, badge)
      markerRef.current = { marker: new Marker({ element }).setLngLat(lngLat).addTo(map), arrow, badge }
      map.jumpTo({ center: lngLat })
    }
    const { marker, arrow, badge } = markerRef.current
    marker.setLngLat(lngLat)
    badge.textContent = initialsFor(view.label)
    badge.style.opacity = view.live && !view.ended ? '1' : '0.55'
    arrow.style.display = position.heading === null || !view.live ? 'none' : 'block'
    arrow.style.transform = `translate(-50%, -50%) rotate(${position.heading ?? 0}deg) translateY(-26px)`
    if (followingRef.current) map.easeTo({ center: lngLat, duration: 800 })
  }, [position?.latitude, position?.longitude, position?.heading, view?.live, view?.label])

  function recenter() {
    followingRef.current = true
    setFollowing(true)
    if (position) mapRef.current?.easeTo({ center: [position.longitude, position.latitude], duration: 600 })
  }

  if (load.kind === 'notFound') {
    return (
      <main style={page}>
        <div style={card}>
          <img src="/landing/assets/app-icon.png" alt="" style={icon} />
          <h1 style={headline}>This link isn't working</h1>
          <p style={body}>
            The run may have ended, or the person turned the link off. Ask them for a new one if they are still out.
          </p>
        </div>
      </main>
    )
  }

  const state = view ? watchState(view, nowMs) : null
  const line = state && view ? statusLine(state, nowMs, view.position?.receivedAt ?? null) : 'Loading…'

  return (
    <>
      {mapFailed ? (
        <main style={page}>
          <div style={card}>
            <img src="/landing/assets/app-icon.png" alt="" style={icon} />
            <h1 style={headline}>{view ? `Watching ${view.label}` : 'Dot Watcher'}</h1>
            <p style={body}>{line}</p>
            {position && state?.kind !== 'ended' && (
              <a
                href={`https://maps.apple.com/?ll=${position.latitude},${position.longitude}&q=${encodeURIComponent(view?.label ?? 'Runner')}`}
                rel="noopener noreferrer"
                style={{ ...body, display: 'inline-block', fontWeight: 700, color: '#1f6feb' }}
              >
                Open in Maps
              </a>
            )}
            <p style={{ ...body, fontSize: 13, margin: 0 }}>This browser can't draw the map.</p>
          </div>
        </main>
      ) : (
        <div ref={containerRef} style={{ width: '100%', height: '100%' }} />
      )}
      {!mapFailed && <div style={bar}>
        <div style={barTitle}>{view ? `Watching ${view.label}` : 'Dot Watcher'}</div>
        <div style={{ ...chip, ...(state?.kind === 'ended' ? chipEnded : state?.kind === 'live' ? chipLive : null) }}>
          {line}
          {offline && state?.kind !== 'ended' ? ' · reconnecting…' : ''}
        </div>
      </div>}
      {!mapFailed && !following && position && (
        <button type="button" style={recenterButton} onClick={recenter}>Recenter</button>
      )}
    </>
  )
}

const badgeStyle: Partial<CSSStyleDeclaration> = {
  width: '36px',
  height: '36px',
  borderRadius: '50%',
  background: '#1f6feb',
  boxShadow: '0 0 0 3px #fff, 0 1px 6px rgba(0,0,0,0.35)',
  color: '#fff',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: '700',
  fontSize: '14px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
}

// A small chevron that orbits the dot to show which way the runner is heading.
const arrowStyle: Partial<CSSStyleDeclaration> = {
  position: 'absolute',
  left: '50%',
  top: '50%',
  width: '0',
  height: '0',
  borderLeft: '7px solid transparent',
  borderRight: '7px solid transparent',
  borderBottom: '12px solid #1f6feb',
  filter: 'drop-shadow(0 0 2px #fff)',
  transformOrigin: 'center',
}

const bar: CSSProperties = {
  position: 'absolute',
  top: 'max(12px, env(safe-area-inset-top))',
  left: 12,
  right: 64,
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'flex-start',
  gap: 8,
  pointerEvents: 'none',
  fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Text", system-ui, sans-serif',
}

const barTitle: CSSProperties = {
  fontSize: 20,
  fontWeight: 800,
  letterSpacing: '-0.4px',
  color: '#14151A',
  textShadow: '0 0 6px #fff, 0 0 6px #fff',
}

const chip: CSSProperties = {
  padding: '7px 14px',
  borderRadius: 999,
  background: 'rgba(255,255,255,0.9)',
  boxShadow: '0 1px 4px rgba(0,0,0,0.18)',
  fontSize: 13,
  fontWeight: 600,
  color: '#14151A',
}

const chipLive: CSSProperties = { color: '#1a7f37' }
const chipEnded: CSSProperties = { background: '#14151A', color: '#fff' }

const recenterButton: CSSProperties = {
  position: 'absolute',
  bottom: 'max(24px, env(safe-area-inset-bottom))',
  right: 16,
  padding: '12px 20px',
  border: 'none',
  borderRadius: 999,
  background: '#1f6feb',
  color: '#fff',
  fontSize: 15,
  fontWeight: 700,
  boxShadow: '0 2px 8px rgba(0,0,0,0.3)',
  cursor: 'pointer',
}
