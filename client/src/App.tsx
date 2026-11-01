import { useEffect } from 'react'
// maplibre-gl has no default export (unlike mapbox-gl) — named imports only.
import { setWorkerUrl } from 'maplibre-gl'
// v6 no longer auto-detects its worker URL under a bundler (only plain CDN <script type=module>
// loading gets that for free) — without this, every vector/GeoJSON source hangs forever waiting
// on a worker that never starts: tile.json/style.json/sprite resolve fine (main thread), but zero
// .pbf tile requests ever fire and isSourceLoaded() stays false. `?worker&url` (not plain `?url`)
// is required so Vite emits the worker as a self-contained chunk — the raw dist file imports a
// sibling module that a plain `?url` copy wouldn't bring along. See
// https://maplibre.org/maplibre-gl-js/docs/guides/v5-to-v6-migration-guide/ and
// https://github.com/maplibre/maplibre-gl-js/issues/8018.
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
import LegalPage from './LegalPage.tsx'
import DeleteAccountPage from './DeleteAccountPage.tsx'
import AdminPanel from './AdminPanel.tsx'
import RouteUploadPage from './RouteUploadPage.tsx'
import LandingPage from './LandingPage.tsx'
import JoinPage from './JoinPage.tsx'
import CrewLinkPage from './CrewLinkPage.tsx'
import WatchPage from './WatchPage.tsx'
import SessionMapPage from './SessionMapPage.tsx'

setWorkerUrl(maplibreWorkerUrl)

const SERVER_URL: string = import.meta.env.VITE_SERVER_URL ?? '/api'

interface RouteState {
  inviteCode: string | null
  joinCode: string | null
  crewCode: string | null
  // The secret of a casual watch link, /watch/{token}. Case-sensitive, so it is never upper-cased.
  watchToken: string | null
  legalPage: 'privacy' | 'terms' | null
  isAdmin: boolean
  isLanding: boolean
  isDeleteAccount: boolean
  routeUploadSessionId: string | null
}

// The only routes the app generates: '/' (landing), '/code/{code}', '/join/{code}' and '/crew/{code}' (shared
// via the iOS ShareLink; iOS claims /join as a universal link, so the web only shows a fallback;
// /crew/{code} is a crew-add link, which likewise only shows a fallback page with the code),
// plus /admin, the legal pages, /delete-account (linked from the Play Store listing's Data Safety
// section) and /route-upload/{sessionId} (opened by the iOS app with an upload token in the URL
// fragment). Watching is invite-code only — anything else falls back to the landing page.
function parseUrl(): RouteState {
  const parts = window.location.pathname.replace(/^\//, '').split('/')
  const norm = (s: string) => s.toUpperCase() || null
  const base = { inviteCode: null, joinCode: null, crewCode: null, watchToken: null, legalPage: null, isAdmin: false, isLanding: false, isDeleteAccount: false, routeUploadSessionId: null }
  if (parts[0] === 'admin') return { ...base, isAdmin: true }
  if (parts[0] === 'privacy') return { ...base, legalPage: 'privacy' }
  if (parts[0] === 'terms') return { ...base, legalPage: 'terms' }
  if (parts[0] === 'delete-account') return { ...base, isDeleteAccount: true }
  if (parts[0] === 'route-upload' && parts[1]) return { ...base, routeUploadSessionId: parts[1] }
  if (parts[0] === 'join' && parts[1]) return { ...base, joinCode: norm(parts[1]) }
  if (parts[0] === 'crew' && parts[1]) return { ...base, crewCode: norm(parts[1]) }
  if (parts[0] === 'watch' && parts[1]) return { ...base, watchToken: parts[1] }
  if (parts[0] === 'code') return { ...base, inviteCode: norm(parts[1] ?? '') }
  return { ...base, isLanding: true }
}

export default function App() {
  const { inviteCode, joinCode, crewCode, watchToken, legalPage, isAdmin, isLanding, isDeleteAccount, routeUploadSessionId } = parseUrl()

  // Unknown paths (e.g. /log) fall back to the landing page below; rewrite the address bar to '/' so
  // they don't sit on a URL that means nothing.
  useEffect(() => {
    if (isLanding && window.location.pathname !== '/') {
      window.history.replaceState(null, '', `/${window.location.search}${window.location.hash}`)
    }
  }, [isLanding])

  if (isAdmin) {
    return <AdminPanel serverUrl={SERVER_URL} />
  }

  if (routeUploadSessionId) {
    return <RouteUploadPage serverUrl={SERVER_URL} sessionId={routeUploadSessionId} />
  }

  if (watchToken) {
    return <WatchPage serverUrl={SERVER_URL} token={watchToken} />
  }

  if (crewCode) {
    return <CrewLinkPage code={crewCode} />
  }

  if (joinCode) {
    return <JoinPage code={joinCode} />
  }

  if (legalPage) {
    return <LegalPage kind={legalPage} />
  }

  if (isDeleteAccount) {
    return <DeleteAccountPage serverUrl={SERVER_URL} />
  }

  if (isLanding) {
    return <LandingPage />
  }

  return <SessionMapPage serverUrl={SERVER_URL} inviteCode={inviteCode} />
}
