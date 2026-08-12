# Persist `isUltraConstrained` and `nextExpectedAt` — Plan

*DotWatcher — plan, August 2026*

## Context

Both `isUltraConstrained` (satellite flag, see `.ai/plans/satelite-connectivity.md`) and
`nextExpectedAt` (adaptive-cadence heartbeat, see `.ai/plans/POST-nextExpectedAt.md`) are reported
by iOS on every `POST /location` and flow through `LocationUpdate` → `ValidatedLocationUpdate` →
`RunnerPosition`. Both are fully wired end-to-end **for live viewing**: `SessionStore.AddPosition`
stores them on the in-memory `RunnerPosition` history, `GetLatestPositions`/`GET /locations/{id}`
return them, the client already renders both (the satellite dish icon and the countdown ring in
`Legend.tsx`, via `findUltraConstrainedRunners`/`findAdaptiveCountdowns` in
`useSessionTimelineLogic.ts`).

What's missing for both is durability. `location_updates` (the Postgres table
`SessionStore.Initialize()` creates) has no columns for either, so:

- `AddPosition` never writes either to Postgres — only to the in-memory `_sessions` cache
  (`server/Stores/SessionStore.cs:737-775`).
- Every read path that falls back to Postgres constructs its `RunnerPosition`/`LocationUpdate`
  without the fields, so they silently default (`false` / `null`):
  - `LoadLatestPositionsByRunner` (multi-pod live-position fallback, `:845-869`)
  - `LoadUpdatesByTimestamp`, backing `GetRecordingAsNdjson` (`:990-994`, `:1102-1127`)
  - `GetRecordingWindowAsNdjson`, backing the scrubber's windowed fetch (`:999-1052`) — **this is
    the one that actually matters for playback**, since it's what the client calls while scrubbing
  - `SaveRecording`/`ParseRecordingLine`, the NDJSON re-import path (`:1149-1209`)

Net effect: a viewer sees the satellite badge or adaptive countdown only for positions they
personally live-polled while the run was happening. Scrubbing into history, reloading after a
server restart, or re-importing a downloaded session always reads `isUltraConstrained: false` /
`nextExpectedAt: null`, regardless of what actually happened. Both limitations are already
documented as deliberately-deferred in `satelite-connectivity.md` and `POST-nextExpectedAt.md`,
which both note "worth doing together if/when this gets picked up" — this plan does both in one
pass, since the fix is the same shape of change at the same six touch points.

## Goal

Make both fields durable so every read path — live, scrubbed history, post-restart, re-imported —
reflects what iOS actually reported at post time.

## Non-goals

- **No client changes.** `findUltraConstrainedRunners`, `findAdaptiveCountdowns`, and `Legend.tsx`
  already do the right thing with whatever values `RunnerPosition.isUltraConstrained`/
  `nextExpectedAt` carry. The gap is entirely server-side (where those values come from on a
  Postgres-backed read) — once the columns exist and are populated, the existing client code
  renders history correctly with zero edits.
- **No new migration framework.** Uses the same `ADD COLUMN IF NOT EXISTS` pattern
  `SessionStore.Initialize()` already anticipates (see `scripts/init-postgres-schema.sh`'s header
  comment). Flagged as a longer-term open question, not solved here.

## Backward compatibility (the hard requirement)

This must be a zero-downtime, fully additive change — no client update required, no data loss,
no behavior change for anything that predates it:

