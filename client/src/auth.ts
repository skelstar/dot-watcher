import { apiHeaders } from './apiHeaders.ts'
import { clearSession, isAuthSession, readSession, writeSession, type AuthSession } from './authStorage.ts'

export type AuthResult = { ok: true; session: AuthSession } | { ok: false; error: string }

const GENERIC_ERROR = 'Something went wrong. Please try again.'

// Posts credentials to /auth/login or /auth/register (both answer with the same AuthResponse) and
// stores the result. `statusMessages` maps the statuses worth a specific message; a 400 falls back to
// the server's own validation text (e.g. "Password must be at least 8 characters.").
async function postCredentials(
  serverUrl: string,
  path: '/auth/login' | '/auth/register',
  body: Record<string, string>,
  statusMessages: Record<number, string>,
  fetchImpl: typeof fetch,
): Promise<AuthResult> {
  let response: Response
  try {
    response = await fetchImpl(`${serverUrl}${path}`, {
      method: 'POST',
      // null = explicitly anonymous: a stale stored token must not ride along on a login.
      headers: { ...apiHeaders(null), 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  } catch {
    return { ok: false, error: "Couldn't reach the server. Check your connection." }
  }

  if (!response.ok) {
    let serverError: string | null = null
    if (response.status === 400) {
      try {
        const data = await response.json() as { error?: unknown }
        if (typeof data.error === 'string') serverError = data.error
      } catch { /* no usable body */ }
    }
    return { ok: false, error: statusMessages[response.status] ?? serverError ?? GENERIC_ERROR }
  }

  let data: unknown
  try {
    data = await response.json()
  } catch {
    return { ok: false, error: GENERIC_ERROR }
  }
  if (!isAuthSession(data)) return { ok: false, error: GENERIC_ERROR }

  writeSession(data)
  return { ok: true, session: data }
}

export function signIn(
  serverUrl: string,
  username: string,
  password: string,
  fetchImpl: typeof fetch = fetch,
): Promise<AuthResult> {
  return postCredentials(
    serverUrl,
    '/auth/login',
    { username: username.trim(), password },
    {
      401: 'Wrong username or password.',
      429: 'Too many failed attempts. Please try again later.',
    },
    fetchImpl,
  )
}

export function register(
  serverUrl: string,
  username: string,
  password: string,
  displayName: string,
  fetchImpl: typeof fetch = fetch,
): Promise<AuthResult> {
  return postCredentials(
    serverUrl,
    '/auth/register',
    { username: username.trim(), password, displayName: displayName.trim() },
    { 409: 'That username is already taken.' },
    fetchImpl,
  )
}

// Signs out locally first so the UI never waits on the network, then revokes the token server-side
// on a best-effort basis (an unreachable server just leaves it to expire).
export async function signOut(serverUrl: string, fetchImpl: typeof fetch = fetch): Promise<void> {
  const session = readSession()
  clearSession()
  if (!session) return
  try {
    await fetchImpl(`${serverUrl}/auth/logout`, {
      method: 'POST',
      headers: apiHeaders(session.accessToken),
    })
  } catch { /* best effort */ }
}
