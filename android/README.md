# Android

Native Kotlin/Jetpack Compose client for Dot Watcher, targeting the same server API as
`ios/` and `client/`. See `.ai/plans/android-app.md` for the full milestone plan this
app is being built against.

**Status:** Milestone 1 (essential runner flow, foreground only) — sign in/register, create
or join a session, and see live positions on a map while the app is open. No background
tracking yet (Milestone 2) and no offline queue/session management UI (Milestone 3).

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
- Recording cadence will clock-align to wall-clock boundaries once background
  tracking lands (Milestone 2), so Android's dots line up in time with iOS's and the
  web client's — see repo root `README.md`, "Position synchronisation".
- Map: MapLibre Android + LINZ topo vector tiles, matching `client/`'s basemap
  (`client/src/map/mapStyle.ts`) rather than Google Maps — see `.ai/plans/android-app.md`'s
  "Open decisions".
