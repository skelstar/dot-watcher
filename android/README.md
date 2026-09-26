# Android

Native Kotlin/Jetpack Compose client for Dot Watcher, targeting the same server API as
`ios/` and `client/`. See `.ai/plans/android-app.md` for the full milestone plan this
app is being built against.

**Status:** Milestone 0 (project scaffold) — builds and runs a placeholder screen that
confirms connectivity to the server. No sign-in, session, or tracking features yet.

## Requirements

- Android Studio (Ladybug or newer) or a JDK 17 + Android SDK toolchain
- Android SDK Platform 35, Build-Tools matching AGP 8.7.2 (see `gradle/libs.versions.toml`)

## Running locally

1. Start the server (see repo root `README.md` / `docker-compose.yml` for the local
   Postgres + `dotnet run` setup).
2. Open `android/` in Android Studio, or from the command line:

   ```bash
   cd android
   ./gradlew assembleDebug
   ```

3. Run on an emulator. The `debug` build type points at `http://10.0.2.2:5000` —
   the emulator's alias for the host machine's `localhost` — matching iOS's Debug
   configuration pointing at the simulator's localhost (see repo root `README.md`,
   "ios" section, and `app/build.gradle.kts`).

## Structure

```
android/
  app/                    Single application module (Milestone 0)
  gradle/libs.versions.toml   Version catalog for dependencies
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
