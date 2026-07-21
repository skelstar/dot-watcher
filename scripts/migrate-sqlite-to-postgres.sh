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
# Usage:
#   scripts/migrate-sqlite-to-postgres.sh <staging.db> <production.db> "<postgres-connection-string>"
#
# Always dry-run this against a disposable Postgres first (e.g. the local docker-compose one:
#   docker compose up -d
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

# Table order matters: parents before children, to satisfy foreign keys.
TABLES_WITH_ID_COPY="users app_sessions session_members revoked_user_tokens session_routes"

SQL_FILE="$WORKDIR/migration.sql"
: > "$SQL_FILE"

table_exists() {
  local db="$1" table="$2"
  local result
  result=$(sqlite3 "$db" "SELECT name FROM sqlite_master WHERE type='table' AND name='$table';")
  [[ -n "$result" ]]
}

dump_table() {
  local db="$1" table="$2"
  if ! table_exists "$db" "$table"; then
    echo "  [skip] $table not present in $db (schema drift between environments — not a bug, just older/newer deploy)" >&2
    return 0
  fi
  # .mode insert emits standard `INSERT INTO table VALUES (...)` statements with proper quoting.
  sqlite3 "$db" <<SQL >> "$SQL_FILE"
.mode insert $table
SELECT * FROM $table;
SQL
}

dump_location_updates() {
  local db="$1"
  if ! table_exists "$db" "location_updates"; then
    echo "  [skip] location_updates not present in $db" >&2
    return 0
  fi
  # Explicit column list, omitting id, so Postgres's IDENTITY column generates fresh ids.
  sqlite3 "$db" <<SQL >> "$SQL_FILE"
.mode insert location_updates
SELECT session_id, runner_user_id, runner_name, latitude, longitude, heading, timestamp FROM location_updates;
SQL
}

echo "==> Generating INSERT statements"
echo "BEGIN;" >> "$SQL_FILE"
for env_db in "$PRODUCTION_DB" "$STAGING_DB"; do
  for t in $TABLES_WITH_ID_COPY; do
    dump_table "$env_db" "$t"
  done
  dump_location_updates "$env_db"
done
echo "COMMIT;" >> "$SQL_FILE"

# sqlite3's .mode insert writes `INSERT INTO location_updates VALUES(...)` with all columns
# by default; the queries above already select an explicit column list, but sqlite3 names the
# statement after the table given to `.mode insert`, not the column list, so patch the generated
# statement to match: SELECT'ing 7 columns still produces `INSERT INTO location_updates VALUES(...)`
# with 7 values, which fails against Postgres's 8-column table (extra `id`). Rewrite to add the
# explicit column list matching the SELECT above.
sed -i.bak \
  -e 's/^INSERT INTO location_updates VALUES/INSERT INTO location_updates(session_id,runner_user_id,runner_name,latitude,longitude,heading,timestamp) VALUES/' \
  "$SQL_FILE"
rm -f "$SQL_FILE.bak"

ROWS=$(grep -c '^INSERT INTO' "$SQL_FILE" || true)
echo "==> Generated $ROWS INSERT statements → $SQL_FILE"

echo "==> Applying to $PG_CONN"
psql "$PG_CONN" -v ON_ERROR_STOP=1 -f "$SQL_FILE"

echo "==> Verifying row counts post-insert"
psql "$PG_CONN" -c "
SELECT 'users' AS table, count(*) FROM users
UNION ALL SELECT 'app_sessions', count(*) FROM app_sessions
UNION ALL SELECT 'session_members', count(*) FROM session_members
UNION ALL SELECT 'location_updates', count(*) FROM location_updates
UNION ALL SELECT 'revoked_user_tokens', count(*) FROM revoked_user_tokens
UNION ALL SELECT 'session_routes', count(*) FROM session_routes;
"

echo "==> Done. SQL file was at $WORKDIR/migration.sql until this script exits (then auto-deleted)."
echo "    Re-run with the trap commented out if you want to keep/inspect the generated SQL."
