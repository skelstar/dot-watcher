// Minimal client for the auth + session + location + Go endpoints, used by the Convergence
// simulator to act as N independent phone/app users against a real Dot Watcher server.

// VITE_SERVER_PORT comes from the repo root's shared .env (see start-local.ps1 and
// client/vite.config.ts, which read the same value) so every local tool agrees on the
// server's port. VITE_SERVER_URL is a full-URL override for pointing at a non-localhost
// server and takes priority over it when set.
const sharedServerPort = (import.meta.env.VITE_SERVER_PORT as string | undefined) ?? '8080'
export const SERVER_URL = (import.meta.env.VITE_SERVER_URL as string | undefined) ?? `http://localhost:${sharedServerPort}`

// The main web client's own dev server (see start-local.ps1) — used to embed it live in the
// Convergence tab. Not part of the shared VITE_SERVER_PORT value above; that's the .NET
// server's port, this is the separate Vite dev server for client/.
export const CLIENT_URL = (import.meta.env.VITE_CLIENT_URL as string | undefined) ?? 'http://localhost:5173'

// False when pointed at a real server (prod, via `npm run dev:prod`): the page then shows a warning,
// since prod has one database and real people near a simulated phone can see its dot.
export const IS_LOCAL_SERVER = (() => {
  try {
    const host = new URL(SERVER_URL).hostname
    return host === 'localhost' || host === '127.0.0.1' || host.endsWith('.local')
  } catch {
    return true
  }
})()

// The shared admin bearer token (matches the server's `BearerToken` config) — only needed for
// the /admin/users cleanup below, not for anything phones themselves do.
const ADMIN_BEARER_TOKEN = import.meta.env.VITE_BEARER_TOKEN as string | undefined

export type AuthedUser = {
  accessToken: string
  userId: string
  username: string
  displayName: string
}

export type SessionMembership = {
  sessionId: string
  sessionName: string
  inviteCode: string
  role: string
  displayName: string
  ownerDisplayName: string
}

class ApiError extends Error {}

async function errorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const data = await res.json()
    return typeof data?.error === 'string' ? data.error : fallback
  } catch {
    return fallback
  }
}

function randomToken(): string {
  return Math.random().toString(36).slice(2, 10) + Math.random().toString(36).slice(2, 10)
}

// Registers a throwaway account for one simulated participant (organizer or phone) and
// returns an authenticated session for it. Each call gets its own random username/password —
// there's no reason for simulated participants to share a real account.
export async function registerParticipant(displayName: string): Promise<AuthedUser> {
  const suffix = randomToken().slice(0, 12)
  const username = `sim-${suffix}`
  const password = randomToken()

  const res = await fetch(`${SERVER_URL}/auth/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password, displayName }),
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Registration failed (${res.status}).`))

  const data = await res.json()
  return {
    accessToken: data.accessToken,
    // The server sends `userId` (see ContractTests); `id` is kept as a fallback for an older server.
    userId: data.user.userId ?? data.user.id,
    username: data.user.username,
    displayName: data.user.displayName,
  }
}

