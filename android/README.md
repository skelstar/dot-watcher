# Android

Native Kotlin/Jetpack Compose client for Dot Watcher, targeting the same server API as
`ios/` and `client/`. See `.ai/plans/android-app.md` for the full milestone plan this
app is being built against.

**Status:** Milestone 2 (background tracking) — sign in/register, create or join a session,
consent to share for a bounded duration (2h/4h/8h/24h, matching iOS), and keep sending
position updates via a foreground service even with the screen locked or the app
backgrounded. No offline queue/session management UI yet (Milestone 3).

**Not yet tested on a real device.** Milestone 2's exit criteria (`.ai/plans/android-app.md`)
is a real device, screen off, in a pocket, posting for the length of a real run — this still
needs that pass, plus a look at OEM-specific "let this app run in background" settings
(Samsung/Xiaomi/etc.), which the in-app battery-optimization exemption prompt can't reach.

## Requirements

- Android Studio (Ladybug or newer) or a JDK 17 + Android SDK toolchain
- Android SDK Platform 35, Build-Tools matching AGP 8.7.2 (see `gradle/libs.versions.toml`)
- A LINZ developer API key for the map basemap (see below)

## Running locally

1. Start the server (see repo root `README.md` / `docker-compose.yml` for the local
   Postgres + `dotnet run` setup — it listens on `:8080` per `server/Properties/launchSettings.json`).
2. Copy `local.properties.example` to `local.properties` (gitignored) and fill in
   `LINZ_API_KEY` with a real LINZ developer key — the same key `client/.env.example`'s
   `VITE_LINZ_API_KEY` documents for the web client. Android Studio fills in `sdk.dir`
   automatically; only `LINZ_API_KEY` needs to be added by hand.
3. Open `android/` in Android Studio, or from the command line:

   ```bash
   cd android
   ./gradlew assembleDebug
   ```

4. Run on an emulator. The `debug` build type points at `http://10.0.2.2:8080` —
   the emulator's alias for the host machine's `localhost` — matching iOS's Debug
   configuration pointing at the simulator's localhost (see repo root `README.md`,
   "ios" section, and `app/build.gradle.kts`).

## Running on a real device

A real phone can't reach `10.0.2.2` (that only means something to the emulator) or a `.local`
mDNS hostname the way iOS's Device configuration can, so there's a third build type,
`device`, for this case — matching iOS's Debug/Device/Release split.

1. Set `DEVICE_API_BASE_URL` in `local.properties` to your dev machine's LAN IP (see
   `local.properties.example`) — e.g. `http://192.168.1.23:8080`, found via
   `ipconfig getifaddr en0` on macOS.
2. Make sure your phone and dev machine are on the same Wi-Fi network, and the server is
   listening on that interface, not just `localhost` (`dotnet run --urls http://0.0.0.0:8080`,
   or adjust `launchSettings.json`).
3. Enable Developer Options and USB debugging on the phone (Settings > About phone > tap
   Build number 7 times, then Settings > System > Developer options > USB debugging).
4. Connect via USB, select the phone as the target device in Android Studio, and choose the
   `device` build variant (Build > Select Build Variant, or the variant dropdown) before
   hitting Run.

No Play Store account or upload needed for this — it's a debug-signed build installed
directly over USB, the fastest loop for iterating on device-only behavior like Milestone 2's
background tracking. Play Console internal testing (Milestone 4) is for distributing signed
release builds to testers without a cable, not required to just see the app running locally.

## Structure

```
android/
  app/                    Single application module
    .../data/             Token storage + repository wrapping the API
    .../location/         Foreground GPS + compass heading capture
    .../network/          Retrofit API surface and wire models
    .../ui/               Auth, session create/join, and live map screens
  gradle/libs.versions.toml   Version catalog for dependencies
  local.properties.example   Template for the gitignored local.properties (LINZ key, SDK path)
```

Kept as a single `:app` module for now; will split into modules (e.g. `:core-network`)
only if/when the codebase size justifies it.

## Conventions carried over from iOS/web

- Every request sends `X-Api-Version: 1`, matching the server's current
  `MinimumApiVersion` floor — see repo root `README.md`, "Client compatibility".
- Heading comes from the device's compass/rotation sensor, never derived from
  consecutive GPS fixes — see repo root `README.md`, "Position payload".
- Recording cadence clock-aligns to wall-clock boundaries (`location/ClockAlignment.kt`),
  so Android's dots line up in time with iOS's and the web client's — see repo root
  `README.md`, "Position synchronisation".
- A share session is explicitly started and always auto-stops at a chosen duration cap
  (2h/4h/8h/24h) — mirrors iOS's App Store 5.1.2(i) constraint that automatic location
  posting must be bounded, not indefinite (`ShareLocationConsentView.swift`), applied here
  as a good practice rather than a Play Store requirement.
- Map: MapLibre Android + LINZ topo vector tiles, matching `client/`'s basemap
  (`client/src/map/mapStyle.ts`) rather than Google Maps — see `.ai/plans/android-app.md`'s
  "Open decisions".
