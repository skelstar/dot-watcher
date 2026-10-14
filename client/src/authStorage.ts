// The web viewer's optional sign-in: where the session lives and how components hear about changes.
// No network and no React in here, so it can be unit-tested under plain node.

export interface AuthUser {
  userId: string
  username: string
  displayName: string
}

// Same shape as the server's AuthResponse (AuthModels.cs), so a login/register response is stored as is.
export interface AuthSession {
  accessToken: string
  expiresAt: string
  user: AuthUser
}

export type KeyValueStore = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

export const AUTH_STORAGE_KEY = 'dotwatcher-auth'

// localStorage rather than sessionStorage (unlike the admin panel's token): a sign-in should carry
// across tabs. Access can throw (private mode, blocked site data), so it is always wrapped.
function defaultStore(): KeyValueStore | null {
  try {
    return globalThis.localStorage ?? null
  } catch {
    return null
  }
}

export function isAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  const user = v.user as Record<string, unknown> | null | undefined
  return (
    typeof v.accessToken === 'string' && v.accessToken !== '' &&
    typeof v.expiresAt === 'string' && !Number.isNaN(Date.parse(v.expiresAt)) &&
    typeof user === 'object' && user !== null &&
    typeof user.userId === 'string' &&
    typeof user.username === 'string' &&
    typeof user.displayName === 'string'
  )
}

// Returns the stored session, or null when there is none, it is corrupt, or it has expired (a bad
// entry is removed so it isn't re-parsed on every request).
function parseSession(raw: string, now: number): AuthSession | null {
  try {
    const parsed: unknown = JSON.parse(raw)
    if (!isAuthSession(parsed)) return null
    return Date.parse(parsed.expiresAt) > now ? parsed : null
  } catch {
    return null
  }
}

// useSyncExternalStore calls the snapshot function on every render and needs a stable reference
// while nothing changed, so the parsed session is cached against the raw stored string.
let cache: { raw: string; session: AuthSession } | null = null

export function readSession(store: KeyValueStore | null = defaultStore(), now: number = Date.now()): AuthSession | null {
  if (!store) return null
  let raw: string | null
  try {
    raw = store.getItem(AUTH_STORAGE_KEY)
  } catch {
    return null
  }
  if (raw === null) {
    cache = null
    return null
  }
  if (cache && cache.raw === raw && Date.parse(cache.session.expiresAt) > now) return cache.session

  const session = parseSession(raw, now)
  if (!session) {
    cache = null
    try { store.removeItem(AUTH_STORAGE_KEY) } catch { /* nothing to do */ }
    return null
  }
  cache = { raw, session }
  return session
}

const listeners = new Set<() => void>()

function notify(): void {
  listeners.forEach(listener => listener())
}

export function writeSession(session: AuthSession, store: KeyValueStore | null = defaultStore()): void {
  // If storage is unavailable the viewer simply stays signed out; there is no in-memory fallback.
  try { store?.setItem(AUTH_STORAGE_KEY, JSON.stringify(session)) } catch { /* nothing to do */ }
  notify()
}

export function clearSession(store: KeyValueStore | null = defaultStore()): void {
  cache = null
  try { store?.removeItem(AUTH_STORAGE_KEY) } catch { /* nothing to do */ }
  notify()
}

// Changes made in this tab notify directly; changes made in another tab arrive as a 'storage' event.
export function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  const onStorage = (event: StorageEvent) => {
    if (event.key === AUTH_STORAGE_KEY || event.key === null) listener()
  }
  if (typeof window !== 'undefined') window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    if (typeof window !== 'undefined') window.removeEventListener('storage', onStorage)
  }
}
