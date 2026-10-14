import assert from 'node:assert/strict'
import test from 'node:test'
import {
  AUTH_STORAGE_KEY,
  clearSession,
  readSession,
  subscribe,
  writeSession,
  type AuthSession,
  type KeyValueStore,
} from './authStorage.ts'

function fakeStore(initial: Record<string, string> = {}): KeyValueStore & { data: Map<string, string> } {
  const data = new Map(Object.entries(initial))
  return {
    data,
    getItem: key => data.get(key) ?? null,
    setItem: (key, value) => { data.set(key, value) },
    removeItem: key => { data.delete(key) },
  }
}

const NOW = Date.parse('2026-10-14T00:00:00Z')

function session(overrides: Partial<AuthSession> = {}): AuthSession {
  return {
    accessToken: 'tok-123',
    expiresAt: '2026-10-15T00:00:00Z',
    user: { userId: 'u1', username: 'sam', displayName: 'SK' },
    ...overrides,
  }
}

test('readSession returns null when nothing is stored', () => {
  assert.equal(readSession(fakeStore(), NOW), null)
})

test('readSession returns null when there is no storage at all', () => {
  assert.equal(readSession(null, NOW), null)
})

test('a written session reads back', () => {
  const store = fakeStore()
  writeSession(session(), store)
  assert.deepEqual(readSession(store, NOW), session())
})

test('readSession returns the same object while the stored value is unchanged', () => {
  const store = fakeStore()
  writeSession(session(), store)
  assert.equal(readSession(store, NOW), readSession(store, NOW))
})

test('an expired session reads as null and is removed', () => {
  const store = fakeStore()
  writeSession(session({ expiresAt: '2026-10-13T23:59:59Z' }), store)
  assert.equal(readSession(store, NOW), null)
  assert.equal(store.data.has(AUTH_STORAGE_KEY), false)
})

test('a session cached before it expired is not returned after it expires', () => {
  const store = fakeStore()
  writeSession(session(), store)
  assert.notEqual(readSession(store, NOW), null)
  const afterExpiry = Date.parse('2026-10-15T00:00:01Z')
  assert.equal(readSession(store, afterExpiry), null)
})

test('corrupt JSON reads as null and is removed', () => {
  const store = fakeStore({ [AUTH_STORAGE_KEY]: '{not json' })
  assert.equal(readSession(store, NOW), null)
  assert.equal(store.data.has(AUTH_STORAGE_KEY), false)
})

test('a stored value of the wrong shape reads as null', () => {
  const store = fakeStore({ [AUTH_STORAGE_KEY]: JSON.stringify({ accessToken: 'x' }) })
  assert.equal(readSession(store, NOW), null)
})

test('clearSession removes the stored session', () => {
  const store = fakeStore()
  writeSession(session(), store)
  clearSession(store)
  assert.equal(readSession(store, NOW), null)
})

test('storage that throws is treated as signed out', () => {
  const throwing: KeyValueStore = {
    getItem: () => { throw new Error('blocked') },
    setItem: () => { throw new Error('blocked') },
    removeItem: () => { throw new Error('blocked') },
  }
  assert.equal(readSession(throwing, NOW), null)
  assert.doesNotThrow(() => writeSession(session(), throwing))
  assert.doesNotThrow(() => clearSession(throwing))
})

test('subscribers are told about writes and clears in this tab, until they unsubscribe', () => {
  const store = fakeStore()
  let calls = 0
  const unsubscribe = subscribe(() => { calls += 1 })
  writeSession(session(), store)
  clearSession(store)
  assert.equal(calls, 2)
  unsubscribe()
  writeSession(session(), store)
  assert.equal(calls, 2)
})
