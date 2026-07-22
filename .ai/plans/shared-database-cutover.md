# Plan: Staging/Production shared-database cutover

Status: **CUTOVER COMPLETE as of 2026-07-22.** Written 2026-07-20. Both Staging and Production are
now deployed on the Postgres-backed build and pointed at the one shared Postgres instance.
Verified via `GET /sessions` against both `dot-watcher-staging.skelstar.io` and
`dot-watcher.skelstar.io` returning the identical set of 4 session IDs — proof both environments
are reading the same database. Remaining work is just cleanup (old SQLite PVCs) and the async
`SessionStore` risk noted below, not core to the cutover itself.

## Goal

Point both the Staging (`dot-watcher-server-staging`) and Production (`dot-watcher-server`)
deployments at one shared Postgres database, so a Staging user and a Production user can share a
session. Phase 2 (this repo, done) ported `SessionStore` off SQLite onto Postgres — Local dev and
CI now run against Postgres, but Staging and Production still each run their own private SQLite
file today and the code no longer has a SQLite fallback at all. **This build must not be deployed
to either environment until the prep work below is done** — without a reachable Postgres and a
configured `ConnectionString`, the server crashes on startup.

## Why this is a separate, deliberate phase

- Both environments have real, live user data: Production has been running since 2026-06-13 with
  a persistent SQLite volume (`8f85533`, "Persist SQLite database to a dedicated volume"), so by
  the time this plan was written there was roughly 5 weeks of real accumulated sessions/positions.
  Staging is used the same way — `scripts/download-session.sh`/`scripts/import-session.sh` exist
  specifically to pull real session recordings *from staging* for local repro, implying it holds
  representative, not throwaway, data.
- Staging's deployment (namespace, secret, PVC) isn't tracked in this repo at all — only
  Production's `server/.deploy.yaml` is committed. The naming convention
  (`dot-watcher-server-staging`) is only known from a code comment in `server/Program.cs`.
- No SQLite→Postgres data-migration or export tooling exists anywhere in the repo today — this
  needs to be built from scratch as part of executing this plan, not assumed to exist.
- I (the agent that wrote this plan) don't have direct access to the live k3s cluster (Tatooine) —
  execution requires the user, or a future session with that access, to run each step.

## Step 1 — inventory the live cluster before touching anything (DONE, 2026-07-22)

Confirmed via `kubectl` on Tatooine:
- Namespace: `dot-watcher-server-staging` (confirmed — Active, 10 days old at time of check,
  i.e. created around 2026-07-12). Sibling namespaces `dot-watcher-client-staging` (10d),
  `dot-watcher-server`/`dot-watcher-client` (both 38d, Production) also confirmed to exist.
- Secret: `dot-watcher-server-secrets` in `dot-watcher-server-staging`, type `Opaque`, 2 keys —
  `BearerToken` (9 bytes) and `JwtSigningKey` (44 bytes). Same name and shape as Production's
  secret, just namespaced separately — the `--from-env-file` recreation pattern in
  `server/README.md` applies unchanged.
- PVCs (both `Bound`, `local-path` storage class, RWO):
  - `dot-watcher-db` — 100Mi (the SQLite DB file volume)
  - `dot-watcher-recordings` — 1Gi
- Ingress: host `dot-watcher-staging.skelstar.io`, `traefik` ingress class, routes `/api` prefix
  (stripped via the `dot-watcher-server-staging-strip-api-prefix` middleware) to service
  `dot-watcher-server:8080`. Created 2026-07-11.

  **Resolved the hostname discrepancy flagged when this plan was written**: Staging's real
  ingress hostname is `dot-watcher-staging.skelstar.io`, matching what iOS build settings and
  `scripts/README.md` already assumed. The `dot-watcher-server.skelstar.io` hostname sitting in
  `server/.deploy.yaml` doesn't match Staging's real host or Production's real host
  (`dot-watcher.skelstar.io`) — confirmed stale/wrong, and should not be used as a template for a
  new Staging manifest without fixing that host first.

## Step 2 — decide how to reconcile the two existing datasets (DONE, 2026-07-22)

Audited both environments' live SQLite files via `scripts/audit-user-collisions.sh` (pulled with
`kubectl cp` from each namespace's pod, read locally with `sqlite3`, never touched in place):

| | Staging | Production |
|---|---|---|
| `users` | 1 | 8 |
| `app_sessions` | 3 | 2 |
| `users.created_at` range | 2026-07-12 (single account) | 2026-07-02 → 2026-07-21 |
| `app_sessions.created_at` range | 2026-07-12 → 2026-07-17 | 2026-07-09 → 2026-07-21 |
| colliding `username`s | 0 | |
| colliding `invite_code`s | 0 | |

