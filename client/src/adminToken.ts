// The admin bearer token, kept per tab in sessionStorage by AdminPanel's sign-in. Pages under /admin
// that are reached by navigating in the same tab (the Go run replay) read it from here.
export const BEARER_TOKEN_KEY = 'adminBearerToken'

export function readAdminToken(): string {
  try {
    const stored = sessionStorage.getItem(BEARER_TOKEN_KEY)
    if (stored) return stored
  } catch {
    // Storage blocked: fall through to the dev default.
  }
  return (import.meta.env.VITE_BEARER_TOKEN as string | undefined) ?? ''
}