export async function createSession(user: AuthedUser, sessionName: string): Promise<SessionMembership> {
  const res = await fetch(`${SERVER_URL}/sessions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${user.accessToken}` },
    body: JSON.stringify({ sessionName, displayName: user.displayName }),
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Create session failed (${res.status}).`))
  return res.json()
}

export async function joinSession(user: AuthedUser, inviteCode: string, displayName: string): Promise<SessionMembership> {
  const res = await fetch(`${SERVER_URL}/session-invites/${encodeURIComponent(inviteCode)}/join`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${user.accessToken}` },
    body: JSON.stringify({ displayName, role: 'runner' }),
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Join failed (${res.status}).`))
  return res.json()
}

export async function postLocation(
  user: AuthedUser,
  sessionId: string,
  lat: number,
  lon: number,
  heading: number | null,
  isUltraConstrained = false,
  nextExpectedAt?: Date,
  // Omitted (not 0/null) when undefined — matches the real app never sending batteryLevel when
  // UIDevice.batteryLevel is unavailable, so the server/client correctly read it as unknown
  // rather than assuming a dead battery. See .ai/plans/battery-level-reporting.md.
  batteryLevel?: number
): Promise<void> {
  const res = await fetch(`${SERVER_URL}/location`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${user.accessToken}` },
    body: JSON.stringify({
      sessionId,
      latitude: lat,
      longitude: lon,
      heading,
      timestamp: new Date().toISOString(),
      isUltraConstrained,
      ...(nextExpectedAt && { nextExpectedAt: nextExpectedAt.toISOString() }),
      ...(batteryLevel !== undefined && { batteryLevel }),
    }),
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Location post failed (${res.status}).`))
}

export async function leaveSession(user: AuthedUser, sessionId: string): Promise<void> {
  await fetch(`${SERVER_URL}/me/sessions/${sessionId}/membership`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${user.accessToken}` },
  })
}

// Deletes every registered account whose username starts with "sim-" (i.e. every account this
// tool has ever created) via the admin endpoints — run before creating a new session so
// previous test runs don't pile up accounts (and, since deleting a user also deletes any
// sessions it owns, their sessions too). Best-effort: one account failing to delete doesn't
// stop the rest. Requires VITE_BEARER_TOKEN; throws if it's not configured.
// `keepUserIds`: accounts still in use (the phones on screen, the session's organizer), left alone.
export async function deleteSimAccounts(keepUserIds: ReadonlySet<string> = new Set()): Promise<number> {
  if (!ADMIN_BEARER_TOKEN) {
    throw new ApiError('VITE_BEARER_TOKEN is not configured — cannot clean up old sim- accounts.')
  }

  const res = await fetch(`${SERVER_URL}/admin/users`, {
    headers: { Authorization: `Bearer ${ADMIN_BEARER_TOKEN}` },
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Listing users failed (${res.status}).`))

  const users: { id: string; username: string }[] = await res.json()
  const stale = users.filter(u => u.username.startsWith('sim-') && !keepUserIds.has(u.id))

  const results = await Promise.allSettled(
    stale.map(u => fetch(`${SERVER_URL}/admin/users/${u.id}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${ADMIN_BEARER_TOKEN}` },
    }))
  )

  return results.filter(r => r.status === 'fulfilled' && r.value.ok).length
}

// ---- The app coming to the front / going to the background ---------------------------------------
// What the real app is to send on each foreground/background (POST /me/activity), so the admin Activity
// tab can show when it was open. A server without the endpoint answers 404, which is ignored.

export type AppState = 'front' | 'back'

export async function reportAppState(user: AuthedUser, state: AppState): Promise<void> {
  const res = await fetch(`${SERVER_URL}/me/activity`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${user.accessToken}` },
    body: JSON.stringify({ kind: state === 'front' ? 'app_opened' : 'app_backgrounded' }),
  })
  if (!res.ok && res.status !== 404) throw new ApiError(await errorMessage(res, `Reporting the app state failed (${res.status}).`))
}

// ---- Go (casual) runs ----------------------------------------------------------------------------
// A phone can go out on its own instead of joining a session, like the app's Go sign. These make the
// same requests the app does, so the server records the run in its activity events (the admin
// Activity tab). See .ai/plans/activity-flow.md.

export type GoRun = { sessionId: string; runId: string }
export type WatchLink = { token: string; path: string }

// Go, crew and groups are features an admin switches on per account (or for everyone in config). A sim
// account is new, so it gets all three: then any two phones can be made crew, Go or not, and any phone
// can join a group. Needs VITE_BEARER_TOKEN, like deleteSimAccounts.
const SIM_FEATURES = ['casual', 'crew', 'groups']