**Decision: merge everything.** Zero collisions on both unique-constrained columns means Staging's
rows can be inserted into the shared Postgres schema as plain inserts alongside Production's — no
discriminator column, no dedup/rename logic, no per-row conflict resolution needed. The migration
script in Step 3 does not need to handle a collision case at all (though it should still assert
zero collisions as a safety check before inserting, in case new rows land in either environment
between this audit and Step 3 executing).

## Step 3 — build and dry-run a one-off migration script (DONE, 2026-07-22)

`scripts/migrate-sqlite-to-postgres.sh`:
- Usage: `scripts/migrate-sqlite-to-postgres.sh <staging.db> <production.db> "<postgres-connection-string>"`.
  Takes local copies of both SQLite files (fetch them with the same `kubectl cp` pattern as
  `scripts/audit-user-collisions.sh`) — never touches live files.
- Re-checks for username/invite_code collisions before inserting anything, as a safety net in
  case new rows landed in either environment after the 2026-07-22 Step 2 audit. Aborts if any
  are found, since the merge-everything decision assumed zero.
- Exports every row from `users`, `app_sessions`, `session_members`, `revoked_user_tokens`,
  `session_routes`, `location_updates` via `sqlite3 -csv`, then loads each via
  `psql ... \copy table(cols) FROM file WITH (FORMAT csv)`, in parent-before-child order to
  satisfy foreign keys (`users` → `app_sessions` → `session_members`/`location_updates`).
  Uses CSV rather than `sqlite3 .mode insert`-generated `INSERT` statements because the latter
  renders embedded newlines (e.g. `session_routes.gpx_content`, a multi-line GPX file) using
  SQLite-only functions (`char()`/`unistr()`) that Postgres's parser rejects outright — hit this
  for real on the first dry-run attempt. CSV's RFC 4180 quoting round-trips arbitrary text
  content safely.
- Deliberately does **not** copy `location_updates.id` — SQLite's `AUTOINCREMENT` and Postgres's
  `GENERATED ALWAYS AS IDENTITY` are different id spaces, so that table is exported with an
  explicit column list and Postgres generates fresh ids.
