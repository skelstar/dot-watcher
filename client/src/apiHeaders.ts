// Bump when a change could break old clients — renamed fields, changed validation, reinterpreted
// values — not just additions. Paired with server config MinimumApiVersion.
const API_VERSION = 1

export function apiHeaders(token?: string | null): Record<string, string> {
  return {
    'X-Api-Version': String(API_VERSION),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  }
}
