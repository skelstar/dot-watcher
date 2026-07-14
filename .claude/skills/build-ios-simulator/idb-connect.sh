#!/bin/bash
# Starts idb_companion for a simulator and connects the idb CLI to it, so
# `idb ui tap` / `idb ui describe-all` work immediately.
#
# Usage: ./idb-connect.sh <UDID>
#
# idb is pip-installed (fb-idb) but its CLI entrypoint lives outside the
# default PATH; a login shell picks it up via ~/.zshrc, but non-interactive
# shells (like this script run standalone) may not, so it's exported here too.
set -euo pipefail

export PATH="/Users/skelstar/Library/Python/3.9/bin:$PATH"

UDID="${1:?Usage: idb-connect.sh <UDID>}"
LOG_FILE="$(mktemp -t idb_companion.XXXXXX.log)"

# Always start fresh — killing a stale companion for this UDID is cheap and
# avoids guessing whether an existing process is still healthy/reachable.
pkill -f "idb_companion --udid $UDID" 2>/dev/null || true

idb_companion --udid "$UDID" > "$LOG_FILE" 2>&1 &

PORT=""
for _ in $(seq 1 20); do
  sleep 0.5
  PORT=$(grep -o 'Starting swift server on tcp port [0-9]*' "$LOG_FILE" | grep -o '[0-9]*$' || true)
  [ -n "$PORT" ] && break
done

if [ -z "$PORT" ]; then
  echo "idb_companion did not report a port in time; see $LOG_FILE" >&2
  exit 1
fi

idb connect localhost "$PORT"
echo "Connected to $UDID on port $PORT. Try: idb ui describe-all --udid $UDID"