- **Existing Postgres rows.** `is_ultra_constrained` gets `NOT NULL DEFAULT FALSE` — every
  pre-existing row backfills to `false`, which is correct: none of them could have reported
  satellite before this column existed. `next_expected_at` is added nullable with no default —
  every pre-existing row reads back as `NULL`/absent, matching what those positions actually
  reported (nothing, since older posts may predate the field or simply didn't set it).
- **Schema migration itself.** Postgres 11+ treats `ADD COLUMN ... DEFAULT <constant>` as a fast
  metadata-only change (no full table rewrite, no long lock) — safe to run against the live
  Production table with existing rows, every time the app starts (`ADD COLUMN IF NOT EXISTS` is
  idempotent, so re-running it on every `Initialize()` call is a no-op after the first).
- **Old iOS clients that never send these fields.** Unaffected — `LocationUpdate.NextExpectedAt`/
  `IsUltraConstrained` already default to `null`/`false` on deserialize (existing optional-field
  design); the new columns just store whatever default was already being computed.
- **Old NDJSON recordings/downloads that predate these fields.** `ParseRecordingLine` already
  deserializes missing fields to their defaults — no change in behavior, they just now also
  persist that default instead of it being silently dropped.
- **Wire format.** Purely additive — no renamed/removed columns, no API shape change. Both fields
  already exist on `RunnerPosition`/`LocationUpdate` today; this only changes where the server
  sources their values from for historical (Postgres-backed) reads. Nothing about `POST /location`,
  `GET /locations/{id}`, or the NDJSON shape changes.
- **`scripts/init-postgres-schema.sh`.** Only extracts the *first* `cmd.CommandText = """` block
  from `SessionStore.cs` (by its own header comment's design). A bare Postgres instance it seeds
  won't have the two new columns until the .NET app's own `Initialize()` runs against it — which
  happens automatically and immediately on first app start in every environment. No change needed
  to that script; nothing depends on it alone producing the final schema.

## Proposed changes

### 1. Schema — `SessionStore.Initialize()`

Add a second idempotent statement block right after the existing `CREATE TABLE` block:

```sql
ALTER TABLE location_updates
    ADD COLUMN IF NOT EXISTS is_ultra_constrained BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE location_updates
    ADD COLUMN IF NOT EXISTS next_expected_at TEXT;
```

`next_expected_at` is `TEXT` (not a native `TIMESTAMPTZ`), matching every other timestamp column in
this table/schema (`timestamp`, `created_at`, etc.), all stored as ISO-8601 strings via
`DateTimeOffset.ToString("O")` / parsed via `DateTimeOffset.Parse`.

### 2. Write path — `AddPosition`

Extend the `INSERT` to include both columns, bound from `update.IsUltraConstrained` (always has a
value) and `update.NextExpectedAt` (nullable — bind with the same `DBNull.Value` fallback pattern
the existing `heading` parameter already uses). Both values are already computed on
`ValidatedLocationUpdate` today; they're just dropped before the Postgres write.

### 3. Read paths — thread both columns through all three Postgres-backed reads

Each of these needs `next_expected_at, is_ultra_constrained` added to its `SELECT` list, read via
`reader.IsDBNull(n) ? null : DateTimeOffset.Parse(reader.GetString(n))` and `reader.GetBoolean(n)`
respectively, and threaded into the `RunnerPosition`/`LocationUpdate` constructor call:

- `LoadLatestPositionsByRunner`
- `LoadUpdatesByTimestamp` (→ `GetRecordingAsNdjson`)
- `GetRecordingWindowAsNdjson` (the scrubber's actual data source while replaying)

### 4. Round-trip on re-import — `SaveRecording` / `ParseRecordingLine`

Extend its `INSERT` to also write both columns from the parsed `ValidatedLocationUpdate` (again
mirroring the existing nullable `heading` parameter pattern for `next_expected_at`), so a session
downloaded via `scripts/download-session.sh` and re-imported via `scripts/import-session.sh` (or
the admin recording-upload endpoint directly) doesn't silently lose either field on the round trip.

### 5. Comment cleanup (accuracy, not behavior)

A few existing code comments assert the current (soon-to-be-false) limitation as fact and would
actively mislead future readers if left as-is:

- `server/Models/RunnerPosition.cs`: `IsUltraConstrained`'s doc comment says "False for older
  clients, the demo runner, or any position loaded back from Postgres ... this is
  in-memory-only, like NextExpectedAt" — update to reflect that both now round-trip through
  Postgres.
- `SessionStore.GetLatestPositions`'s comment on the Postgres fallback: "The in-memory copy is
  preferred when present since it carries `nextExpectedAt`/`isUltraConstrained`, neither of which
  has a column in `location_updates`" — update; the in-memory copy is still preferred for
  freshness/latency, but no longer because these fields don't survive Postgres.

### 6. Tests — `tests/DotWatcher.Server.Tests`

- `LocationsApiTests`: add `IsUltraConstrained` round-trip tests on `GET /locations/{id}` mirroring
  the existing `NextExpectedAt` ones (true and default-false cases) — regression coverage for the
  in-memory path, which already works today.
- `SessionsApiTests`: the tests that actually matter, since these paths are the ones that are
  currently broken:
  - `GET .../recording` (full, no since/until → `GetRecordingAsNdjson`/`LoadUpdatesByTimestamp`)
    includes both fields after a `POST /location` that set them.
  - `GET .../recording?since=&until=` (→ `GetRecordingWindowAsNdjson`) does too — this is the
    scrubber's real code path.
  - The existing two-factory "different pod" pattern
    (`GetLocationsByInviteCode_WhenPostLandedOnADifferentPod_StillReturnsThePosition`) extended to
    assert both fields survive the cross-pod Postgres fallback (`LoadLatestPositionsByRunner`).
  - A re-import round trip: `POST /sessions/{id}/recording` with NDJSON containing both fields set
    → `GET .../recording` still shows them.

### 7. Docs

- `docs/database-schema.html`: add `is_ultra_constrained` and `next_expected_at` rows to the
  `location_updates` table card.
- `.ai/plans/satelite-connectivity.md`'s "Known limitation — live-only, not persisted" section and
  the matching open item in `POST-nextExpectedAt.md`: update once shipped, the same way `e5ddb4c`
  documented the original limitation — both fields now persist.

## Effort estimate

Small. Two columns, one write-path edit, three read-path edits (all the same shape, done together
per field), one re-import edit, a handful of tests, two doc updates. No client-side code changes.
