#!/bin/bash
# Imports a bare NDJSON recording into a server as a brand-new session, so it's replayable
# through the normal app UI at the invite code this prints out.
#
# Unlike import-session.sh, this needs nothing but the .ndjson file itself — no info.json, no
# download-session.sh directory layout. Use it for a recording obtained any other way: the admin
# panel's "Export records" button, a colleague's file, a hand-edited track.
#
# The NDJSON carries only location rows (runner name, coordinates, timestamps) — no session name
# and no owner — so the session name is passed in, and the session gets a throwaway owner account
# (the same approach import-session.sh uses; app_sessions.owner_user_id is NOT NULL and
# references users(id), so every session needs a real owner row).
#
# The invite code is generated server-side at creation, and printed at the end.
#
# Usage:
#   scripts/import-ndjson.sh -n SESSION_NAME
#   scripts/import-ndjson.sh -n SESSION_NAME -f path/to/file.ndjson
#   scripts/import-ndjson.sh -n SESSION_NAME --owner "Sean"
#   SERVER_URL=http://localhost:8096 ADMIN_TOKEN=dev-token scripts/import-ndjson.sh -n SESSION_NAME
#
# Caveat: the recording upload endpoint doesn't associate rows to a user, so the admin panel's
# per-member position counts will read as zero for the imported data even though the session
# replays correctly on the map.

set -euo pipefail

SERVER_URL="${SERVER_URL:-http://localhost:8080}"
ADMIN_TOKEN="${ADMIN_TOKEN:-dev-token}"
FILE="imported_route.ndjson"
SESSION_NAME=""
OWNER_NAME="Imported"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -n|--name) SESSION_NAME="$2"; shift 2 ;;
    -f|--file) FILE="$2"; shift 2 ;;
    --owner) OWNER_NAME="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [[ -z "$SESSION_NAME" ]]; then
  echo "Usage: scripts/import-ndjson.sh -n SESSION_NAME [-f FILE] [--owner NAME]" >&2
  exit 1
fi

# Mirrors the server's own NormalizeSessionName so a bad name fails here, with the rule spelled
# out, rather than as a bare 400 after the throwaway account has already been registered.
if [[ ! "$SESSION_NAME" =~ ^[A-Za-z0-9_-]{4,8}$ ]]; then
  echo "Session name must be 4-8 letters, numbers, dashes, or underscores (got '$SESSION_NAME')." >&2
  exit 1
fi
SESSION_NAME=$(echo "$SESSION_NAME" | tr '[:lower:]' '[:upper:]')

if [[ ! -f "$FILE" ]]; then
  echo "No such file: $FILE (pass one with -f, or drop it in as imported_route.ndjson)." >&2
  exit 1
fi

if [[ ! -s "$FILE" ]]; then
  echo "$FILE is empty — the server rejects a recording with no location updates." >&2
  exit 1
fi

# Probed by actually running it, not just `command -v`: on Windows, `python3` resolves to an App
# Execution Alias stub that exists on PATH but errors out the moment it's invoked.
PY=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c "" >/dev/null 2>&1; then
    PY="$candidate"
    break
  fi
done
[[ -n "$PY" ]] || { echo "python3 (or python) is required (used for JSON parsing)." >&2; exit 1; }

LINES=$(grep -c '[^[:space:]]' "$FILE" || true)
echo "==> Importing $FILE ($LINES lines) as '$SESSION_NAME' into $SERVER_URL"

USERNAME="import_$(echo "$SESSION_NAME" | tr '[:upper:]' '[:lower:]')_$(date +%s)"
PASSWORD="Passw0rd!$(date +%s)"

echo "==> Registering throwaway owner account ($USERNAME) ..."
# Plain -s (not -f): a transport failure (can't connect at all) must be told apart from an HTTP
# error response (e.g. 409 for a duplicate username) — curl only exits non-zero for the former,
# and -f would also swallow the latter's body, hiding the actual reason from the message below.
if ! REGISTER_RESPONSE=$(curl -s -X POST "$SERVER_URL/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\",\"displayName\":\"$OWNER_NAME\"}"); then
  echo "Could not reach $SERVER_URL — is the server running there? (pass SERVER_URL=... if it's on a different port)" >&2
  exit 1
fi
TOKEN=$(echo "$REGISTER_RESPONSE" | "$PY" -c "import sys,json; print(json.load(sys.stdin).get('accessToken',''))")

if [[ -z "$TOKEN" ]]; then
  echo "Failed to register owner account: $REGISTER_RESPONSE" >&2
  exit 1
fi

echo "==> Creating session $SESSION_NAME ..."
if ! SESSION_RESPONSE=$(curl -s -X POST "$SERVER_URL/sessions" \
  -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  -d "{\"sessionName\":\"$SESSION_NAME\",\"displayName\":\"$OWNER_NAME\"}"); then
  echo "Could not reach $SERVER_URL — is the server running there?" >&2
  exit 1
fi
SESSION_ID=$(echo "$SESSION_RESPONSE" | "$PY" -c "import sys,json; print(json.load(sys.stdin).get('sessionId',''))")
INVITE_CODE=$(echo "$SESSION_RESPONSE" | "$PY" -c "import sys,json; print(json.load(sys.stdin).get('inviteCode',''))")

if [[ -z "$SESSION_ID" ]]; then
  echo "Failed to create session: $SESSION_RESPONSE" >&2
  exit 1
fi

# An explicit non-form Content-Type is required — a plain curl --data-binary defaults to
# application/x-www-form-urlencoded, which ASP.NET then tries to parse as form data and rejects.
echo "==> Uploading recording ..."
UPLOAD_BODY=$(mktemp)
if ! UPLOAD_STATUS=$(curl -s -o "$UPLOAD_BODY" -w '%{http_code}' \
  -X POST "$SERVER_URL/sessions/$SESSION_ID/recording" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/x-ndjson" \
  --data-binary "@$FILE"); then
  echo "Could not reach $SERVER_URL — is the server running there?" >&2
  rm -f "$UPLOAD_BODY"
  exit 1
fi

if [[ "$UPLOAD_STATUS" != "200" ]]; then
  echo "  Recording upload failed (HTTP $UPLOAD_STATUS): $(cat "$UPLOAD_BODY")" >&2
  rm -f "$UPLOAD_BODY"
  exit 1
fi
rm -f "$UPLOAD_BODY"
echo "  recording uploaded"

echo ""
echo "==> Done."
echo "  Session:     $SESSION_NAME"
echo "  Session ID:  $SESSION_ID"
echo "  Invite code: $INVITE_CODE"
echo "  Replay URL:  http://localhost:5173/code/$INVITE_CODE"
echo ""
echo "  (Throwaway owner account '$USERNAME' left in place; delete via"
echo "   DELETE $SERVER_URL/me with that user's token if you want it gone.)"
