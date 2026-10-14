import { readSession } from './authStorage.ts'

// Bump when a change could break old clients — renamed fields, changed validation, reinterpreted
// values — not just additions. Paired with server config MinimumApiVersion.
const API_VERSION = 1

const BROWSER_ID_KEY = 'dotwatcher-browser-id'
let cachedBrowserId: string | null | undefined

// Identifies this browser profile for log correlation, the web counterpart to the iOS app's
// Keychain-backed X-Device-Id. Persisted in localStorage, so — unlike Keychain — it's wiped by
// "Clear browsing data" or a private/incognito window: it identifies "this browser profile", not
// "this physical device".
function browserId(): string | null {
  if (cachedBrowserId !== undefined) return cachedBrowserId
  try {
    const existing = localStorage.getItem(BROWSER_ID_KEY)
    const id = existing ?? crypto.randomUUID()
    if (!existing) localStorage.setItem(BROWSER_ID_KEY, id)
    cachedBrowserId = id
  } catch {
    cachedBrowserId = null
  }
  return cachedBrowserId
}

// `token` undefined: the map viewer sends the signed-in user's token if there is one (the server
// ignores it on the invite-code endpoints today). null: explicitly anonymous (login/register). A
// string: that exact token (route upload token, admin bearer). 'web-admin' never picks up the viewer
// session since it carries its own bearer token.
export function apiHeaders(token?: string | null, clientId: string = 'web-map'): Record<string, string> {
  const id = browserId()
  const bearer = token === undefined && clientId === 'web-map' ? readSession()?.accessToken : token
  return {
    'X-Api-Version': String(API_VERSION),
    'X-Client-Id': clientId,
    ...(id ? { 'X-Browser-Id': id } : {}),
    ...(bearer ? { Authorization: `Bearer ${bearer}` } : {}),
  }
}
