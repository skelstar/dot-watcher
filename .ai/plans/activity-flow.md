# Activity flow: what each dot did, as a line

Branches: `activity-flow-server`, then `activity-flow-admin` (the web admin tab), `activity-flow-ios` and
`activity-flow-android` (only for the "opened the app" event). The server branch is useful alone: it starts
recording, and the admin tab can follow when there is data to look at.

Mock-up: https://claude.ai/artifact/QZ4YAJsNjJrbu5vvxjR8LJ (the "Flow: AB's evening" board; the
time-based board beside it is the rejected first idea).

## Why

Working out what someone did means reading a Seq request log from the bottom up and translating paths in
your head: `POST /casual/start`, five `GET /casual/live`, `POST /casual/watch-link`, an anonymous
`GET /watch/{token}`... The story is all there but hard to see. We want a picture: one line per dot with
stops for what they did, in order, and branches off it when something else touched them (someone opened
their watch link, someone joined their crew).

## Why a table and not Seq

We looked at drawing this from Seq (its HTTP API, or a Seq App plug-in). Seq can't host a custom view, and
reading it back means guessing meaning from paths, an API key in the cluster, and history that depends on
Seq's retention. The database doesn't help either: it holds current state only, and the casual clean-up
deletes runs and watch links after 7 days.

So the server records the events it cares about in its own table, written at the moment they happen. Seq
stays as it is, for debugging.

## Today

- Every request is logged once, at the end of the request-logging middleware in `server/Program.cs`, with
  `RequestMethod`, `RequestPath`, `StatusCode`, `UserId`, `DeviceId`, `ClientId`, `AppVersion`.
- `RequestPath` is the real path (only `/watch/{token}` is rewritten), so a route table needs the endpoint's
  route pattern (`(ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText`), not the path.
- The middleware finds the user by authenticating the request **after** it ran. `POST /auth/logout` has
  revoked the token by then, which is why sign-outs show with no runner in Seq. Login and register have no
  token at all; `AuthController` sets `Log:Runner` for those.
- **Nothing records an app being opened.** Both apps call `GET /me` when they come to the front (iOS
  `ContentView` `scenePhase == .active` → `fetchPalette()` → `refreshPalette()`; Android
  `MainActivity.onResume` → `refreshPalette()`), but only to refresh the palette, and `GET /me` is called
  from other places too, so it can't stand in for "opened".
- The watch page (`client/src/WatchPage.tsx`) polls `GET /watch/{token}` every 10 s.

## The table

```sql
CREATE TABLE IF NOT EXISTS activity_events (
  id            BIGSERIAL PRIMARY KEY,
  at            TIMESTAMPTZ NOT NULL,
  user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE, -- whose line it is on
  kind          TEXT NOT NULL,
  other_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,         -- the other person, if any
  device_id     TEXT,
  client        TEXT,            -- ios | android | web
  app_version   TEXT,
  run_id        TEXT,            -- no foreign key: history outlives the casual clean-up
  session_id    TEXT,
  dedupe_key    TEXT,            -- see "Not recording the same thing twice"
  detail        JSONB
);
CREATE INDEX IF NOT EXISTS idx_activity_user_at ON activity_events(user_id, at);
CREATE UNIQUE INDEX IF NOT EXISTS idx_activity_dedupe ON activity_events(dedupe_key) WHERE dedupe_key IS NOT NULL;
```

Created with the other tables in `SessionStore`; reads and writes in a new `Stores/SessionStore.Activity.cs`,
like the other partials.

`user_id` is always the dot whose line the event belongs on. An event caused by someone else on that line
(an anonymous viewer, a new crew member) is a branch: the `kind` says so, and `other_user_id` names them
when they have an account.

## What gets recorded

Only successful requests (status below 400). Kinds are past-tense and stable; the admin page owns the
wording.

**On the line (the dot did it):**

| Kind | Trigger | Detail |
|---|---|---|
| `app_opened` | `POST /me/activity` (new, below) | |
| `signed_in` | `POST /auth/login` | |
| `registered` | `POST /auth/register` | |
| `signed_out` | `POST /auth/logout` | |
| `run_started` | `POST /casual/start` | |
| `run_stopped` | `POST /casual/stop` | `durationSeconds` |
| `watch_link_made` | `POST /casual/watch-link` | |
| `watch_link_removed` | `DELETE /casual/watch-link` | |
| `route_added` / `route_removed` | `POST` / `DELETE /casual/route` | |
| `profile_changed` | `PUT /me/profile` | |
| `go_settings_changed` | `PUT /me/casual/settings` | |
| `crew_code_made` | `POST /me/crew/codes` | |
| `crew_joined` | `POST /me/crew/codes/{code}/redeem` | `other_user_id` = code owner |
| `crew_member_removed` | `DELETE /me/crew/{userId}` | `other_user_id` |
| `blocked` / `unblocked` | `POST` / `DELETE /me/blocks/{userId}` | `other_user_id` |
| `session_created` | `POST /sessions` | `session_id` |
| `session_joined` | `POST /session-invites/{inviteCode}/join`, `POST /nearby/sessions/{sessionId}/join` | `session_id`, `detail.via` |
| `session_left` | `DELETE /me/sessions/{sessionId}/membership` | `session_id` |
| `session_ended` | `POST /sessions/{sessionId}/end` | `session_id` |

