# iOS universal invite links

Branch: `ios-universal-invite-links` (from `staging`).

## Goal

Someone shares a session by WhatsApp (or any messenger). The recipient taps the
link on their iPhone. If DotWatcher is installed, it opens and the invite code
is already filled in on the onboarding screen. If it isn't installed, the link
opens the web client instead.

## Approach

Use **Universal Links** (https links the app claims), not a custom URL scheme.
Messengers make https links tappable and they fall back to the web.

### Decision: a new `/join/{CODE}` path for the app

`/code/{CODE}` is already the web viewer link. If the app claimed it, anyone
who only wants to watch would get bounced into the app. So:

- `/join/{CODE}` is claimed by the app and means "join this session as a runner".
- `/code/{CODE}` stays the web viewer link and is not claimed by the app.

The client README already mentions `/join/INVITECODE`, but `parseUrl()` in
`client/src/App.tsx` doesn't handle it yet, so today it falls back to the landing
page.

### Hosts involved

| Env | Web host | Source |
|---|---|---|
| Prod | `dot-watcher.skelstar.io` | `DOTWATCHER_WEB_BASE_URL` |
| Staging | `dot-watcher-staging.skelstar.io` | `DOTWATCHER_STAGING_WEB_BASE_URL` |

Both need to serve the AASA file, and the app needs an `applinks:` entry for each.
Bundle ID `io.skelstar.DotWatcher`, team `8A9JZATC8G`.

## Checklist

### 1. Web: serve the association file

- [x] Add `client/public/.well-known/apple-app-site-association` (no extension)
      with `appID` `8A9JZATC8G.io.skelstar.DotWatcher` and path `/join/*`.
- [x] `client/nginx.conf` serves it as `application/json` (the client is an nginx container
      serving `dist/`, so Vite copies `public/` as-is).
- [ ] Confirm after deploy that it's served over HTTPS with no redirect, as `application/json`.
- [ ] Same file reachable on the staging host (same nginx image, so expected to work; confirm after deploy).

### 2. Web: `/join/{CODE}` fallback page

- [x] `parseUrl()` in `client/src/App.tsx` recognises `/join/{CODE}`.
- [x] Page for people without the app: shows the code, an App Store link, and a
      "just watch" link to `/code/{CODE}`.
- [x] Update the route comment above `parseUrl()` and the client README.

### 3. iOS: claim the links

- [x] Add the Associated Domains entitlement to `DotWatcher.entitlements`:
      `applinks:dot-watcher.skelstar.io` and `applinks:dot-watcher-staging.skelstar.io`.
- [ ] Enable Associated Domains on the App ID in the Apple Developer portal
      (manual step, outside the repo) and refresh provisioning.

### 4. iOS: handle the link

- [x] `.onOpenURL` in `ContentView` (via `InviteLink.code(from:)`) parses `/join/{CODE}`
      (case-insensitive, trims, ignores anything that isn't a valid 6-character code).
- [x] Put the code into `noSessionInviteCode` so the code boxes show it.
- [x] If the user is signed out, keep the code across the sign-in sheet and prefill after.
- [x] If the user is already in a session, an alert asks "Join session CODE?" (nothing happens if they are already in it).
- [x] Joining still needs a tap on "Join Session". The link only prefills.
- [ ] Unit test for the URL parsing. Blocked: the Xcode project has no test target. Add one, or accept manual verification.

### 5. Change the share message

The current message (`ContentView.swift`, `headerSection`) is:
"Join my DotWatcher session! / Invite code: CODE / https://…/code/CODE".

- [x] Primary link becomes `…/join/{CODE}` ("tap to join in the app").
- [x] Keep the plain invite code in the text, for people who copy it by hand.
- [x] Add the `…/code/{CODE}` link as "Just want to watch?".
- [x] Message reads sensibly if the link isn't tappable.
- [x] Android share message (`LiveMapScreen.kt`) updated to match. Android App Links remain a later task.

### 6. Verify (real device, signed build, staging first)

CI only builds, so these are manual checks.

- [ ] AASA file validates (Apple's CDN fetch, or `curl` the host and check headers).
- [ ] Tap the link in WhatsApp, Messages and Notes: app opens with code prefilled.
- [ ] App not installed: link opens the web fallback page.
- [ ] Signed out: code survives sign-in.
- [ ] Bad or short code: nothing is prefilled and no crash.
- [ ] Long-press the link, "Open in Safari" route still works.

### 7. Wrap up

- [x] `ios/README.md`: document the link flow and the entitlement.
- [ ] Open the PR against `staging`.

## Open questions

- ~~Where is `client/` deployed~~ — nginx container (`client/Dockerfile`); content type set in `client/nginx.conf`.
- What should a link do if the user is already in a session? Prefill only and
  show a hint, or ignore it?
- Should the link auto-join when the user is signed in, or always wait for a tap? (Plan: always wait.)
- ~~App Store URL for the web fallback page~~ — already in `LandingPage.tsx`, now shared via `client/src/appStore.ts`.

## Out of scope

- Android App Links (`assetlinks.json` and an intent filter). Follow-up.
- Carrying the code through an App Store install (deferred deep links). The Paste button covers it.
