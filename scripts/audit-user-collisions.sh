#!/bin/bash
# Step 2 prep for .ai/plans/shared-database-cutover.md: audits Staging's and Production's
# SQLite databases for username/invite_code overlap before any merge decision is made.
#
# Read-only. Copies each pod's SQLite file out via `kubectl cp` (never touches the live file in
# place) into a local tmp dir, then diffs username/invite_code lists and prints row counts and
# created_at ranges so you can judge how much real data is actually at stake.
#
# Run this ON TATOOINE (needs kubectl pointed at the k3s cluster).
#
# Usage:
#   scripts/audit-user-collisions.sh

set -euo pipefail

STAGING_NS="dot-watcher-server-staging"
PROD_NS="dot-watcher-server"
DB_PATH_IN_POD="/data/db/dotwatcher.db"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

fetch_db() {
  local ns="$1" out="$2"
  local pod_count pod container

  pod_count=$(kubectl get pods -n "$ns" --no-headers | wc -l | tr -d ' ')
  if [[ "$pod_count" != "1" ]]; then
    echo "ERROR: expected exactly 1 pod in namespace $ns, found $pod_count. Not guessing which one — inspect manually with 'kubectl get pods -n $ns'." >&2
    exit 1
  fi

  pod=$(kubectl get pods -n "$ns" -o jsonpath='{.items[0].metadata.name}')
  container=$(kubectl get pod "$pod" -n "$ns" -o jsonpath='{.spec.containers[0].name}')
  echo "==> Copying $DB_PATH_IN_POD from $ns/$pod (container: $container)"
  kubectl cp "$ns/$pod:$DB_PATH_IN_POD" "$out" -c "$container"
}

fetch_db "$STAGING_NS" "$WORKDIR/staging.db"
fetch_db "$PROD_NS" "$WORKDIR/production.db"

echo
echo "==> Row counts"
for env in staging production; do
  echo "--- $env ---"
  sqlite3 "$WORKDIR/$env.db" <<SQL
SELECT 'users: ' || count(*) FROM users;
SELECT 'app_sessions: ' || count(*) FROM app_sessions;
SELECT 'users created_at range: ' || min(created_at) || ' .. ' || max(created_at) FROM users;
SELECT 'app_sessions created_at range: ' || min(created_at) || ' .. ' || max(created_at) FROM app_sessions;
SQL
done

echo
echo "==> Extracting username/invite_code lists"
sqlite3 "$WORKDIR/staging.db" "SELECT username FROM users ORDER BY username;" > "$WORKDIR/staging_usernames.txt"
sqlite3 "$WORKDIR/production.db" "SELECT username FROM users ORDER BY username;" > "$WORKDIR/production_usernames.txt"
sqlite3 "$WORKDIR/staging.db" "SELECT invite_code FROM app_sessions ORDER BY invite_code;" > "$WORKDIR/staging_invite_codes.txt"
sqlite3 "$WORKDIR/production.db" "SELECT invite_code FROM app_sessions ORDER BY invite_code;" > "$WORKDIR/production_invite_codes.txt"

echo
echo "==> Username collisions (present in both environments)"
comm -12 "$WORKDIR/staging_usernames.txt" "$WORKDIR/production_usernames.txt" | tee "$WORKDIR/username_collisions.txt"
COUNT=$(wc -l < "$WORKDIR/username_collisions.txt" | tr -d ' ')
echo "($COUNT colliding username(s))"

echo
echo "==> invite_code collisions (present in both environments)"
comm -12 "$WORKDIR/staging_invite_codes.txt" "$WORKDIR/production_invite_codes.txt" | tee "$WORKDIR/invite_code_collisions.txt"
COUNT=$(wc -l < "$WORKDIR/invite_code_collisions.txt" | tr -d ' ')
echo "($COUNT colliding invite_code(s))"

echo
echo "==> Done. Raw DB copies were left in $WORKDIR until this script exits (then auto-deleted)."
echo "    Re-run with 'bash -x' or comment out the trap if you want to inspect them further."
