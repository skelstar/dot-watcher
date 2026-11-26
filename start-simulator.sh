#!/bin/bash
# Starts the simulator (tools/simulator) at http://localhost:5174.
#
# Usage:
#   ./start-simulator.sh --prod    # against prod (npm run dev:prod)
#   ./start-simulator.sh --local   # against a local server on localhost:8080 (npm run dev)
#
# One of --prod or --local is required.
#
# Prod mode reads tools/simulator/.env.prod.local. The first run copies it from
# .env.prod.example and stops so you can set VITE_BEARER_TOKEN (prod's admin token).
# Installs npm packages when node_modules is missing or package-lock.json is newer.
# Ctrl-C stops it.

set -euo pipefail

usage() {
  echo "Usage: $0 --prod | --local" >&2
  exit 1
}

[[ $# -eq 1 ]] || usage
case "$1" in
  --prod) MODE="prod" ;;
  --local) MODE="local" ;;
  -h|--help)
    grep '^#' "$0" | sed 's/^# \{0,1\}//'
    exit 0
    ;;
  *) usage ;;
esac

SIM_DIR="$(cd "$(dirname "$0")/tools/simulator" && pwd)"
cd "$SIM_DIR"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm not found. Install Node.js 18+ first." >&2
  exit 1
fi

if [[ ! -d node_modules || package-lock.json -nt node_modules ]]; then
  echo "==> Installing npm packages ..."
  npm install
  touch node_modules
fi

if [[ "$MODE" == "prod" ]]; then
  if [[ ! -f .env.prod.example ]]; then
    echo "tools/simulator/.env.prod.example is missing. Is this branch up to date with staging?" >&2
    exit 1
  fi
  if [[ ! -f .env.prod.local ]]; then
    cp .env.prod.example .env.prod.local
    echo "Created tools/simulator/.env.prod.local."
    echo "Set VITE_BEARER_TOKEN in it to prod's admin token, then run this again."
    exit 1
  fi
  if ! grep -Eq '^VITE_BEARER_TOKEN=.+' .env.prod.local; then
    echo "VITE_BEARER_TOKEN is empty in tools/simulator/.env.prod.local." >&2
    echo "Set it to prod's admin token, then run this again." >&2
    exit 1
  fi
  echo "==> Simulator against PROD at http://localhost:5174 (sim- accounts on prod get deleted by Create session)"
  exec npm run dev:prod
else
  if ! curl -s -o /dev/null --max-time 3 http://localhost:8080; then
    echo "Warning: nothing answering on http://localhost:8080. Start the server first." >&2
  fi
  echo "==> Simulator against the local server at http://localhost:5174"
  exec npm run dev
fi
