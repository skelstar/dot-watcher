---
name: build-ios-simulator
description: Build the DotWatcher iOS app and install/relaunch it on the booted Simulator(s) so code changes can be seen and verified live. Use whenever iOS source changes need to be checked visually, after editing anything under ios/DotWatcher, or when asked to "build and deploy/restart the simulator(s)".
---

# Build and restart DotWatcher on the iOS Simulator

Rebuilds the app from source and reinstalls it on every currently booted
Simulator, so a stale build (old UI, missing fix) is never mistaken for a bug.
Always rebuild before screenshotting/inspecting the app after a source change —
`xcrun simctl launch` on an old install silently shows old behavior.

## 1. Find booted simulators

```bash
xcrun simctl list devices | grep -i booted
```

Note each UDID (e.g. `A62EDA8F-28C6-45C7-97C3-ABEA51F42DFE`). If none are
booted, ask the user which simulator to boot, or boot one:
`xcrun simctl boot <UDID>`.

## 2. Build

```bash
cd /Users/skelstar/Documents/GitHub/dot-watcher/ios/DotWatcher
xcodebuild -project DotWatcher.xcodeproj -scheme DotWatcher \
  -destination 'generic/platform=iOS Simulator' -configuration Debug build
```

Confirm the tail of the output ends with `** BUILD SUCCEEDED **`. If it fails,
fix the compile error before continuing — do not install a stale/previous
build over a broken one.

The built `.app` always lands at the same DerivedData path regardless of which
simulator it's later installed on:

```
/Users/skelstar/Library/Developer/Xcode/DerivedData/DotWatcher-dkggwqncoxmmhzbgykqkydgjvdpk/Build/Products/Debug-iphonesimulator/DotWatcher.app
```

(If that DerivedData hash ever changes, e.g. after a `derived data` clean, get
the current path from the `CodeSign .../DotWatcher.app` line in the build
output.)

## 3. Install and launch on every booted simulator

Bundle ID is `io.skelstar.DotWatcher`. Repeat for each booted UDID from step 1:

```bash
APP_PATH="/Users/skelstar/Library/Developer/Xcode/DerivedData/DotWatcher-dkggwqncoxmmhzbgykqkydgjvdpk/Build/Products/Debug-iphonesimulator/DotWatcher.app"
for DEV in A62EDA8F-28C6-45C7-97C3-ABEA51F42DFE F3A77401-6AA5-44CA-A385-FD50D7B8050A; do
  xcrun simctl install "$DEV" "$APP_PATH"
  xcrun simctl launch "$DEV" io.skelstar.DotWatcher
done
```

`simctl install` overwrites the existing install in place — no need to
uninstall first. `simctl launch` restarts the app if it's already running.

## 4. Verify visually

```bash
xcrun simctl io <UDID> screenshot /path/to/scratchpad/name.png
```

Then read the PNG with the Read tool and actually look at it before claiming
the change works — a screenshot you don't inspect proves nothing.

## Local dev server

The app talks to `http://localhost:8080` in dev builds. If testing a feature
that needs the server, make sure it's running first:

```bash
lsof -ti:8080 || (cd /Users/skelstar/Documents/GitHub/dot-watcher/server && \
  (dotnet run --urls "http://localhost:8080" > /tmp/dotwatcher-server.log 2>&1 &))
```

If a server changed (`server/` diff), kill the stale process
(`kill $(lsof -ti:8080)`) and restart it the same way — otherwise the app hits
old server behavior even though the client rebuilt fine.

## Driving taps (optional)

`simctl` alone can't send taps. If a scenario needs actual UI interaction
(not just visual inspection after manually navigating), `idb` is installed —
see project memory / prior session notes for setup
(`idb_companion --udid <UDID> &`, then `idb connect localhost <port from
companion log>`, then `idb ui tap <x> <y>` / `idb ui describe-all`).
