# Crew feature flag — Plan

*DotWatcher — plan, October 2026*

Goal: let the owner switch Crew on for chosen accounts only, then turn it on for everyone later without shipping new
app builds. Related: [`crew-management.md`](crew-management.md).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally.

---

## Decisions taken

- **Per-account, server-side.** The flag belongs to the user account, not the device or app build. Staging and
  Production share one database, so a grant applies to both.
- **The server is the gate.** Hiding UI is cosmetic. Every `/me/crew*` endpoint refuses callers without the flag.
- **Generic, not crew-only.** A `features` list per user (`"crew"` today) so the next feature reuses the mechanism.
- **Clients follow the server.** Login and `/me` return the user's features; clients show Crew UI only when `crew` is
  present. Old app builds (which don't know about features) keep working: they never show Crew anyway, or they show it
  and get a clean refusal (see Open questions).
- **Global switch for rollout.** A config value turns a feature on for every account, so going general is a config
  change, not a data migration.

## Server

Written on the current branch, not yet committed. Nothing built or run locally (see top).

### S1. Storage
- [x] `features TEXT NOT NULL DEFAULT ''` on `users`, added with the existing idempotent pattern in `SessionStore.cs`
      (`ALTER TABLE users ADD COLUMN IF NOT EXISTS …`, after the crew block). Additive with a default, so builds that
      predate it never see it (Staging and Production share the database).
- [x] Comma-separated list; `GetUserFeatures(userId)` and `SetUserFeatures(userId, features)` on `SessionStore`.
      Names are normalised (trim, lower-case, de-duplicate, sort) by `FeatureFlags.Normalize`.

### S2. Resolving a user's effective features
- [x] `FeatureFlags` service (`server/FeatureFlags.cs`): effective features = the user's stored features ∪ config
      `Features:EnabledForAll`. Registered in `Program.cs`. Feature names live in a `Features` static class.

### S3. Enforcement
- [x] `CrewController` has a private `Gate(out user)` used by every action, including `/me/profile` (the nickname
      only exists for crew). Not signed in → `401`; signed in without the feature → `404`.
- [x] Crew isn't surfaced anywhere else in `server/` (checked: only `CrewController`, `SessionStore`, the limiter and
      models).

### S4. Exposing features to clients
- [x] `AuthenticatedUser` gains `Features` (optional, so bearer-token-derived users and existing call sites are
      unchanged; it is filled in by login, register and `GET /me`).
- [x] **Added to the first draft:** `GET /me` returns the signed-in account with its features. Without it a grant
      would only reach a client at its next sign-in, and the plan promises clients pick it up when they foreground.
- [x] `docs/api-reference.html` (login response) and `server/README.md` (Crew section) updated. `ContractTests.cs`
      needed no change.

### S5. Granting (admin/ops)
- [x] `PUT /admin/users/{userId}/features`, body `{ "features": ["crew"] }`, admin bearer. Replaces the list; `[]`
      revokes; unknown user → `404`; missing `features` → `400`. `AdminUserSummary` (and so `GET /admin/users`)
      includes each account's `Features`.
- [x] A "Crew" checkbox per account in the Users table of `client/src/AdminPanel.tsx` (branch
      `crew-feature-flag-web`). Ticking sends the account's full feature list with `crew` added or removed.

### S6. Tests (`tests/DotWatcher.Server.Tests`)
- [x] `DotWatcherApiFactory` gets `EnabledForAll` (default `["crew"]`, so the existing crew tests are untouched).
- [x] New `FeatureFlagTests.cs`: every crew endpoint → `404` without the feature; signed-out stays `401`; grant opens
      crew for that account only; revoke closes it; `EnabledForAll` opens it for all; names normalised; admin
      endpoint auth/404/400; features in login, `GET /me` and the admin user list.
- [ ] CI is the first run of any of this.

## Backwards compatibility (old app builds)

Shipped builds share the server, so the server change must not alter anything they already depend on.

- [x] The only change to an existing response shape is the extra `features` key on the login/register user object.
      iOS `AppUser` is `Codable` (unknown keys ignored); Android decodes with `ignoreUnknownKeys = true`. Web not yet
      checked (S-web, below).
- [x] Builds with crew (iOS `CrewStore.refresh`, Android `CrewModel.refresh`) treat any status other than `401`/`426`
      from `/me/crew` as "no crew on this server" and hide it quietly. That is why the gate is a `404`, not `401`/`403`.
      Checked by reading both.
- [x] Builds without crew never call a crew endpoint. (`CrewStore.swift` was first committed 2026-10-23.)
- [x] Web client: `isAuthSession` (`client/src/authStorage.ts`) checks only the fields it needs, so an extra
      `features` key is accepted. (The "S-web" mention above refers to this.)
- [ ] **Manual check after the server PR deploys to Staging:** run an older TestFlight/Android build against Staging
      and confirm sign-in, the main screen and joining a session are unaffected, and that a build with crew shows no
      Crew row for an ungranted account and shows it once granted. No automated test covers an old build.

## Clients (separate PRs, each "Crew: …")

- [x] **iOS** — needs no `features` plumbing: `CrewStore.refresh()` already probes `/me/crew`, hides the row,
      nickname section and everything else on a 404 (`isAvailable`), and re-probes on sign-in and whenever the app
      comes to the front, so a grant appears without a re-login or `GET /me`. The one gap was a crew link: a lookup
      for an ungranted account 404s like a dead code and read "already been used or has expired". `beginAdd` now
      waits for the first probe if it hasn't answered, and shows "Crew isn't available on your account yet." when
      the account doesn't have crew. (Branch `crew-feature-flag-ios`.)
- [x] **Android** — same as iOS: `CrewModel.load()` already probes `/me/crew` and hides crew on a 404, and
      `AppViewModel` refreshes on resume. `beginAdd` now runs the first probe if it hasn't answered and shows
      "Crew isn't available on your account yet." for an account without crew. (Branch
      `crew-feature-flag-android`.)
- [x] **Web** — no change needed. `CrewLinkPage.tsx` never talks to the server or signs anyone in: it only shows the
      code and tells the person to add it in the app, which is where the "not available yet" message now lives.
      `LandingPage.tsx` has no crew entry points (its only "crew" is the word in an FAQ answer).
- [x] Refresh on sign-in and foreground: both apps already do this through their existing crew probe, so a grant takes
      effect without re-login. `GET /me` stays available on the server for any later client that wants the list.

## Rollout

1. Merge server PR (S1–S6). Nobody has the flag, so Crew disappears from the API for everyone, including existing
   test accounts: grant yourself and testers first.
2. Grant `crew` to chosen accounts via the admin endpoint.
3. Ship client gating PRs. Until a client ships gating, Crew UI there is simply server-refused for unflagged users.
4. General release: set `Features:EnabledForAll` to `["crew"]` (config only). Later, remove the check or keep it for
   the next feature.

## Open questions

- **Both sides need the flag?** A flagged user shares a code with an unflagged friend. The friend's redeem call gets
  `404` and sees "not available yet". Acceptable for a tester phase; revisit if testers complain.
- **Revoking.** Revoking `crew` hides the UI and API but leaves existing crew rows in the database. Intended: the
  data stays and returns if re-granted.
- **Existing-test caveat.** Branch `crew-ios-app` / `crew-assetlinks` are already merged, so any current testers lose
  access at step 1 until granted. Grant before or immediately after deploy.

## Branch / PR naming

Per the project convention: `crew-feature-flag` for the server work, PR title "Crew: feature flag (server)"; client
PRs `crew-feature-flag-ios`, `crew-feature-flag-android`, `crew-feature-flag-web`.
