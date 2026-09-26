# Android app — build plan

## Background

`android/` currently contains only a placeholder README ("not started"). The
server already speaks a stable, versioned JSON API consumed by both the iOS
app and the web client, so the Android app is a new client against an
existing contract — no server redesign needed, only additive changes where
Android needs something the other clients don't already expose.

Reference points already in the repo:

- Server API: `server/Controllers/{AuthController,SessionsController,LocationsController,LogsController,AdminController}.cs`
- Auth/session model and sequence diagram: `README.md` ("Auth model" section)
- Client compatibility gate (`X-Api-Version`): `README.md` ("Client compatibility"), `server/README.md#client-compatibility-x-api-version`
- Position payload contract: `README.md` ("Position payload"), `tests/DotWatcher.Server.Tests/ContractTests.cs`
- Feature reference implementation: `ios/DotWatcher/DotWatcher/*.swift` (especially `LocationManager.swift` for background tracking/posting cadence, `ContentView.swift` for session flow, `NativeMapView.swift` for map rendering)
- Known gap being scoped for iOS but equally relevant to Android: `.ai/plans/offline-location-queue.md`

## Guiding approach

Ship in increments, each one a usable app, rather than one big-bang release:

1. **Milestone 0 — project scaffold.** Nothing user-facing; just gets a
   buildable Kotlin app into CI.
2. **Milestone 1 — essential runner flow.** Sign in, create/join a session,
   send position in the foreground, view a live map. This alone makes the
   app usable for a real run if the phone stays awake and in-app.
3. **Milestone 2 — essential background tracking.** Reliable background
   location updates with the screen locked — the thing that makes it a
   replacement for the iOS app rather than a demo.
4. **Milestone 3 — parity features.** Session management (leave/share/invite
   codes), interval configuration, offline queueing, update-required
   handling — the "non-essential but expected" layer.
5. **Milestone 4 — polish.** Notifications, blocked users, GPX routes, help
   content, distribution via Play internal testing track.

Each milestone ends in something installable and demoable, and each is a
natural PR boundary.

---

## Milestone 0 — Project scaffold

**Goal:** empty Android app builds, runs on an emulator, and is wired into CI.

- New Kotlin/Gradle project under `android/`, matching the existing
  `ios/`/`client/` sibling-directory convention.
- Suggested stack (Google's current recommended path, not per user
  preference on this codebase — flag for confirmation): Kotlin, Jetpack
  Compose for UI, single-Activity architecture, `minSdk` chosen to cover
  realistic runner phones (e.g. Android 8/API 26+), Gradle version catalogs
  for dependency management.
- HTTP client: Retrofit + OkHttp (mirrors the structured, typed-request
  style already used server-side); JSON via kotlinx.serialization or Moshi.
- Add an `android` job to `.github/workflows` alongside the existing
  server/client CI jobs — `./gradlew build` (and later `test`) on PRs
  touching `android/`, matching this repo's CI-owns-verification convention
  (see `AGENTS.md`).
- Basic app shell: single screen, hardcoded "hello" call to
  `GET /sessions` (or equivalent) against the local dev server, proving the
  network path and JSON decoding work end-to-end.

**Exit criteria:** PR merges, CI green, app installs on an emulator and
successfully calls the local server.

---

## Milestone 1 — Essential runner flow (foreground only)

**Goal:** a runner can sign in, start a session, and see live positions,
while the app is open and the screen is on. No background tracking yet.

Maps to server endpoints already documented in `README.md`:

- **Auth:** `POST /auth/register`, `POST /auth/login`, `POST /auth/logout`.
  Store the returned access token (Android Keystore-backed
  `EncryptedSharedPreferences`, the rough equivalent of iOS Keychain usage).
- **Session creation/join:** `POST /sessions`,
  `POST /session-invites/{inviteCode}/join` with `role: runner`.
- **Send position (foreground):** `POST /location` on a simple
  timer while the app is in the foreground — coordinates via
  `FusedLocationProviderClient` (Google Play services location API, the
  Android analogue of `CLLocationManager`), heading via
  `SensorManager`'s rotation-vector sensor, matching the
  "heading from device compass, not derived from consecutive fixes" rule
  from `README.md`.
