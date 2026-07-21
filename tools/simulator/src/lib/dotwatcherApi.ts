// Minimal client for the auth + session + location endpoints, used by the Convergence
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
    userId: data.user.id,
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
  heading: number | null
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

// Session names must be 4-8 letters/digits/dash/underscore and unique server-side; padding
// with a random suffix keeps repeated "Create session" clicks in the same dev session from
// colliding on a shared default name.
export function randomSessionName(): string {
  return `SIM${Math.random().toString(36).slice(2, 7).toUpperCase()}`.slice(0, 8)
}
