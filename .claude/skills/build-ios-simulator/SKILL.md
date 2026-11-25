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

To run against **prod** instead of the local server, pass the server override
at launch (see "Against prod" below):

```bash
SIMCTL_CHILD_DOTWATCHER_API_BASE_URL="https://dot-watcher.skelstar.io/api" \
SIMCTL_CHILD_DOTWATCHER_WEB_BASE_URL="https://dot-watcher.skelstar.io" \
  xcrun simctl launch "$DEV" io.skelstar.DotWatcher
```

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
  (Features__EnabledForAll__0=crew Features__EnabledForAll__1=casual \
   Features__EnabledForAll__2=events Features__EnabledForAll__3=groups \
   dotnet run --urls "http://localhost:8080" > /tmp/dotwatcher-server.log 2>&1 &))
```

The `Features__EnabledForAll__*` variables turn those features on for every
local account, so new simulator accounts can use crew, Go, nearby join and
groups without granting anything. Needs Docker (Postgres) up.

If a server changed (`server/` diff), kill the stale process
(`kill $(lsof -ti:8080)`) and restart it the same way — otherwise the app hits
old server behavior even though the client rebuilt fine.

## Against prod

Debug builds read `DOTWATCHER_API_BASE_URL` / `DOTWATCHER_WEB_BASE_URL` from
the environment (`configuredBaseURL` in `LocationManager.swift`; Release builds
ignore it). Two ways to set them:

- **From the command line:** the `SIMCTL_CHILD_…` launch in step 3. The same
  build works for local and prod; only the launch differs.
- **From Xcode:** the **"DotWatcher (Simulator Prod)"** scheme (Debug, with
  both variables set). **"DotWatcher (Device Prod)"** does the same for a
  phone.

Prod is the only server and its database is real (there's no staging):

- Sign in with **test accounts**, never your own.
- Accounts on prod don't have `groups` unless granted: Admin → Users →
  **Groups** chip, per test account.
- Simulated positions are real positions to prod. Keep simulators somewhere
  nobody runs (the default Apple Park location is fine), not near home, or
  real people nearby may see the simulators' anonymous dots.

## Joining groups on the simulators

A group can be joined from a tile only by someone near its join location, so
both simulators need **the same simulated location** and an account with
`groups` (automatic on the local server above; granted on prod).

```bash
# Same spot on every booted simulator (Apple Park; any shared point works).
for DEV in A62EDA8F-28C6-45C7-97C3-ABEA51F42DFE F3A77401-6AA5-44CA-A385-FD50D7B8050A; do
  xcrun simctl location "$DEV" set 37.3349,-122.0090
done
```

Then, signed in as a different account on each simulator:

1. Simulator A: **Create** → name it → **Let people nearby join** on
   (optionally **Ask for a code to join**) → Create. The start screen shows
   **YOUR GROUP**.
2. Simulator B: pull down on the start screen to refresh; A's group appears
   as a tile. Tap it → **Join group** (enter the code if A set one).
3. To see each other by name, both press **Go**.

To join while tracking: on B press **Go**, then **‹ Home**, then tap the tile.
Keep B's location near A's join location while doing it (a moving preset
such as Features → Location → City Run would carry B away from it).

## Driving taps (optional)

`simctl` alone can't send taps. If a scenario needs actual UI interaction (not
just visual inspection after manually navigating), `idb` (fb-idb) is
pip-installed and `~/.zshrc` puts its CLI on PATH for interactive shells — but
run this skill's helper to be safe in non-interactive shells and to start/wire
up `idb_companion` for the target simulator:

```bash
/Users/skelstar/Documents/GitHub/dot-watcher/.claude/skills/build-ios-simulator/idb-connect.sh <UDID>
```

Then get exact tap coordinates from the accessibility tree instead of
eyeballing a screenshot (screenshot coordinates are in pixels; `idb ui tap`
takes points, and scaling from a screenshot is error-prone):

```bash
export PATH="/Users/skelstar/Library/Python/3.9/bin:$PATH"
idb ui describe-all --udid <UDID>   # lists every element with its point-space frame
idb ui tap --udid <UDID> <x> <y>    # tap the center of the element you want
```
