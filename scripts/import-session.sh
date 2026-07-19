#!/bin/bash
# Re-imports a session previously downloaded by download-session.sh into a local server, so it
# shows up in that server's own admin panel and is replayable through the normal app UI.
#
# Downloading a session (scripts/download-session.sh) only saves files locally — it never
# touches any server's database. The admin panel only lists sessions that exist as rows in the
# server it's talking to, so a session downloaded from staging needs to be recreated locally
# before it'll appear at http://localhost:5173/admin (or wherever your local admin UI points).
#
# This creates a brand-new local session (new sessionId/inviteCode — the original staging IDs
# aren't reused) using the downloaded session's name and owner display name, then uploads the
# downloaded recording and route into it via the existing admin-token-protected bulk endpoints.
#
# Usage:
#   scripts/import-session.sh CODE
#   scripts/import-session.sh CODE --dir data/sessions
#   SERVER_URL=http://localhost:8080 ADMIN_TOKEN=dev-token scripts/import-session.sh CODE
#
# Caveat: the recording upload endpoint doesn't associate rows to a user, so the admin panel's
# per-member position counts will read as zero for the imported data even though the session,
# its recording, and its route all replay correctly on the map.

set -euo pipefail

SERVER_URL="${SERVER_URL:-http://localhost:8080}"
ADMIN_TOKEN="${ADMIN_TOKEN:-dev-token}"
DATA_DIR="data/sessions"
CODE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dir) DATA_DIR="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      if [[ -z "$CODE" ]]; then CODE="$1"; shift
      else echo "Unknown argument: $1" >&2; exit 1
      fi
      ;;
  esac
done

if [[ -z "$CODE" ]]; then
  echo "Usage: scripts/import-session.sh CODE [--dir DIR]" >&2
  exit 1
fi

SRC="$DATA_DIR/$CODE"
if [[ ! -d "$SRC" ]]; then
  echo "No downloaded data at $SRC (run scripts/download-session.sh $CODE first)." >&2
  exit 1
fi

command -v python3 >/dev/null || { echo "python3 is required (used for JSON parsing)."; exit 1; }

SESSION_NAME=$(python3 -c "import json; print(json.load(open('$SRC/info.json'))['sessionName'])")
OWNER_NAME=$(python3 -c "import json; print(json.load(open('$SRC/info.json'))['ownerDisplayName'])")

echo "==> Importing $CODE ('$SESSION_NAME', owner '$OWNER_NAME') into $SERVER_URL"

USERNAME="import_$(echo "$CODE" | tr '[:upper:]' '[:lower:]')_$(date +%s)"
PASSWORD="Passw0rd!$(date +%s)"

echo "==> Registering throwaway owner account ($USERNAME) ..."
REGISTER_RESPONSE=$(curl -s -X POST "$SERVER_URL/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\",\"displayName\":\"$OWNER_NAME\"}")
TOKEN=$(echo "$REGISTER_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

echo "==> Creating local session $SESSION_NAME ..."
SESSION_RESPONSE=$(curl -s -X POST "$SERVER_URL/sessions" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d "{\"sessionName\":\"$SESSION_NAME\",\"displayName\":\"$OWNER_NAME\"}")
SESSION_ID=$(echo "$SESSION_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('sessionId',''))")
INVITE_CODE=$(echo "$SESSION_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('inviteCode',''))")

if [[ -z "$SESSION_ID" ]]; then
  echo "Failed to create session: $SESSION_RESPONSE" >&2
  exit 1
fi

if [[ -f "$SRC/recording.ndjson" ]]; then
  echo "==> Uploading recording ($(wc -l < "$SRC/recording.ndjson" | tr -d ' ') lines) ..."
  UPLOAD_STATUS=$(curl -s -o /tmp/import-session-recording-response.json -w '%{http_code}' \
    -X POST "$SERVER_URL/sessions/$SESSION_ID/recording" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/x-ndjson" \
    --data-binary "@$SRC/recording.ndjson")
  if [[ "$UPLOAD_STATUS" != "200" ]]; then
    echo "  Recording upload failed (HTTP $UPLOAD_STATUS): $(cat /tmp/import-session-recording-response.json)" >&2
  else
    echo "  recording uploaded"
  fi
  rm -f /tmp/import-session-recording-response.json
else
  echo "==> No recording.ndjson found, skipping."
fi

if [[ -f "$SRC/route.gpx" ]]; then
  echo "==> Uploading route ..."
  ROUTE_STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$SERVER_URL/sessions/$SESSION_ID/route" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/gpx+xml" \
    --data-binary "@$SRC/route.gpx")
  if [[ "$ROUTE_STATUS" != "200" ]]; then
    echo "  Route upload failed (HTTP $ROUTE_STATUS)" >&2
  else
    echo "  route uploaded"
  fi
else
  echo "==> No route.gpx found, skipping."
fi

echo ""
echo "==> Done."
echo "  Session:     $SESSION_NAME"
echo "  Session ID:  $SESSION_ID"
echo "  Invite code: $INVITE_CODE"
echo "  Admin panel: (wherever your local client's /admin route points)"
echo "  Replay URL:  http://localhost:5173/$SESSION_NAME"
echo ""
echo "  (Throwaway owner account '$USERNAME' left in place; delete via"
echo "   DELETE $SERVER_URL/me with that user's token if you want it gone.)"
