# Plan: Staging/Production shared-database cutover

Status: drafted, not started. Written 2026-07-20. This is a design/runbook document only —
nothing described here has been executed against the live cluster or its data.

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

## Step 1 — inventory the live cluster before touching anything

Confirm, on Tatooine, what's actually deployed for Staging:
- Namespace name (`dot-watcher-server-staging`, presumed but unconfirmed in-repo).
- Its k8s secret name and contents (equivalent of `dot-watcher-server-secrets`).
- Its PVC(s) for the SQLite DB file and recordings, and their current size.
- Its actual `hostname`/ingress config, to reconcile against the real staging hostname
  `dot-watcher-staging.skelstar.io` (seen in iOS build settings and `scripts/README.md`) — the
  hostname in `server/.deploy.yaml` (`dot-watcher-server.skelstar.io`) matches neither Staging's
  real hostname nor Production's real hostname (`dot-watcher.skelstar.io`) and should be reconciled
  before using that file as a template for anything new.

## Step 2 — decide how to reconcile the two existing datasets (user decision, not mine)

Staging's and Production's `users`/`app_sessions` tables were created completely independently.
Before any merge, someone needs to decide:
- Do Staging's rows get merged into the shared Postgres alongside Production's, kept apart via a
  discriminator column, or discarded as disposable/test data?
- If merged: what happens on a `username` collision (the same username registered independently in
  both) or an `invite_code` collision (both are unique-constrained per-database today, not
  globally)? Concretely audit both databases for overlapping `username`/`invite_code` values before
  deciding — don't assume there's no collision.

This plan intentionally does not pick an answer — it's a product/data decision, not an engineering
one.

## Step 3 — build and dry-run a one-off migration script

Once Step 2's decision is made:
- Write a small one-off tool (a console script, or even a `sqlite3 .dump` + transform + `psql`
  pipeline) that reads every table out of a copy of each environment's SQLite file
  (`users`, `app_sessions`, `session_members`, `location_updates`, `revoked_user_tokens`,
  `session_routes`) and inserts the rows into the new shared Postgres schema created by
  `SessionStore.Initialize()`.
- Run it first against **copies** of both SQLite files pulled down locally (the existing
  `scripts/download-session.sh` pattern is a precedent for pulling data off Staging/Production
  safely) — never against the live files directly.
- Verify row counts per table and spot-check a handful of sessions/positions after the dry run
  before touching anything live.

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
