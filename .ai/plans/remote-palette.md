# Remote palette — Plan

*DotWatcher — plan, October 2026*

Goal: change the start-screen button colours (Go, Invite code, Create, the event sign, the screen background) without
shipping an app release. The server holds the palette; both apps read it, cache the last good copy, and fall back to
the colours built into the app. Related: [`crew-feature-flag.md`](crew-feature-flag.md) (the same "config, not a
release" idea for features) and the colour playground at `client/public/palette-playground.html` (the Palette tab on `/admin`).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally. Nothing in this plan is built yet.

---

## Decisions taken

- **Config, not a new endpoint.** The palette is a `config` object added to responses that already exist: the user
  object returned by login and register, and `GET /me`. No new route.
- **The palette's source is server config.** A `Palette` section in `appsettings.json` (like `Features:EnabledForAll`),
  so changing colours is a config change and a restart, with no data migration. Staging and Production share a database
  but not config, so a palette can be tried on Staging first.
- **The server sends final hex values.** The playground derives some colours (Invite/Create background and Go text are
  shade-of-another-colour). The apps do not repeat that maths: the server sends the finished hex for every token, and
  the playground's "Copy values" is the way to get them.
- **Built-in colours stay as the fallback.** Today's `SignPalette` (Android) and its iOS equivalent remain in the app.
  Anything missing, invalid or unreadable means the built-in colours are used. A bad palette can never break the screen.
- **Colours only.** Font, layout, copy and any new colour token still need a release. The palette has a `version` so a
  future token can be added without older builds misreading the object.
- **Applied as soon as it arrives, with a fade.** Clients apply the saved palette at start-up and when the app returns to the front, so
  there is no flash of old colours, then fetch the server's palette and, if it differs, fade it in (about 0.6s) once the request returns. No blank loading screen: the fetch is bounded by the normal request time, and a slow or failed one just leaves the saved colours until the next open. Pull-down refresh does not trigger it.

## The palette

Eight tokens, matching the playground's Colours panel. Each is a 6-digit hex string with a leading `#`.

| Key | Used for |
| --- | --- |
| `goBg` | Go button fill; also the dot behind the initials |
| `goFg` | Go text and arrow |
| `goBar` | Go top bar |
| `altBg` | Invite code / Create fill; also the initials colour in the dot |
| `altFg` | Invite code / Create text and frame |
| `accent` | Event sign fill |
| `accentInk` | Event sign text, frame and top bar |
| `screen` | Screen background behind the buttons |

Response shape (additive; the existing user fields are unchanged):

```json
{
  "userId": "…",
  "username": "…",
  "displayName": "…",
  "features": ["crew"],
  "config": {
    "palette": {
      "version": 1,
      "goBg": "#6f1a07", "goFg": "#f7f3e3", "goBar": "#af9164",
      "altBg": "#f7f3e3", "altFg": "#2b2118",
      "accent": "#af9164", "accentInk": "#2b2118",
      "screen": "#ffffff"
    }
  }
}
```

`config` is the home for any later server-driven setting (another palette, copy, a toggle), so the next one is a key
inside it, not a new endpoint. `config.palette` is omitted when the server has no `Palette` section, and clients treat
that as "use the built-in colours".

## Server

### S1. Config and validation
- [x] `Palette` config section (the eight keys and `Version`) read by `PaletteProvider`. Deliberately not added to `appsettings.json` yet: rollout step 1 merges with no palette.
- [x] Validate once at start-up: every colour matches `^#[0-9a-fA-F]{6}$`, and the three text-on-background pairs
      (`goFg` on `goBg`, `altFg` on `altBg`, `accentInk` on `accent`) have contrast of at least 4.5:1. If it fails,
      log a warning and serve no palette, so a typo in config means built-in colours rather than unreadable buttons.
- [x] The same check runs in the playground as the contrast badge, so a value that looks fine there passes here.

### S2. Exposing it
- [x] `AuthenticatedUser` gains an optional `Config` (null for users read from a bearer token, like `Features`).
      Filled in by login, register and `GET /me` in `AuthController`.
- [x] `docs/api-reference.html` (login response and `GET /me`) and `server/README.md` updated.
- [x] `Config` is a record with an optional `Palette` so a later setting adds a property, not a new response shape.

### S3. Tests (`tests/DotWatcher.Server.Tests`)
- [x] `GET /me`, login and register include `config.palette` when configured and omit it when not.
- [x] An invalid hex, or a pair under 4.5:1, results in no palette (and not a failed response).
- [x] `DotWatcherApiFactory` default config has no palette, so existing tests are unchanged.
- [x] CI is the first run of any of this. Passed on the server PR (#273).

## Clients (separate PRs)

Neither app calls `GET /me` today. Both already call several `/me/…` endpoints on launch and foreground (crew probe,
events nearby, casual settings), so this adds one call, not a new kind of traffic.

- [x] **Android** — `GET me` added to `DotWatcherApi`; a `PaletteStore` reads the cached palette at start-up, hands
      `SignPalette` its colours, and refreshes from `GET me` on launch and resume. Storage: the existing preferences
      store. Falls back to the current `SignPalette` values. PR #276, CI green.
- [x] **iOS** — same shape: a `PaletteStore`, cached in `UserDefaults`, refreshed on launch and when the app comes to
      the front. PR #275. There is no iOS CI and no test target, so it has not been compiled yet.
- [x] **Both** re-validate what they receive (hex format, `version` they understand, contrast of the three pairs) and
      ignore the whole palette if anything fails. All-or-nothing, so a half-applied palette cannot happen.
- [x] **Both** also apply a palette from the login or register response, so a new sign-in does not wait for `GET /me`. Both save it for the next launch, as decided above.
- [x] **Web** — not needed. The landing page does not use these colours.

## Playground

- [x] Add a "JSON" tab to Copy values in `palette-playground.html`, emitting the palette block above, ready to paste into
      `appsettings.json`.

## Backwards compatibility (old app builds)

- [x] The only change to an existing response is the extra `config` key on the user object. iOS `AppUser` is `Codable`
      (unknown keys ignored); Android decodes with `ignoreUnknownKeys = true`; web checks only the fields it needs.
      The same reasoning as the `features` key in the crew plan, and still to be re-checked here.
- [x] Old builds never read `config`, so they keep the colours they shipped with. A palette change only reaches
      builds that include the client PRs.
- [ ] **Manual check after the server PR deploys to Staging:** run an older TestFlight/Android build against Staging
      and confirm sign-in and the main screen are unaffected.

## Rollout

1. Merge the server PR with no `Palette` section. Nothing changes for anyone.
2. Ship the client PRs, both defaulting to the built-in colours. Confirm they behave identically with no palette.
3. Add the chosen palette to Staging config, restart, confirm the apps pick it up on the next launch, and that a
   deliberately bad value (bad hex, low contrast) leaves the built-in colours in place.
4. Copy the palette to Production config. To change colours later, edit config and restart; no release.
5. To revert, remove the `Palette` section (or set it back) and restart. Apps return to the built-in colours the next
   time they refresh.

## Palette library and app-version assignments (`/admin/palettes`)

Config is still the base, but palettes can also be managed at runtime. Palettes are saved in the database (`palettes`)
and **reach no phone until one is assigned to an app version** (`palette_assignments`), so they can be created and
tweaked freely. PR "Palette library".

- `GET` / `POST /admin/palettes`, `GET` / `PUT` / `DELETE /admin/palettes/{id}`: list, create, read, update, delete.
  A name (30 chars) is required; `label` (32 chars, e.g. an app version) is optional. Both are informational.
  Deleting an assigned palette is refused (`409`).
- `GET /admin/palettes/assignments`, `PUT` / `DELETE /admin/palettes/assignments/{appVersion}`: which palette each app
  version gets. `appVersion` is the part of `X-App-Version` before the build number (`1.2.2` from `1.2.2 (20)`), or
  `default` for every other version.
- **A phone gets, in order:** the palette assigned to its exact version, the `default` one, the `Palette` config
  section, then none (built-in colours). Both apps already send `X-App-Version` on every call that returns
  `config.palette`, so the apps need no change.
- **Delivery to the phone is unchanged:** `config.palette` on login, register and `GET /me`, applied on launch and on
  return to the front (#275, #276). Assignments and palettes are cached for about five seconds.
- **Editing an assigned palette is live** for those phones. To stage a change, save a new palette and assign it when
  ready.
- Staging and Production share the database, so assignments made on either apply to both once both run this code.

### Testing it on Staging

The quickest way is the Bruno collection in `tools/Bruno/palette` (PR #279): open it in Bruno, pick the `staging`
environment, fill in `username`, `password` and `adminToken`, and run it. It sets `appVersion` to a test value, so
only requests sending that `X-App-Version` see the test palette. By hand, with `API=https://dot-watcher-staging.skelstar.io/api`,
`ADMIN` the staging `BearerToken` and a staging account:

```sh
me() { curl -s $API/me -H "Authorization: Bearer $TOKEN" -H "X-App-Version: 9.9.9 (1)" | jq '.config.palette'; }
```

1. `POST /admin/palettes` with a low-contrast pair: `400` naming the contrast. With no `name`: `400`.
2. `POST /admin/palettes` with a good palette: `201` with an `id`. `GET /admin/palettes` lists it with `assignedTo: []`.
3. `me`: no test palette yet (it is not assigned).
4. `PUT /admin/palettes/assignments/9.9.9` with `{ "paletteId": "<id>" }`: `200`. `me` now shows the palette; a request
   with another `X-App-Version` does not.
5. `DELETE /admin/palettes/<id>`: `409` while assigned.
6. `PUT /admin/palettes/<id>` with a changed colour: `me` shows the change (live for assigned palettes).
7. `DELETE /admin/palettes/assignments/9.9.9`: `204`; `me` no longer shows it. Then `DELETE /admin/palettes/<id>`: `204`.

Use a version no real build has (like `9.9.9`) so real phones are untouched, and clean up afterwards: assignments made
on Staging apply to Production once Production runs this code. `500`s on step 2 point at the `palettes` tables not
being created; check the server log.

## Open questions

- **Signed out.** `GET /me` needs an account. If the start screen can ever be shown before sign-in, the first launch
  uses the built-in colours until sign-in. Check whether that screen is reachable without an account.
- **Try before everyone.** Assigning a palette to a version nobody runs yet (or the next unreleased one) is the
  preview. Per-account preview through the feature flags is still possible but not built.
- **Staleness.** A palette is applied on the next launch after it is fetched, so an edit reaches a given phone one
  launch later. Fine for colours; revisit if it ever needs to be immediate.
- **Dark mode.** The playground has one palette. If dark mode needs its own, the object becomes `light` and `dark`
  sets under a bumped `version`.
- **Where the values live long term.** Config needs a restart, so palettes are now also stored in the database
  (`/admin/palettes`) with no change to the response shape or the apps. A library and assign UI in the admin Palette
  tab is not built yet.
- **Newer versions.** An assignment matches one exact app version. A new release has no palette until one is assigned
  to it, or to `default`.

## Branch / PR naming

Per the project convention: `remote-palette` for this plan; server PR "Palette: serve from config (server)", then
`remote-palette-android` and `remote-palette-ios` for the clients.
