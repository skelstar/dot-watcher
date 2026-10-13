# Web client: subtle sign-in path

First slice of the privacy work (concept: "Dot Watcher Sessions and Races 'grey dots'" artifact,
claude.ai/artifact/KGmYJuxcdL3o1RhbneTUtm). The web viewer is anonymous today (`/code/{INVITE}` reads
`/session-invites/{code}/...` with no identity). This adds a way to sign in from the web client without
changing anything for people who just open a link and watch.

The point of this slice is plumbing: a viewer identity on the web, ready for later steps (guest-to-account
prompt on a second runner's invite, staff code for race directors, server-side redaction). Nothing about
what a viewer sees changes yet.

## Decisions
- Subtle means: never shown as a gate, modal or banner. Anonymous viewing stays exactly as it is.
- Reuse the existing accounts and endpoints (`POST /auth/login`, `/auth/register`, `/auth/logout`).
  No server changes in this slice.
- Registration is included, so family and support crew who don't run can make an account on the web.
- Session lifetime matches the app: use the server's `expiresAt` as given, no web-specific shortening.
- Entry point is a small "Sign in" link in the map attribution block, beside Privacy / Terms
  (`LEGAL_LINKS_ATTRIBUTION` in `map/mapStyle.ts`). It is icon-only: a head-in-a-circle glyph (inline
  SVG, `aria-label="Account"` and a title tooltip). Signed-in state is shown by filling the glyph, not by
  changing its size or adding text.
- Sign-in opens a small panel over the map; it does not navigate away, so the viewer keeps their place.
- Token is kept in `localStorage` (not `sessionStorage` like the admin panel) so it is shared across tabs,
  matching the concept's storage reasoning. All reads/writes wrapped in try/catch, like `apiHeaders.ts`.

## 1. Auth state (`client/src/auth.ts`, new)
- `getSession()`: `{ token, expiresAt, user: { id, username, displayName } } | null`; returns null and
  clears storage if `expiresAt` has passed.
- `signIn(username, password)`: `POST ${SERVER_URL}/auth/login` with `apiHeaders(null, 'web-map')`.
  Map 401 -> "Wrong username or password", 429 -> "Too many attempts, try again later" (honour
  `Retry-After`), anything else -> generic error.
- `register(username, password, displayName)`: `POST ${SERVER_URL}/auth/register`. The response is the
  same `AuthResponse` as login, so the viewer is signed in immediately. Map 409 -> "That username is
  taken"; for 400 show the server's `error` text (username 3-64 chars, password 8+, display name 1-80).
- `signOut()`: best-effort `POST /auth/logout`, then clear storage regardless of the result.
- Change notification: a tiny subscribe API plus the `storage` event, so other open tabs update
  (same mechanism the concept uses for the watch list).
- `useAuth()` hook for components.

## 2. Send the token (`client/src/apiHeaders.ts`)
- `apiHeaders()` already takes an optional token. Default it to the stored session token when the
  caller passes none, except for `web-admin` calls, which keep their own bearer token.
- Viewer requests (`/session-invites/...`) start carrying `Authorization` when signed in. The server
  ignores it today. Confirm that before shipping: check that no `/session-invites` route rejects or
  rate-limits differently when a token is present.

## 3. UI
- `SignInPanel.tsx` (new): two modes, Sign in (username, password) and Create account (username,
  password, initials), toggled by a text link under the form. Inline error text. Dismiss on Escape or
  outside tap. The initials field mirrors the app's "Your initials" box (max 2 characters), since the
  account's display name is a 2-letter tag by design (see the comment in `AuthController.Register`).
  Short line under Create account: why an account (follow runners across devices), no more. Style to match `InvalidInvitePrompt` / the toasts in `App.tsx` (plain inline
  styles, system-ui).
- Signed-in state in the same panel: "Signed in as {displayName}" and Sign out.
- Attribution link: `LEGAL_LINKS_ATTRIBUTION` is a static string given to MapLibre once at map creation,
  so it cannot re-render when sign-in state changes. Approach: keep the string static and append a fixed
  `<a href="#account" class="dw-account">` containing the inline SVG glyph, and handle the click with
  event delegation on the map container (MapLibre injects the attribution HTML). Reflect signed-in state
  by toggling a CSS class on that element from `useAuth()`.
- Not shown on `/`, `/join/{code}`, legal pages or `/admin`. Landing page copy is unchanged.

## 4. Registration
- Included in this slice (see Decisions). Only username/password/initials, same as the apps. Other
  methods (Sign in with Apple, phone SMS) stay open in the concept and are not part of this.
- The register call is unauthenticated and creates accounts. `AuthAttemptLimiter` is used on login only;
  `/auth/register` has no rate limiting (checked in `AuthController.cs`). Opening it to a browser form
  makes account spam easier, so a small per-IP limit on register is a recommended server follow-up, kept
  out of this slice unless you want it in.

## 5. Tests
- Unit tests in `client/src` (vitest, as in `sessionLiveness.test.ts`): `auth.ts` expiry handling,
  corrupt storage JSON, error mapping (login and register), `apiHeaders` token default vs `web-admin` override.
- No server tests needed (no server change).
- Verification is left to CI (`client-node.yml`) per AGENTS.md; manual check in the browser afterwards:
  open a `/code/...` link anonymously (unchanged), sign in, reload, open a second tab, sign out, create an account.

## Out of scope (later slices)
- Guest-follow limit and the sign-in prompt on a second runner's invite.
- Staff code / race director view and its login requirement.
- Server-side redaction (grey dots) and any use of the viewer token by the server.
- Password reset, account deletion UI, Sign in with Apple, phone numbers, register rate limiting.

## Open questions
- Add per-IP rate limiting to `/auth/register` as part of this slice, or as a separate follow-up?
