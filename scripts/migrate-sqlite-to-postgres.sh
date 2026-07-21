#!/bin/bash
# Step 3 of .ai/plans/shared-database-cutover.md: one-off migration of Staging's and
# Production's SQLite data into the shared Postgres schema created by SessionStore.Initialize().
#
# Step 2 (2026-07-22 audit) found zero username/invite_code collisions between the two
# environments, so this does a straight merge — no discriminator column, no dedup logic. It
# re-checks for collisions before inserting as a safety net in case new rows landed in either
# environment since that audit.
#
# location_updates.id is NOT copied — SQLite's AUTOINCREMENT and Postgres's
# GENERATED ALWAYS AS IDENTITY are different id spaces, and Postgres must generate its own.
#
# Uses CSV export + psql's \copy rather than sqlite3's `.mode insert` INSERT-statement export:
# .mode insert renders embedded newlines (e.g. session_routes.gpx_content, a multi-line GPX
# file) using SQLite-only functions (char()/unistr()) that Postgres's parser rejects outright.
# CSV (RFC 4180 quoting, which both sqlite3 -csv and psql \copy speak natively) has no such
# problem — arbitrary text content, including embedded newlines, round-trips safely.
#
# Usage:
#   scripts/migrate-sqlite-to-postgres.sh <staging.db> <production.db> "<postgres-connection-string>"
#
# Always dry-run this against a disposable Postgres first (e.g. the local docker-compose one:
#   docker compose up -d
#   scripts/init-postgres-schema.sh "postgresql://dotwatcher:dotwatcher-dev@localhost:5432/dotwatcher"
#   scripts/migrate-sqlite-to-postgres.sh staging.db production.db \
#     "postgresql://dotwatcher:dotwatcher-dev@localhost:5432/dotwatcher"
# ) before ever pointing it at a real shared instance. This script does not fetch the SQLite
# files itself — reuse scripts/audit-user-collisions.sh's kubectl cp step, or pass local copies.

set -euo pipefail

STAGING_DB="${1:?Usage: $0 <staging.db> <production.db> <postgres-connection-string>}"
PRODUCTION_DB="${2:?Usage: $0 <staging.db> <production.db> <postgres-connection-string>}"
PG_CONN="${3:?Usage: $0 <staging.db> <production.db> <postgres-connection-string>}"

for f in "$STAGING_DB" "$PRODUCTION_DB"; do
  [[ -f "$f" ]] || { echo "ERROR: $f not found" >&2; exit 1; }
done

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

echo "==> Re-checking for collisions before inserting anything (safety net, not a re-audit)"
sqlite3 "$STAGING_DB" "SELECT username FROM users ORDER BY username;" > "$WORKDIR/staging_usernames.txt"
sqlite3 "$PRODUCTION_DB" "SELECT username FROM users ORDER BY username;" > "$WORKDIR/production_usernames.txt"
sqlite3 "$STAGING_DB" "SELECT invite_code FROM app_sessions ORDER BY invite_code;" > "$WORKDIR/staging_invite_codes.txt"
sqlite3 "$PRODUCTION_DB" "SELECT invite_code FROM app_sessions ORDER BY invite_code;" > "$WORKDIR/production_invite_codes.txt"

USERNAME_COLLISIONS=$(comm -12 "$WORKDIR/staging_usernames.txt" "$WORKDIR/production_usernames.txt" | wc -l | tr -d ' ')
INVITE_COLLISIONS=$(comm -12 "$WORKDIR/staging_invite_codes.txt" "$WORKDIR/production_invite_codes.txt" | wc -l | tr -d ' ')

if [[ "$USERNAME_COLLISIONS" != "0" || "$INVITE_COLLISIONS" != "0" ]]; then
  echo "ERROR: found $USERNAME_COLLISIONS username collision(s) and $INVITE_COLLISIONS invite_code collision(s)." >&2
  echo "The merge-everything decision (2026-07-22) assumed zero collisions. Stopping — do not proceed" >&2
  echo "without re-deciding Step 2 for these specific rows." >&2
  exit 1
fi
echo "  OK: 0 username collisions, 0 invite_code collisions"

table_exists() {
  local db="$1" table="$2"
  local result
  result=$(sqlite3 "$db" "SELECT name FROM sqlite_master WHERE type='table' AND name='$table';")
  [[ -n "$result" ]]
}

# Table order matters: parents before children, to satisfy foreign keys.
# Format: "table_name:col1,col2,...". location_updates omits id — Postgres's IDENTITY column
# generates its own, since SQLite's AUTOINCREMENT ids aren't meaningful across two source DBs.
TABLE_SPECS=(
  "users:id,username,display_name,password_hash,created_at"
  "app_sessions:id,session_name,invite_code,owner_user_id,created_at,archived_at"
  "session_members:session_id,user_id,role,display_name,joined_at,left_at"
  "revoked_user_tokens:token_id,expires_at,revoked_at"
  "session_routes:session_id,gpx_content,uploaded_at"
  "location_updates:session_id,runner_user_id,runner_name,latitude,longitude,heading,timestamp"
)

copy_table() {
  local db="$1" spec="$2" env_label="$3"
  local table="${spec%%:*}" cols="${spec#*:}"

  if ! table_exists "$db" "$table"; then
    echo "  [skip] $table not present in $db ($env_label) — schema drift between environments, not a bug" >&2
    return 0
  fi

  local csv_file="$WORKDIR/${env_label}_${table}.csv"
  sqlite3 -csv "$db" "SELECT $cols FROM $table;" > "$csv_file"

  # wc -l on the CSV file would overcount whenever a column has embedded newlines (e.g.
  # session_routes.gpx_content) — ask sqlite3 directly for the real row count instead.
  local row_count
  row_count=$(sqlite3 "$db" "SELECT count(*) FROM $table;")
  if [[ "$row_count" == "0" ]]; then
    echo "  [empty] $table has 0 rows in $db ($env_label), skipping copy" >&2
    return 0
  fi

  echo "  Copying $row_count row(s) of $table from $env_label"
  psql "$PG_CONN" -v ON_ERROR_STOP=1 -c "\\copy $table($cols) FROM '$csv_file' WITH (FORMAT csv)"
}

echo "==> Copying tables (production first, then staging)"
# Note: each \copy runs as its own psql invocation, so this isn't wrapped in one cross-table
# transaction. For this data volume (tens/thousands of rows), re-running after truncating any
# partially-loaded tables is an acceptable rollback story for a dry run; a production execution
# of Step 4 should reassess this if it needs atomicity across the whole merge.
for env_db_label in "production:$PRODUCTION_DB" "staging:$STAGING_DB"; do
  env_label="${env_db_label%%:*}"
  db="${env_db_label#*:}"
  for spec in "${TABLE_SPECS[@]}"; do
    copy_table "$db" "$spec" "$env_label"
  done
done

echo "==> Verifying row counts post-copy"
psql "$PG_CONN" -c "
SELECT 'users' AS table, count(*) FROM users
UNION ALL SELECT 'app_sessions', count(*) FROM app_sessions
UNION ALL SELECT 'session_members', count(*) FROM session_members
UNION ALL SELECT 'location_updates', count(*) FROM location_updates
UNION ALL SELECT 'revoked_user_tokens', count(*) FROM revoked_user_tokens
UNION ALL SELECT 'session_routes', count(*) FROM session_routes;
"

echo "==> Done. CSV files were in $WORKDIR until this script exits (then auto-deleted)."
echo "    Re-run with the trap commented out if you want to keep/inspect them."
