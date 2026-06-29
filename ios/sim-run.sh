#!/bin/bash
set -e

PROJECT="$(dirname "$0")/DotWatcher/DotWatcher.xcodeproj"
SCHEME="DotWatcher"
BUNDLE_ID="io.skelstar.DotWatcher"
BUILD_DIR="/tmp/DotWatcher-sim-build"
APP_PATH="$BUILD_DIR/Build/Products/Debug-iphonesimulator/DotWatcher.app"

echo "==> Building..."
xcodebuild \
  -project "$PROJECT" \
  -scheme "$SCHEME" \
  -sdk iphonesimulator \
  -configuration Debug \
  -derivedDataPath "$BUILD_DIR" \
  build 2>&1 | xcpretty 2>/dev/null || cat

BOOTED=$(xcrun simctl list devices | grep Booted | grep -oE '[A-F0-9-]{36}')

if [ -z "$BOOTED" ]; then
  echo "No booted simulators found. Boot at least one in Xcode first."
  exit 1
fi

echo "==> Installing and launching on booted simulators..."
for UDID in $BOOTED; do
  (
    NAME=$(xcrun simctl list devices | grep "$UDID" | sed 's/ (.*//')
    xcrun simctl install "$UDID" "$APP_PATH"
    xcrun simctl launch --terminate-running-process "$UDID" "$BUNDLE_ID" > /dev/null
    echo "    ✓ $(echo $NAME | xargs)"
  ) &
done
wait

echo "==> Done."