export async function enableGo(user: AuthedUser): Promise<void> {
  if (!ADMIN_BEARER_TOKEN) {
    throw new ApiError('VITE_BEARER_TOKEN is not configured — cannot switch Go and crew on for this account.')
  }
  const res = await fetch(`${SERVER_URL}/admin/users/${user.userId}/features`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${ADMIN_BEARER_TOKEN}` },
    body: JSON.stringify({ features: SIM_FEATURES }),
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Switching Go, crew and groups on failed (${res.status}).`))
}

// ---- Groups ----------------------------------------------------------------------------------------
// For an account with "groups", joining a session by invite code is a group join (see
// .ai/plans/go-and-groups.md): the same request as joinSession, but the phone doesn't track in the
// session. It keeps posting to its Go run, and the group's members see each other there by name.
// Leaving is leaveSession.

export async function joinGroup(user: AuthedUser, inviteCode: string, displayName: string): Promise<SessionMembership> {
  await enableGo(user)
  return joinSession(user, inviteCode, displayName)
}

// ---- Crew ------------------------------------------------------------------------------------------
// The app's way: one makes a single-use crew code, the other redeems it, and they're crew both ways.

export async function makeCrew(a: AuthedUser, b: AuthedUser): Promise<void> {
  // A phone that joined a session never had the features switched on.
  await Promise.all([enableGo(a), enableGo(b)])
  const codeRes = await fetch(`${SERVER_URL}/me/crew/codes`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${a.accessToken}` },
  })
  if (!codeRes.ok) throw new ApiError(await errorMessage(codeRes, `Making a crew code failed (${codeRes.status}).`))
  const { code } = await codeRes.json() as { code: string }

  const redeemRes = await fetch(`${SERVER_URL}/me/crew/codes/${encodeURIComponent(code)}/redeem`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${b.accessToken}` },
  })
  if (!redeemRes.ok) throw new ApiError(await errorMessage(redeemRes, `Joining the crew failed (${redeemRes.status}).`))
}

// Removes them from each other's crew (the server does both sides).
export async function removeCrew(a: AuthedUser, b: AuthedUser): Promise<void> {
  const res = await fetch(`${SERVER_URL}/me/crew/${b.userId}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${a.accessToken}` },
  })
  if (!res.ok && res.status !== 404) throw new ApiError(await errorMessage(res, `Removing crew failed (${res.status}).`))
}

// Positions for the run are posted to the returned sessionId with postLocation, as for a session.
export async function startGoRun(user: AuthedUser): Promise<GoRun> {
  const res = await fetch(`${SERVER_URL}/casual/start`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${user.accessToken}` },
  })
  if (res.status === 404) throw new ApiError('Go is switched off on this server (404).')
  if (!res.ok) throw new ApiError(await errorMessage(res, `Starting the Go run failed (${res.status}).`))
  return res.json()
}

export async function stopGoRun(user: AuthedUser): Promise<void> {
  await fetch(`${SERVER_URL}/casual/stop`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${user.accessToken}` },
  })
}

export async function createWatchLink(user: AuthedUser): Promise<WatchLink> {
  const res = await fetch(`${SERVER_URL}/casual/watch-link`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${user.accessToken}` },
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Making a watch link failed (${res.status}).`))
  return res.json()
}

// One look at a watch link from a pretend browser, the way the watch page polls it. A new browserId
// counts as a new viewer ("Someone watched ×2"); the same one again doesn't.
export async function watchOnce(link: WatchLink, browserId: string): Promise<void> {
  const res = await fetch(`${SERVER_URL}${link.path}`, {
    headers: { 'X-Browser-Id': browserId, 'X-Client-Id': 'web-watch' },
    cache: 'no-store',
  })
  if (!res.ok) throw new ApiError(await errorMessage(res, `Watching failed (${res.status}).`))
}

// Session names must be 4-8 letters/digits/dash/underscore and unique server-side; padding
// with a random suffix keeps repeated "Create session" clicks in the same dev session from
// colliding on a shared default name.
export function randomSessionName(): string {
  return `SIM${Math.random().toString(36).slice(2, 7).toUpperCase()}`.slice(0, 8)
}
