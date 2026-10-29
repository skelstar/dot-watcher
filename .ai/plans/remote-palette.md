# Remote palette — Plan

*DotWatcher — plan, October 2026*

Goal: change the start-screen button colours (Go, Invite code, Create, the event sign, the screen background) without
shipping an app release. The server holds the palette; both apps read it, cache the last good copy, and fall back to
the colours built into the app. Related: [`crew-feature-flag.md`](crew-feature-flag.md) (the same "config, not a
release" idea for features) and the colour playground at `.ai/mockups/button-colours.html`.

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
- **Applied on the next launch.** Clients apply the cached palette at start-up and refresh in the background, so a new
  palette appears the next time the app is opened. No flash of the old colours, no live recolouring mid-session.

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
- [ ] `Palette` section in `appsettings.json` with the eight keys and `Version`, bound to a small options record.
- [ ] Validate once at start-up: every colour matches `^#[0-9a-fA-F]{6}$`, and the three text-on-background pairs
      (`goFg` on `goBg`, `altFg` on `altBg`, `accentInk` on `accent`) have contrast of at least 4.5:1. If it fails,
      log a warning and serve no palette, so a typo in config means built-in colours rather than unreadable buttons.
- [ ] The same check runs in the playground as the contrast badge, so a value that looks fine there passes here.

### S2. Exposing it
- [ ] `AuthenticatedUser` gains an optional `Config` (null for users read from a bearer token, like `Features`).
      Filled in by login, register and `GET /me` in `AuthController`.
- [ ] `docs/api-reference.html` (login response and `GET /me`) and `server/README.md` updated.
- [ ] `Config` is a record with an optional `Palette` so a later setting adds a property, not a new response shape.

### S3. Tests (`tests/DotWatcher.Server.Tests`)
- [ ] `GET /me`, login and register include `config.palette` when configured and omit it when not.
- [ ] An invalid hex, or a pair under 4.5:1, results in no palette (and not a failed response).
- [ ] `DotWatcherApiFactory` default config has no palette, so existing tests are unchanged.
- [ ] CI is the first run of any of this.

## Clients (separate PRs)

Neither app calls `GET /me` today. Both already call several `/me/…` endpoints on launch and foreground (crew probe,
events nearby, casual settings), so this adds one call, not a new kind of traffic.

- [ ] **Android** — `GET me` added to `DotWatcherApi`; a `PaletteStore` reads the cached palette at start-up, hands
      `SignPalette` its colours, and refreshes from `GET me` on launch and resume. Storage: the existing preferences
      store. Falls back to the current `SignPalette` values.
- [ ] **iOS** — same shape: a `PaletteStore`, cached in `UserDefaults`, refreshed on launch and when the app comes to
      the front.
- [ ] **Both** re-validate what they receive (hex format, `version` they understand, contrast of the three pairs) and
      ignore the whole palette if anything fails. All-or-nothing, so a half-applied palette cannot happen.
- [ ] **Both** also apply a palette from the login or register response, so a new sign-in does not wait for `GET /me`.
- [ ] **Web** — not needed. The landing page does not use these colours.

## Playground

- [ ] Add a "JSON" tab to Copy values in `button-colours.html`, emitting the palette block above, ready to paste into
      `appsettings.json`.

## Backwards compatibility (old app builds)

- [ ] The only change to an existing response is the extra `config` key on the user object. iOS `AppUser` is `Codable`
      (unknown keys ignored); Android decodes with `ignoreUnknownKeys = true`; web checks only the fields it needs.
      The same reasoning as the `features` key in the crew plan, and still to be re-checked here.
- [ ] Old builds never read `config`, so they keep the colours they shipped with. A palette change only reaches
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

## Open questions

- **Signed out.** `GET /me` needs an account. If the start screen can ever be shown before sign-in, the first launch
  uses the built-in colours until sign-in. Check whether that screen is reachable without an account.
- **Try before everyone.** Per-account preview is possible by reusing the feature flags (a `palette-preview` feature
  serving `Palette:Preview` to flagged accounts). Not in the first pass; Staging config covers testing for now.
- **Staleness.** A palette is applied on the next launch after it is fetched, so an edit reaches a given phone one
  launch later. Fine for colours; revisit if it ever needs to be immediate.
- **Dark mode.** The playground has one palette. If dark mode needs its own, the object becomes `light` and `dark`
  sets under a bumped `version`.
- **Where the values live long term.** Config needs a restart. If that gets tedious, the same object could be edited
  from the admin Users page and stored in the database, with no change to the response shape or the apps.

## Branch / PR naming

Per the project convention: `remote-palette` for this plan; server PR "Palette: serve from config (server)", then
`remote-palette-android` and `remote-palette-ios` for the clients.