- Tolerates schema drift between environments (e.g. `session_routes` — added 2026-07-16 — was
  present in one environment's SQLite file but not the other at dry-run time): checks each table
  exists before exporting it rather than assuming parity, and skips missing/empty tables with a
  clear log line instead of erroring.
- Companion script `scripts/init-postgres-schema.sh` creates the target schema in a Postgres
  instance without needing the .NET SDK (Tatooine has Docker/kubectl but no `dotnet`) — extracts
  the exact `CREATE TABLE` block straight out of `SessionStore.cs` so it can't drift from what
  `SessionStore.Initialize()` actually creates.

**Dry-run verified end-to-end on Tatooine** against the local `docker compose up -d` Postgres,
using real copies of both environments' SQLite files (pulled via `kubectl cp`). Zero collisions
found (consistent with the Step 2 audit), and every table copied cleanly. Final counts: 9 users,
5 app_sessions, 8 session_members, 1833 location_updates, 5 revoked_user_tokens, 1 session_routes
— matches the Step 2 audit's per-environment counts (8+1 users, 2+3 app_sessions) with no errors.

## Step 4 — rollout sequence

1. **DONE (2026-07-22)** — Provisioned a Postgres instance reachable from both the
   `dot-watcher-server` and `dot-watcher-server-staging` namespaces: an in-cluster Deployment +
   PVC + Service in its own `dot-watcher-db` namespace (`scripts/shared-postgres.yaml`, merged via
   PR #74). Confirmed reachable cross-namespace from `dot-watcher-server-staging` at
   `postgres.dot-watcher-db.svc.cluster.local:5432` via a live `psql SELECT 1` test. Schema
   created via `scripts/init-postgres-schema.sh`.
2. **DONE (2026-07-22)** — Added `ConnectionString` to both `dot-watcher-server-secrets` secrets
   (Production and Staging namespaces) via `kubectl patch` (surgical add of one key, preserving
   the existing `BearerToken`/`JwtSigningKey`), rather than recreating the secret from scratch.
   Value uses the in-cluster DNS name:
   `Host=postgres.dot-watcher-db.svc.cluster.local;Port=5432;Database=dotwatcher;Username=dotwatcher;Password=<...>`.
   **Gotcha hit during execution**: adding the key to the *secret* alone did not make it reach the
   container — each Deployment's pod spec only had explicit `env` entries with `secretKeyRef` for
   `BearerToken`/`JwtSigningKey`; `ConnectionString` needed its own new `env` entry added via
   `kubectl patch deployment ... --type=json -p='[{"op":"add","path":"/spec/template/spec/containers/0/env/-",...}]'`
   in both namespaces before the app would actually see it.
3. **DONE (2026-07-22)** — Ran the migration script against the real (not copied) SQLite data
   from both environments, against the real shared Postgres instance (not the local dry-run one).
   Zero collisions, no errors. Final counts in the shared instance: 9 users, 5 app_sessions, 8
   session_members, 1833 location_updates, 5 revoked_user_tokens, 1 session_routes — matches the
   Step 2 audit. **Important nuance**: this only copied data *into* the new instance — Staging and
   Production are still both running unmodified, still reading/writing their own private SQLite
   files. Nothing user-facing has changed yet, and the shared instance will drift out of sync with
   each environment's SQLite file for any writes that happen between this copy and Step 4's actual
   cutover (step 4 below) — a final delta/re-sync (or re-running this step) right before cutover
   is worth considering if there's a gap in time.
4. **DONE (2026-07-22)** — Redeployed Staging first, verified healthy, then Production, each onto
   the Postgres-backed build with `ConnectionString` wired. Both came up clean after the env-var
   patch (0 restarts). The `libgssapi_krb5.so.2 cannot open shared object file` message seen in
   both pods' startup logs is a harmless Npgsql GSSAPI-auth-probe warning (the connection uses
   plain username/password auth, not GSSAPI) — not an error, and not worth fixing.
5. **DONE (2026-07-22)** — Verified via the admin `GET /sessions` endpoint: both
   `https://dot-watcher-staging.skelstar.io/api/sessions` and
   `https://dot-watcher.skelstar.io/api/sessions` return the **identical** set of 4 session IDs
   (`FRIC0001`, `COMMUTE1`, `WAIMAP2`, `WUU2K65` — the 5th session, `TEST1`, is unarchived and
   excluded from this endpoint's results, which is pre-existing filtering behavior, not a bug).
   This is definitive proof both environments are reading the same shared database. Did not
   separately verify Seq logs — the `GET /sessions` match was conclusive enough on its own.
6. **Not yet done** — Keep the old SQLite PVCs mounted-but-unused (don't delete) for a rollback
   window before cleaning them up. Since both environments' code no longer reads SQLite at all,
   these PVCs are now just inert leftover storage — safe to leave as-is; revisit deleting them
   once confident no rollback will be needed.

## Follow-up needed: `server/.deploy.yaml` is now out of sync with the live cluster state

The live `dot-watcher-server` and `dot-watcher-server-staging` Deployments were patched directly
via `kubectl patch` to add the `ConnectionString` env var (see Step 4.2 above) — but
`server/.deploy.yaml`, the source file the `/deploy` skill uses to (re)create this deployment,
was **not** updated to match. It still only has `DbPath` (the old SQLite path, now dead) and no
`ConnectionString` entry at all. It also still has the stale hostname
`dot-watcher-server.skelstar.io` flagged back in Step 1 (Production's real hostname is
`dot-watcher.skelstar.io`).

**Risk**: if `/deploy update dot-watcher-server` (or a from-scratch deploy after a namespace
teardown) ever regenerates the Deployment from this file, it would recreate it *without* the
`ConnectionString` env var — silently reintroducing the exact startup crash this whole cutover
was meant to fix, since the live patch would be overwritten/not reapplied.

**Not fixed here** because the `/deploy` skill's `.deploy.yaml` schema wasn't confirmed to support
`valueFrom.secretKeyRef`-style env vars (only plain `value: ...` literals appear in the file
today) — writing a `ConnectionString` entry into this file without knowing whether the skill can
express "pull from a secret key" risks baking in something that looks right but silently deploys
wrong (e.g. a literal, hardcoded connection string checked into the repo, which would be a real
credential leak). Whoever picks this up next should check the `/deploy` skill's actual schema
support before editing this file, then fix both the missing `ConnectionString` entry and the
stale hostname together.

## Named risk to flag for whoever executes this, not fixed in Phase 2

Every `SessionStore` call is synchronous, blocking ADO.NET I/O. Against local SQLite that was
sub-millisecond disk I/O; against one shared Postgres instance serving live GPS pings from every
runner in every active session across *both* environments at once, blocking network round-trips
held on ASP.NET Core thread-pool threads is a real thread-pool-starvation risk under load in a way
SQLite never was. Not a blocker for cutover, but worth keeping an eye on once both environments'
traffic lands on one instance — the next step up (not scoped here) would be an async rewrite of
`SessionStore` and/or `NpgsqlDataSource`-level connection pooling tuning.
