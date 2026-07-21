#!/bin/bash
# Creates the SessionStore schema in a Postgres instance without needing the .NET SDK installed
# (e.g. on Tatooine, which runs containers but has no dotnet toolchain). Extracts the exact
# CREATE TABLE block straight out of server/Stores/SessionStore.cs so it can never drift from
# what SessionStore.Initialize() actually creates at app startup.
#
# Usage:
#   scripts/init-postgres-schema.sh "<postgres-connection-string>"
#
# Example (local docker-compose Postgres):
#   scripts/init-postgres-schema.sh "postgresql://dotwatcher:dotwatcher-dev@localhost:5432/dotwatcher"

set -euo pipefail

PG_CONN="${1:?Usage: $0 <postgres-connection-string>}"
SOURCE_FILE="$(dirname "$0")/../server/Stores/SessionStore.cs"

[[ -f "$SOURCE_FILE" ]] || { echo "ERROR: $SOURCE_FILE not found" >&2; exit 1; }

# The schema lives between the first CommandText = """ opening and its closing """ in
# Initialize() — later methods reuse the same cmd.CommandText = """ pattern for migrations, so
# stop after the first closing delimiter instead of matching every occurrence in the file.
SQL=$(awk '
  done { next }
  /cmd.CommandText = """/ { flag=1; next }
  flag && /^[ \t]*""";/ { flag=0; done=1; next }
  flag
' "$SOURCE_FILE")

if [[ -z "$SQL" ]]; then
  echo "ERROR: couldn't extract CREATE TABLE block from $SOURCE_FILE — has SessionStore.cs's format changed?" >&2
  exit 1
fi

echo "==> Applying schema to $PG_CONN"
echo "$SQL" | psql "$PG_CONN" -v ON_ERROR_STOP=1

echo "==> Tables now present:"
psql "$PG_CONN" -c "\dt"