- **View live positions:** `GET /locations/{sessionCode}` polled every
  10–15s (matching the web client's cadence), rendered on a map.
- **Map rendering:** decide the map SDK now since it shapes a lot of later
  UI work — see "Open decisions" below. Render runners as directional
  markers using `heading`, falling back to a plain dot when heading is
  absent, mirroring `client/`'s and iOS's behavior.
- **Required headers:** send `X-Api-Version` (start at whatever integer the
  Android client's initial payload shape matches — likely `1`, since it's
  adopting the current contract) and `Authorization: Bearer <token>` on
  protected calls, per the "Client compatibility" and "Auth model" sections
  of `README.md`.

**Explicitly out of scope for this milestone:** background updates, offline
queueing, interval configuration UI, session list management beyond "the
one session I just joined."

**Exit criteria:** two devices (or one device + web client) in the same
session see each other's live positions update on a map while both apps are
open in the foreground.

---

## Milestone 2 — Essential background tracking

**Goal:** tracking survives the screen being locked and the app being
backgrounded — this is the feature that makes the app actually useful for a
run, and the part of iOS parity most likely to need real Android-specific
design (Android's background execution limits differ substantially from
iOS's).

- Implement a **foreground service** (`android.app.Service` with
  `startForeground()` + a persistent notification, required by Android for
  any long-running background location work since Android 8+, and further
  restricted by the Android 14 foreground-service-type declarations for
  location).
- Request `ACCESS_BACKGROUND_LOCATION` (Android 10+) in addition to
  fine/coarse location, with the two-step permission flow Android enforces
  (foreground location granted first, background location requested
  separately, often via a settings deep-link).
- Recording cadence: same "clock-aligned recording intervals" design
  described in `README.md` ("Position synchronisation") — snap to
  wall-clock boundaries (`:00`, `:30`, etc.) rather than "every N seconds
  from launch," so Android's positions line up with iOS's and the web
  client's on the shared map.
- **Correction (checked against `ios/DotWatcher/DotWatcher/LocationManager.swift` while
  implementing):** iOS does not actually have a user-facing post-interval picker — it uses a
  fixed 15s cadence (90s when `isUltraConstrained`, e.g. satellite), which is what Android
  matches instead of building a 30s/60s/120s picker as originally guessed here. iOS *does* have
  a tracking-**duration** picker (2h/4h/8h/24h, auto-stops tracking at the cap) — that's the
  option Android ports, not a post-interval one. No satellite-tier equivalent on Android yet.
- Doze mode / App Standby / OEM battery-optimization interaction: request
  exemption from battery optimization for this app
  (`REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`) and document the various
  OEM-specific "let this app run in background" settings (Samsung, Xiaomi,
  etc.) that real users will likely need to toggle — this is the single
  biggest platform-parity risk relative to iOS and worth a dedicated test
  pass on a couple of real devices, not just an emulator.

**Exit criteria:** a real device, screen off, in a pocket, posts positions
at the configured interval for the length of a real run (test with an
actual walk/run, not just an emulator).

---

## Milestone 3 — Parity features ("non-essential" layer)

Roughly ports the remaining iOS behavior documented in `README.md` and
visible in the iOS source, once the essential path is proven:

- **Session management UI:** list of sessions the user belongs to, swipe or
  menu actions to share (Android share sheet — the equivalent of iOS's
  SMS/Email/WhatsApp/Map share row) an invite code/link, and leave a
  session.
- **Failed-send handling / offline queue:** the gap called out in
  `.ai/plans/offline-location-queue.md` is currently open for iOS too — if
  it lands there first, port the same design (persisted queue, capped size,
  flush-oldest-first) rather than designing it twice; if Android gets there
  first, the design in that doc is a reasonable starting point for both
  platforms.
- **`426 Upgrade Required` handling:** blocking "Update Required" screen
  when the server rejects the client's `X-Api-Version`, mirroring
  `UpdateRequiredView.swift`.
- **Consent / permissions UX:** an explicit "share your location" consent
  screen before first tracking start, mirroring
  `ShareLocationConsentView.swift`, plus handling permission-denied and
  "don't ask again" states gracefully (Android's permission UX differs
  enough from iOS's that this needs its own pass, not a direct port).
- **Build variants:** Android's equivalent of the three Xcode configurations
  (Debug/Device/Release) — Gradle build types/flavors pointing at
  localhost (emulator's `10.0.2.2` alias), the LAN dev server, and
  production.

**Exit criteria:** feature-for-feature parity with the iOS app's core
session/tracking/sharing loop, still on an internal test track.

---

## Milestone 4 — Polish / distribution

Lower-priority items, roughly mapped to remaining iOS files:

- Blocked users management (`BlockedUsersView.swift` equivalent).
- GPX route import/display (`GpxRouteParser.swift`, `.ai/plans/ios-route-upload.md` if that's landed by then).
- In-app help content (`HelpView.swift`/`Help.md` equivalent).
- Push notifications (if/when relevant — check whether iOS has any first;
  not obviously present in the current iOS file list).
- App icon, Play Store listing assets, privacy/terms links (the web client
  already serves `/privacy` and `/terms` — reuse those rather than
  duplicating).
- Distribution via **Play Console internal testing track** (Android's
  equivalent of TestFlight) rather than public release, matching how iOS is
  currently distributed.

---

## Open decisions to settle before/at Milestone 0–1

1. **Map SDK.** Options: Mapbox/MapLibre Android SDK (keeps visual/behavioral
   parity with `client/`'s MapLibre + LINZ topo setup, and is a similar
   architecture to iOS's tile-based rendering), or Google Maps SDK for
   Android (more "native Android," but LINZ topo tiles would need a custom
   tile overlay either way, and Google Maps bakes in a Google basemap
   underneath that would need suppressing). Recommend **MapLibre Android**
   for consistency with the web client's existing LINZ integration and to
   avoid Google Maps API key/billing setup.
2. **UI toolkit.** Recommend Jetpack Compose (current standard, less
   boilerplate) over classic XML Views, unless there's a reason to prefer
   the latter.
3. **`minSdk` target** — depends on what devices actual runners in this
   group use; default to a conservative-but-current choice (API 26+) absent
   other input.
4. **`X-Api-Version` starting value** — confirm with the server floor
   (`MinimumApiVersion`) before shipping Milestone 1; should just be able to
   start at the current value (`1`) since Android is adopting the current
   contract, not an old one.

## Non-goals

- No server-side changes are anticipated for Milestones 0–2; the API
  already supports everything needed for parity. Only Milestone 3's offline
  queue might eventually motivate a `POST /locations/batch` endpoint (see
  `.ai/plans/offline-location-queue.md`, point 3) — shared with iOS if so,
  not Android-specific.
- Not aiming for Play Store public release initially — internal testing
  track only, matching iOS's TestFlight-only distribution.
