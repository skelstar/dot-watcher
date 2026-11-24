// Per-account feature grants on the admin page (server: PUT /admin/users/{id}/features).

/**
 * The account's granted features with `feature` turned on or off. The endpoint replaces the whole list, so
 * everything else the account has (including features the page doesn't know) is kept. Sorted, as the server
 * stores it.
 */
export function withFeature(granted: string[], feature: string, on: boolean): string[] {
  const rest = granted.filter(f => f !== feature)
  return on ? [...rest, feature].sort() : rest
}