**Branches (someone else, on this dot's line):**

| Kind | Trigger | Notes |
|---|---|---|
| `watch_link_opened` | `GET /watch/{token}` | On the run owner's line. Once per browser per run (see below). No account, so no `other_user_id`. |
| `crew_member_joined` | the same redeem as `crew_joined` | On the code owner's line, `other_user_id` = the person who joined. One request writes both rows. |

Left out on purpose: polling and loads (`GET /casual/live`, `GET /locations/...`, `POST /location`, crew and
nearby reads), everything under `/admin`, the casual clean-up, and account deletion (its rows go with the
account anyway).

## Writing it

1. **A route table in one place**, `ActivityRoutes`: `(method, route pattern) → kind`. The middleware looks it
   up after `next()`; no match or status ≥ 400 means nothing is written.
2. **Controllers fill in what the middleware can't see**, through `HttpContext.Items`, like the existing
   `Log:` keys:
   - `Activity:UserId`: set by `AuthController` on login, register **and logout**. Logout sets it before
     revoking the token. This also fixes the missing runner on sign-outs in Seq, if `Log:Runner` is set at
     the same place.
   - `Activity:RunId`: casual start, stop, watch link, route, and the watch controller (with
     `Activity:UserId` = the run's owner).
   - `Activity:OtherUserId`, `Activity:SessionId`, `Activity:Detail` where the tables above say.
3. **Never slow or fail a request.** The middleware puts the event on a `Channel<ActivityEvent>`; a
   `BackgroundService` drains it and inserts in batches. A failed insert is logged to Seq and dropped.

### Not recording the same thing twice

- `watch_link_opened`: `dedupe_key = "watch:{runId}:{X-Browser-Id}"`, insert with `ON CONFLICT DO NOTHING`.
  With no browser id the key falls back to the request's IP hash, so a page left open doesn't log 6 a minute.
- `app_opened`: the writer skips it if the same device sent one in the last 60 s. That covers flicking
  between apps and the double foreground some Android dialogs cause.

## The "opened the app" event

`POST /me/activity` with `{ "kind": "app_opened" }`. Signed-in only, and the server accepts only kinds the
apps are allowed to send (just `app_opened` for now). Returns 204. Fire and forget on the client: no retry,
no error shown.

- iOS: in `ContentView`'s `scenePhase` `.active` branch, beside `fetchPalette()`, when signed in.
- Android: in `MainActivity.onResume`, beside `appViewModel.refreshPalette()`, when signed in.

Older app versions never send it; their lines just start at the first thing they did.

## Retention

`CasualMaintenanceService` also deletes `activity_events` older than `Activity:RetentionDays` (default 30)
on its hourly pass. Not shown on the flow.

## The admin tab

`GET /admin/activity/dots?day=2026-11-19` returns who has events that day (user id, name, colour, count).
`GET /admin/activity?day=…&userIds=a,b` returns their events in order. Admin-token only, like the rest of
`/admin`.

In `client/src/AdminPanel.tsx`, a new **Activity** tab:

- Pick a day, then one or more dots. **One line per dot**, stacked, each labelled with the initials dot.
- Stops are **evenly spaced, in order**, not to time scale. The time is under each stop's name.
- The line's look shows state: thicker and orange while a run is going (from `run_started` to
  `run_stopped`), dashed while signed out.
- Branches go below the line for `watch_link_opened` and `crew_member_joined`, from the stop they belong to
  (the watch link made, the crew code made).
- A long gap (over 15 min) between stops shows as a break in the line with the length ("32 min later").

Drawn in plain React, as SVG or positioned elements; no chart library.

## Tests (CI runs them)

- A start, a watch link and a stop each write one row with the run id; a 4xx writes nothing.
- Logout writes `signed_out` against the right user.
- Ten polls of `/watch/{token}` from one browser write one `watch_link_opened`; a second browser writes another.
- Redeeming a crew code writes `crew_joined` and `crew_member_joined`.
- `POST /me/activity` needs a signed-in user, rejects unknown kinds, and merges a repeat within 60 s.
- Deleting an account deletes its rows; the maintenance pass deletes rows past retention.
- The admin endpoints refuse a user token.

## Open questions

1. **Runs that end without Stop** (the app was killed, or the clean-up closed it): should the line show "run
   ended" at the run's `ended_at`? That would come from the clean-up, which we said not to show.
2. **The branches above the line** in the mock-up (the map refreshing during a run): worth a per-run count,
   for example `live_polls` on the `run_stopped` row from an in-memory counter, or drop them?
3. **Failed sign-ins**: useful as red stops when helping someone who "can't log in", but they have no user
   until we match the username. Later?
4. 30 days of retention: right?

## Status, 20 November 2026

Plan only. Nothing built.
