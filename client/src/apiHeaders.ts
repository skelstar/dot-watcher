// Bump when a change could break old clients — renamed fields, changed validation, reinterpreted
// values — not just additions. Paired with server config MinimumApiVersion.
const API_VERSION = 1

export function apiHeaders(token?: string | null, clientId: string = 'web-map'): Record<string, string> {
  return {
    'X-Api-Version': String(API_VERSION),
    'X-Client-Id': clientId,
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  }
}
