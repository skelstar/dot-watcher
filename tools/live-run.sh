#!/bin/bash
# Simulates a runner tracking a live session against a local Dot Watcher server, so the
# scrubber/replay UI has a real in-progress session to test against instead of only ever
# seeing finished runs.
#
# Usage:
#   tools/live-run.sh
#   tools/live-run.sh --interval 5 --duration 300
#   SERVER_URL=http://localhost:8080 tools/live-run.sh
#
# A session stays "live" as long as its most recent location update is less than 5 minutes
# old (LIVE_STALE_MS in client/src/sessionLiveness.ts), so it naturally goes "finished" a
# few minutes after this script exits or is interrupted — no separate cleanup step needed
# to end the run. Ctrl-C stops it early.

set -euo pipefail

SERVER_URL="${SERVER_URL:-http://localhost:8080}"
INTERVAL_SECONDS=8
DURATION_SECONDS=1800  # 30 minutes of simulated running by default
SESSION_NAME=""
RUNNER_NAME="LR"

# Wellington waterfront, heading roughly northeast at an easy jogging pace.
START_LAT=-41.2924
START_LON=174.7787
LAT_STEP=0.0003
LON_STEP=0.0002
HEADING=45

while [[ $# -gt 0 ]]; do
  case "$1" in
    --interval) INTERVAL_SECONDS="$2"; shift 2 ;;
    --duration) DURATION_SECONDS="$2"; shift 2 ;;
    --session-name) SESSION_NAME="$2"; shift 2 ;;
    --runner-name) RUNNER_NAME="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$SESSION_NAME" ]]; then
  SESSION_NAME="LIVE$(date +%H%M)"
fi

command -v python3 >/dev/null || { echo "python3 is required (used for JSON parsing and float math)."; exit 1; }

echo "==> Checking server at $SERVER_URL ..."
if ! curl -s -o /dev/null -w '' --fail "$SERVER_URL/" 2>/dev/null; then
  echo "Could not reach $SERVER_URL. Is the server running? (cd server && dotnet run --urls \"$SERVER_URL\")" >&2
  exit 1
fi

USERNAME="liverun_$(date +%s)"
PASSWORD="Passw0rd!$(date +%s)"

echo "==> Registering throwaway user ($USERNAME) ..."
REGISTER_RESPONSE=$(curl -s -X POST "$SERVER_URL/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\",\"displayName\":\"$RUNNER_NAME\"}")
TOKEN=$(echo "$REGISTER_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

echo "==> Creating session $SESSION_NAME ..."
SESSION_RESPONSE=$(curl -s -X POST "$SERVER_URL/sessions" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d "{\"sessionName\":\"$SESSION_NAME\",\"displayName\":\"$RUNNER_NAME\"}")
SESSION_ID=$(echo "$SESSION_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin)['sessionId'])")
INVITE_CODE=$(echo "$SESSION_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin)['inviteCode'])")

echo ""
echo "  Session:     $SESSION_NAME"
echo "  Invite code: $INVITE_CODE"
echo "  Debug UI:    http://localhost:5173 (load this invite code)"
echo ""
echo "==> Posting a location update every ${INTERVAL_SECONDS}s for up to ${DURATION_SECONDS}s. Ctrl-C to stop early."
echo ""

cleanup() {
  echo ""
  echo "==> Stopped. The session will read as finished ~5 minutes after the last update."
  echo "    (Throwaway user '$USERNAME' and its session are left in place for inspection;"
  echo "     delete via DELETE $SERVER_URL/me with that user's token if you want it gone.)"
}
trap cleanup EXIT

lat="$START_LAT"
lon="$START_LON"
elapsed=0
while [[ "$elapsed" -lt "$DURATION_SECONDS" ]]; do
  lat=$(python3 -c "print($lat + $LAT_STEP)")
  lon=$(python3 -c "print($lon + $LON_STEP)")
  timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)

  curl -s -o /dev/null -X POST "$SERVER_URL/location" \
    -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
    -d "{\"runnerName\":\"$RUNNER_NAME\",\"sessionId\":\"$SESSION_ID\",\"latitude\":$lat,\"longitude\":$lon,\"heading\":$HEADING,\"timestamp\":\"$timestamp\"}"

  echo "  [$timestamp] posted ($lat, $lon)"
  sleep "$INTERVAL_SECONDS"
  elapsed=$((elapsed + INTERVAL_SECONDS))
done
