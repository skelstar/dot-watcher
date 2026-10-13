import { useSyncExternalStore } from 'react'
import { readSession, subscribe, type AuthSession } from './authStorage.ts'

// The viewer's signed-in session, or null when browsing anonymously. Updates when this tab or any
// other tab signs in or out.
export function useAuthSession(): AuthSession | null {
  return useSyncExternalStore(subscribe, () => readSession(), () => null)
}
