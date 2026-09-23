#!/bin/bash
# Imports a session recording into a server as a brand-new session, so it's replayable through
# the normal app UI at the invite code this prints out.
#
# Two ways to feed it data:
#   - By invite code, from a directory download-session.sh already saved (info.json,
#     recording.ndjson, route.gpx) - the session name, owner, and route come from that directory.
#   - By a bare NDJSON file, with -n/--name for the session name - for a recording obtained any
#     other way: the admin panel's "Export records" button, a colleague's file, a hand-edited
#     track. A bare file carries no session name or owner, so -n is required and --owner
#     defaults to "Imported".
#
# Every session needs a real owner row (app_sessions.owner_user_id is NOT NULL, references
# users(id)), so either mode registers a throwaway account to own the new session.
#
# Usage:
#   scripts/import-session.sh CODE [--dir data/sessions]
#   scripts/import-session.sh -n SESSION_NAME -f path/to/file.ndjson
#   scripts/import-session.sh -n SESSION_NAME -f file.ndjson --owner "Sean" --route file.gpx
#   SERVER_URL=http://localhost:8096 ADMIN_TOKEN=dev-token scripts/import-session.sh ...
#
# Caveat: the recording upload endpoint doesn't associate rows to a user, so the admin panel's
# per-member position counts will read as zero for the imported data even though the session
# replays correctly on the map.

set -euo pipefail

SERVER_URL="${SERVER_URL:-http://localhost:8080}"
ADMIN_TOKEN="${ADMIN_TOKEN:-dev-token}"
DATA_DIR="data/sessions"
CODE=""
SESSION_NAME=""
OWNER_NAME=""
FILE=""
ROUTE_FILE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dir) DATA_DIR="$2"; shift 2 ;;
    -n|--name) SESSION_NAME="$2"; shift 2 ;;
    -f|--file) FILE="$2"; shift 2 ;;
    --owner) OWNER_NAME="$2"; shift 2 ;;
    --route) ROUTE_FILE="$2"; shift 2 ;;
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

if [[ -n "$CODE" && ( -n "$SESSION_NAME" || -n "$FILE" ) ]]; then
  echo "Pass either CODE (directory mode) or -n/-f (bare-file mode), not both." >&2
  exit 1
fi

if [[ -z "$CODE" && -z "$SESSION_NAME" ]]; then
  echo "Usage:" >&2
  echo "  scripts/import-session.sh CODE [--dir DIR]" >&2
  echo "  scripts/import-session.sh -n SESSION_NAME -f FILE [--owner NAME] [--route FILE]" >&2
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

if [[ -n "$CODE" ]]; then
  SRC="$DATA_DIR/$CODE"
  if [[ ! -d "$SRC" ]]; then
    echo "No downloaded data at $SRC (run scripts/download-session.sh $CODE first)." >&2
    exit 1
  fi
  SESSION_NAME=$("$PY" -c "import json; print(json.load(open('$SRC/info.json'))['sessionName'])")
  OWNER_NAME=$("$PY" -c "import json; print(json.load(open('$SRC/info.json'))['ownerDisplayName'])")
  FILE="$SRC/recording.ndjson"
  [[ -f "$SRC/route.gpx" ]] && ROUTE_FILE="$SRC/route.gpx"
else
  OWNER_NAME="${OWNER_NAME:-Imported}"
  # Mirrors the server's own NormalizeSessionName so a bad name fails here, with the rule spelled
  # out, rather than as a bare 400 after the throwaway account has already been registered.
  if [[ ! "$SESSION_NAME" =~ ^[A-Za-z0-9_-]{4,8}$ ]]; then
    echo "Session name must be 4-8 letters, numbers, dashes, or underscores (got '$SESSION_NAME')." >&2
    exit 1
  fi
  SESSION_NAME=$(echo "$SESSION_NAME" | tr '[:lower:]' '[:upper:]')
fi

if [[ -z "$FILE" || ! -f "$FILE" ]]; then
  echo "No such recording file: ${FILE:-<none>} (pass one with -f, or run download-session.sh first)." >&2
  exit 1
fi
if [[ ! -s "$FILE" ]]; then
  echo "$FILE is empty — the server rejects a recording with no location updates." >&2
  exit 1
fi

LINES=$(grep -c '[^[:space:]]' "$FILE" || true)
echo "==> Importing $FILE ($LINES lines) as '$SESSION_NAME' into $SERVER_URL"

USERNAME="import_$(echo "$SESSION_NAME" | tr '[:upper:]' '[:lower:]')_$(date +%s)"
PASSWORD="Passw0rd!$(date +%s)"

echo "==> Registering throwaway owner account ($USERNAME) ..."
# Plain -s (not -f): a transport failure (can't connect at all) must be told apart from an HTTP
# error response (e.g. 409 for a duplicate session name) — curl only exits non-zero for the
# former, and -f would also swallow the latter's body, hiding the actual reason below.
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

if [[ -n "$ROUTE_FILE" ]]; then
  echo "==> Uploading route ..."
  if ! ROUTE_STATUS=$(curl -s -o /dev/null -w '%{http_code}' \
    -X POST "$SERVER_URL/sessions/$SESSION_ID/route" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H "Content-Type: application/gpx+xml" \
    --data-binary "@$ROUTE_FILE"); then
    echo "  Could not reach $SERVER_URL to upload the route." >&2
  elif [[ "$ROUTE_STATUS" != "200" ]]; then
    echo "  Route upload failed (HTTP $ROUTE_STATUS)" >&2
  else
    echo "  route uploaded"
  fi
fi

echo ""
echo "==> Done."
echo "  Session:     $SESSION_NAME"
echo "  Session ID:  $SESSION_ID"
echo "  Invite code: $INVITE_CODE"
echo "  Replay URL:  http://localhost:5173/code/$INVITE_CODE"
echo ""
echo "  (Throwaway owner account '$USERNAME' left in place; delete via"
echo "   DELETE $SERVER_URL/me with that user's token if you want it gone.)"
