import assert from 'node:assert/strict'
import test, { beforeEach } from 'node:test'
import { apiHeaders } from './apiHeaders.ts'
import { register, signIn, signOut } from './auth.ts'
import { AUTH_STORAGE_KEY, readSession, writeSession, type AuthSession, type KeyValueStore } from './authStorage.ts'

// auth.ts and apiHeaders.ts read the default store from globalThis.localStorage, so tests install a
// fake one there.
const data = new Map<string, string>()
const store: KeyValueStore = {
  getItem: key => data.get(key) ?? null,
  setItem: (key, value) => { data.set(key, value) },
  removeItem: key => { data.delete(key) },
}
Object.defineProperty(globalThis, 'localStorage', { value: store, configurable: true, writable: true })

beforeEach(() => { data.clear() })

const SERVER = 'https://example.test/api'

function authResponse(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: 'tok-abc',
    expiresAt: '2999-01-01T00:00:00Z',
    user: { userId: 'u1', username: 'sam', displayName: 'SK' },
    ...overrides,
  }
}

interface Call { url: string; init: RequestInit }

function fakeFetch(respond: () => Response | Promise<Response>): { fetchImpl: typeof fetch; calls: Call[] } {
  const calls: Call[] = []
  const fetchImpl = (async (url: string | URL | Request, init?: RequestInit) => {
    calls.push({ url: String(url), init: init ?? {} })
    return respond()
  }) as typeof fetch
  return { fetchImpl, calls }
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

test('signIn posts trimmed credentials, stores the session and returns it', async () => {
  const { fetchImpl, calls } = fakeFetch(() => json(authResponse()))
  const result = await signIn(SERVER, '  sam ', 'hunter2hunter2', fetchImpl)

  assert.equal(result.ok, true)
  assert.equal(calls[0].url, `${SERVER}/auth/login`)
  assert.deepEqual(JSON.parse(String(calls[0].init.body)), { username: 'sam', password: 'hunter2hunter2' })
  assert.equal(readSession()?.accessToken, 'tok-abc')
})

test('signIn never sends a previously stored token', async () => {
  writeSession(authResponse({ accessToken: 'old-token' }))
  const { fetchImpl, calls } = fakeFetch(() => json(authResponse()))
  await signIn(SERVER, 'sam', 'pw', fetchImpl)

  const headers = calls[0].init.headers as Record<string, string>
  assert.equal(headers.Authorization, undefined)
})

test('signIn maps 401 and 429 to friendly messages and stores nothing', async () => {
  const wrong = await signIn(SERVER, 'sam', 'bad', fakeFetch(() => new Response(null, { status: 401 })).fetchImpl)
  assert.deepEqual(wrong, { ok: false, error: 'Wrong username or password.' })

  const locked = await signIn(SERVER, 'sam', 'bad', fakeFetch(() => json({ error: 'x' }, 429)).fetchImpl)
  assert.deepEqual(locked, { ok: false, error: 'Too many failed attempts. Please try again later.' })

  assert.equal(data.has(AUTH_STORAGE_KEY), false)
})

test('signIn reports an unreachable server', async () => {
  const fetchImpl = (async () => { throw new TypeError('network') }) as typeof fetch
  const result = await signIn(SERVER, 'sam', 'pw', fetchImpl)
  assert.equal(result.ok, false)
  assert.match((result as { error: string }).error, /reach the server/)
})

test('signIn rejects a success response of the wrong shape', async () => {
  const result = await signIn(SERVER, 'sam', 'pw', fakeFetch(() => json({ hello: 'world' })).fetchImpl)
  assert.equal(result.ok, false)
  assert.equal(data.has(AUTH_STORAGE_KEY), false)
})

test('register posts the display name and signs the viewer in', async () => {
  const { fetchImpl, calls } = fakeFetch(() => json(authResponse()))
  const result = await register(SERVER, 'sam', 'longenoughpw', ' SK ', fetchImpl)

  assert.equal(result.ok, true)
  assert.equal(calls[0].url, `${SERVER}/auth/register`)
  assert.deepEqual(JSON.parse(String(calls[0].init.body)), { username: 'sam', password: 'longenoughpw', displayName: 'SK' })
  assert.equal(readSession()?.user.username, 'sam')
})

test('register maps 409 to a taken-username message', async () => {
  const result = await register(SERVER, 'sam', 'longenoughpw', 'SK', fakeFetch(() => json({ error: 'dup' }, 409)).fetchImpl)
  assert.deepEqual(result, { ok: false, error: 'That username is already taken.' })
})

test('register shows the server validation message on 400', async () => {
  const result = await register(
    SERVER, 'sam', 'short', 'SK',
    fakeFetch(() => json({ error: 'Password must be at least 8 characters.' }, 400)).fetchImpl,
  )
  assert.deepEqual(result, { ok: false, error: 'Password must be at least 8 characters.' })
})

test('signOut clears the session immediately and revokes the token with it', async () => {
  writeSession(authResponse({ accessToken: 'tok-to-revoke' }))
  const { fetchImpl, calls } = fakeFetch(() => new Response(null, { status: 204 }))
  await signOut(SERVER, fetchImpl)

  assert.equal(readSession(), null)
  assert.equal(calls[0].url, `${SERVER}/auth/logout`)
  assert.equal((calls[0].init.headers as Record<string, string>).Authorization, 'Bearer tok-to-revoke')
})

test('signOut still signs out locally when the server is unreachable', async () => {
  writeSession(authResponse())
  const fetchImpl = (async () => { throw new TypeError('network') }) as typeof fetch
  await signOut(SERVER, fetchImpl)
  assert.equal(readSession(), null)
})

test('apiHeaders sends the viewer token by default for the map client', () => {
  writeSession(authResponse({ accessToken: 'viewer-token' }))
  assert.equal(apiHeaders().Authorization, 'Bearer viewer-token')
})

test('apiHeaders sends no Authorization when signed out', () => {
  assert.equal(apiHeaders().Authorization, undefined)
})

test('apiHeaders keeps an explicit token and ignores the viewer session for web-admin', () => {
  writeSession(authResponse({ accessToken: 'viewer-token' }))
  assert.equal(apiHeaders('route-upload-token').Authorization, 'Bearer route-upload-token')
  assert.equal(apiHeaders('', 'web-admin').Authorization, undefined)
  assert.equal(apiHeaders('admin-bearer', 'web-admin').Authorization, 'Bearer admin-bearer')
  assert.equal(apiHeaders(null).Authorization, undefined)
})
