#!/bin/bash
# Stores the nine start-screen palettes from the landing-sign mock-ups (.ai/mockups/landing-signs.html) in a
# server's palette library, so they can be
# assigned to app versions (the admin Palette tab, or PUT /admin/palettes/assignments/{appVersion}).
#
# Safe to run twice: a palette whose name is already in the library is skipped, never duplicated or changed.
# Nothing is assigned, so no phone sees any of them until you assign one.
#
# Usage:
#   scripts/store-palettes.sh --dry-run
#   SERVER_URL=https://dot-watcher-staging.skelstar.io/api ADMIN_TOKEN=... scripts/store-palettes.sh
#
# Staging and Production share one database, so storing on either puts them in the library for both.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_PORT=8080
ROOT_ENV="$SCRIPT_DIR/../.env"
if [[ -f "$ROOT_ENV" ]]; then
  ENV_PORT=$(grep -E '^s*VITE_SERVER_PORTs*=' "$ROOT_ENV" | tail -1 | cut -d= -f2- | xargs)
  [[ -n "$ENV_PORT" ]] && DEFAULT_PORT="$ENV_PORT"
fi

SERVER_URL="${SERVER_URL:-http://localhost:$DEFAULT_PORT}"
ADMIN_TOKEN="${ADMIN_TOKEN:-dev-token}"
DRY_RUN=0

for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    -h|--help) grep '^#' "$0" | sed 's/^# {0,1}//'; exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 1 ;;
  esac
done

# Probed by actually running it, not just `command -v`: on Windows, `python3` can be the Store stub.
PY=""
for candidate in python3 python; do
  if "$candidate" -c "import json" >/dev/null 2>&1; then PY="$candidate"; break; fi
done
[[ -n "$PY" ]] || { echo "python3 (or python) is required (used for JSON)." >&2; exit 1; }

PALETTES='[
  {"version":1,"name":"Lava Go (swatches A)","goBg":"#6f1a07","goFg":"#f7f3e3","goBar":"#af9164","altBg":"#f7f3e3","altFg":"#2b2118","accent":"#af9164","accentInk":"#2b2118","screen":"#ffffff"},
  {"version":1,"name":"Coffee Go (swatches B)","goBg":"#2b2118","goFg":"#af9164","goBar":"#6f1a07","altBg":"#f7f3e3","altFg":"#2b2118","accent":"#6f1a07","accentInk":"#f7f3e3","screen":"#ffffff"},
  {"version":1,"name":"Camel Go (swatches C)","goBg":"#af9164","goFg":"#2b2118","goBar":"#6f1a07","altBg":"#b3b6b7","altFg":"#2b2118","accent":"#2b2118","accentInk":"#f7f3e3","screen":"#ffffff"},
  {"version":1,"name":"Sign: green + yellow","goBg":"#243a30","goFg":"#efbf3f","goBar":"#efbf3f","altBg":"#f6efd9","altFg":"#243a30","accent":"#efbf3f","accentInk":"#243a30","screen":"#ffffff"},
  {"version":1,"name":"Forest + coral","goBg":"#243a30","goFg":"#efbf3f","goBar":"#efbf3f","altBg":"#f6efd9","altFg":"#243a30","accent":"#f08a5d","accentInk":"#2a1a12","screen":"#ffffff"},
  {"version":1,"name":"Navy + orange","goBg":"#1f3350","goFg":"#f2efe4","goBar":"#f2efe4","altBg":"#f6f1e4","altFg":"#1f3350","accent":"#f28c28","accentInk":"#1f2a3d","screen":"#ffffff"},
  {"version":1,"name":"Forest + teal","goBg":"#243a30","goFg":"#efbf3f","goBar":"#efbf3f","altBg":"#f6efd9","altFg":"#243a30","accent":"#8fd0d6","accentInk":"#173a3a","screen":"#ffffff"},
  {"version":1,"name":"Charcoal + orange","goBg":"#2a2d2f","goFg":"#ffb81c","goBar":"#ffb81c","altBg":"#f4f1ea","altFg":"#2a2d2f","accent":"#ff6a2b","accentInk":"#2a2d2f","screen":"#ffffff"},
  {"version":1,"name":"Berry + cream","goBg":"#5a1f3a","goFg":"#f6d58a","goBar":"#f6d58a","altBg":"#f8efe2","altFg":"#5a1f3a","accent":"#e9b949","accentInk":"#3d1226","screen":"#ffffff"}
]'

echo "Server: $SERVER_URL"
EXISTING=$(curl -sS --fail -H "Authorization: Bearer $ADMIN_TOKEN" "$SERVER_URL/admin/palettes") || {
  echo "Could not list palettes (wrong ADMIN_TOKEN, wrong SERVER_URL, or a server without the palette library)." >&2
  exit 1
}

# One line per palette to store: its name, a tab, then its JSON. Names already in the library are left out.
TO_STORE=$(EXISTING="$EXISTING" PALETTES="$PALETTES" "$PY" -c '
import json, os
have = {e["palette"].get("name") for e in json.loads(os.environ["EXISTING"])}
for p in json.loads(os.environ["PALETTES"]):
    if p["name"] in have:
        print("SKIP	" + p["name"])
    else:
        print("ADD	" + p["name"] + "	" + json.dumps(p))
')

stored=0; skipped=0
while IFS=$'	' read -r action name body; do
  [[ -z "${action:-}" ]] && continue
  if [[ "$action" == "SKIP" ]]; then
    echo "  already there: $name"; skipped=$((skipped + 1)); continue
  fi
  if [[ "$DRY_RUN" == 1 ]]; then
    echo "  would store:   $name"; continue
  fi
  status=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$SERVER_URL/admin/palettes"     -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" -d "$body")
  if [[ "$status" == 201 ]]; then
    echo "  stored:        $name"; stored=$((stored + 1))
  else
    echo "  FAILED ($status): $name" >&2; exit 1
  fi
done <<< "$TO_STORE"

echo "Done: $stored stored, $skipped already there."
