# Plan: Staging/Production shared-database cutover

Status: Step 1 (cluster inventory) done as of 2026-07-22. Written 2026-07-20. Steps 2-4 are still
design/runbook only — nothing described in them has been executed against the live cluster or its
data.

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

## Step 3 — build and dry-run a one-off migration script (script built, dry-run partially done)

`scripts/migrate-sqlite-to-postgres.sh` is written:
- Usage: `scripts/migrate-sqlite-to-postgres.sh <staging.db> <production.db> "<postgres-connection-string>"`.
  Takes local copies of both SQLite files (fetch them with the same `kubectl cp` pattern as
  `scripts/audit-user-collisions.sh`) — never touches live files.
- Re-checks for username/invite_code collisions before inserting anything, as a safety net in
  case new rows landed in either environment after the 2026-07-22 Step 2 audit. Aborts if any
  are found, since the merge-everything decision assumed zero.
- Dumps every row from `users`, `app_sessions`, `session_members`, `revoked_user_tokens`,
  `session_routes` via `sqlite3 .mode insert`, in parent-before-child order to satisfy foreign
  keys (`users` → `app_sessions` → `session_members`/`location_updates`).
- Deliberately does **not** copy `location_updates.id` — SQLite's `AUTOINCREMENT` and Postgres's
  `GENERATED ALWAYS AS IDENTITY` are different id spaces, so that table is dumped with an explicit
  column list and lets Postgres generate fresh ids.
- Applies the generated SQL with `psql -v ON_ERROR_STOP=1` inside a single transaction, then
  prints post-insert row counts per table for verification.

**Dry-run status**: verified the SQL-generation half against hand-built fixture SQLite files
matching the real schema (checked table order, foreign-key satisfaction, and the
`location_updates` column-list handling produce correct, well-formed `INSERT` statements — 9
rows in, 9 `INSERT`s out, ids omitted correctly for `location_updates`). **Not yet verified**:
that the generated SQL actually applies cleanly to a real Postgres instance — this dev machine
has no local Docker/psql available. Before Step 4, still need to: spin up the local
`docker compose up -d` Postgres (or any disposable instance), pull real copies of both
environments' SQLite files via `kubectl cp`, and run the script against them end-to-end,
confirming the printed post-insert row counts match each environment's known counts (Staging: 1
user/3 sessions; Production: 8 users/2 sessions, per the Step 2 audit) and spot-checking a
handful of sessions/positions.

## Step 4 — rollout sequence

1. Provision a Postgres instance reachable from both the `dot-watcher-server` and
   `dot-watcher-server-staging` namespaces (in-cluster deployment with its own PVC, or an external
   instance) — this becomes the one shared database for both environments.
2. Add `ConnectionString` to the existing `dot-watcher-server-secrets` k8s secret (and create the
   equivalent secret for Staging, following the same
   `kubectl create secret generic ... --from-env-file=...` pattern already documented in
   `server/README.md`'s Deployment section).
3. Run the migration script from Step 3 once, against the real (not copied) SQLite data, during a
   maintenance window.
4. Redeploy both `dot-watcher-server` and `dot-watcher-server-staging` pointing at the new
   `ConnectionString`.
5. Verify via the admin `GET /sessions` endpoint and Seq logs (`https://seq.skelstar.io`) that both
   environments see the same session/user data and that new writes from either environment show up
   for the other.
6. Keep the old SQLite PVCs mounted-but-unused (don't delete) for a rollback window before cleaning
   them up.

## Named risk to flag for whoever executes this, not fixed in Phase 2

Every `SessionStore` call is synchronous, blocking ADO.NET I/O. Against local SQLite that was
sub-millisecond disk I/O; against one shared Postgres instance serving live GPS pings from every
runner in every active session across *both* environments at once, blocking network round-trips
held on ASP.NET Core thread-pool threads is a real thread-pool-starvation risk under load in a way
SQLite never was. Not a blocker for cutover, but worth keeping an eye on once both environments'
traffic lands on one instance — the next step up (not scoped here) would be an async rewrite of
`SessionStore` and/or `NpgsqlDataSource`-level connection pooling tuning.
