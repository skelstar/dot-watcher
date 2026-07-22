#!/bin/bash
# Downloads a session's data via its invite code and saves it locally — no login/access token
# needed, since every /session-invites/{code}/... route is unauthenticated by design (that's
# what lets a viewer open a shared link without an account).
#
# Usage:
#   scripts/download-session.sh CODE
#   scripts/download-session.sh CODE --out data/sessions
#   SERVER_URL=https://dot-watcher-staging.skelstar.io/api scripts/download-session.sh CODE
#
# Writes into <out>/<CODE>/:
#   info.json        - SessionInfo (sessionName, ownerDisplayName)
#   meta.json         - RecordingMeta (runStartTimestamp, latestTimestamp)
#   recording.ndjson  - the full latest-run recording (one JSON object per line)
#   route.gpx          - the saved route, if the session has one (skipped otherwise)

set -euo pipefail

SERVER_URL="${SERVER_URL:-https://dot-watcher-staging.skelstar.io/api}"
OUT_DIR="data/sessions"
CODE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) OUT_DIR="$2"; shift 2 ;;
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
  echo "Usage: scripts/download-session.sh CODE [--out DIR]" >&2
  exit 1
fi

DEST="$OUT_DIR/$CODE"
mkdir -p "$DEST"
BASE="$SERVER_URL/session-invites/$CODE"

fetch() {
  local path="$1" out="$2"
  local status
  status=$(curl -s -o "$out" -w '%{http_code}' "$BASE/$path")
  if [[ "$status" != "200" ]]; then
    echo "  [skip] $path -> HTTP $status" >&2
    rm -f "$out"
    return 1
  fi
}

echo "==> Downloading session $CODE from $SERVER_URL"

if fetch "" "$DEST/info.json"; then
  echo "  saved info.json"
fi

if fetch "recording/meta" "$DEST/meta.json"; then
  echo "  saved meta.json"
fi

if fetch "recording" "$DEST/recording.ndjson"; then
  LINES=$(wc -l < "$DEST/recording.ndjson" | tr -d ' ')
  echo "  saved recording.ndjson ($LINES lines)"
fi

if fetch "route" "$DEST/route.gpx"; then
  echo "  saved route.gpx"
fi

echo "==> Done. Files in $DEST/"
