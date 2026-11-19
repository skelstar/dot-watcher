# Activity flow: what each dot did, as a line

Branches: `activity-flow-mvp` first (server and admin tab, see MVP). After it: `activity-flow-server` for the
rest of the events, and `activity-flow-ios` / `activity-flow-android` for the "opened the app" event.

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
- `RequestPath` is the real path (only `/watch/{token}` is rewritten), so matching events to routes needs
  the endpoint's route pattern (`(ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText`), not the path.
- The middleware finds the user by authenticating the request **after** it ran. `POST /auth/logout` has
  revoked the token by then, which is why sign-outs show with no runner in Seq. Login and register have no
  token at all; `AuthController` sets `Log:Runner` for those.
- **Nothing records an app being opened.** Both apps call `GET /me` when they come to the front (iOS
  `ContentView` `scenePhase == .active` → `fetchPalette()` → `refreshPalette()`; Android
  `MainActivity.onResume` → `refreshPalette()`), but only to refresh the palette, and `GET /me` is called
  from other places too, so it can't stand in for "opened".
- The watch page (`client/src/WatchPage.tsx`) polls `GET /watch/{token}` every 10 s.

## MVP

The smallest thing that shows a Go run as a flow. One branch, `activity-flow-mvp`, server and admin tab
together.

**Records four events**, all from the server, no app changes:

| Kind | Label | Shows | Route |
|---|---|---|---|
| `run_started` | Started a Go run | line | `POST /casual/start` |
| `run_stopped` | Stopped the run | line | `POST /casual/stop` |
| `watch_link_made` | Shared a watch link | line | `POST /casual/watch-link` |
| `watch_link_opened` | Someone watched | branch from `watch_link_made` | `GET /watch/{token}` |

**Builds:**

1. The `activity_events` table (below), with account deletion cascading to it.
2. The register (`server/Activity/activity-events.json`) and its schema, holding these four as `recording` and every
   other event in this plan as `wanted`.
3. The middleware hook, the queue and the background writer (Writing it, below).
4. `Activity:RunId` set by `POST /casual/start`, `/casual/stop` and `/casual/watch-link`, and by
   `GET /watch/{token}` together with `Activity:UserId` = the run's owner.
5. Watch opens recorded once per browser per run (Not recording the same thing twice).
6. Deleting rows past retention in `CasualMaintenanceService`.
7. The admin endpoints and the Activity tab: pick a day, pick dots, one line per dot, evenly spaced stops
   with times, the line orange from `run_started` to `run_stopped`, and the "Someone watched" branch.
   Plus the read-only Register list.

**Leaves for later:** every other event in the register; `POST /me/activity` and the app changes; the
dashed signed-out line and the long-gap breaks (nothing to sign out from yet in the MVP's four events);
the open questions.

**Done when:** start a Go run on a phone, share the watch link, open it in a browser, stop the run, and the
admin Activity tab shows one line with three stops and a "Someone watched" branch.

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
(an anonymous viewer, a new crew member) is a branch, and `other_user_id` names them when they have an
account.

## The register

`server/Activity/activity-events.json` is the one list of events: what the server records and how the admin tab
words and places them. Embedded in the assembly (`EmbeddedResource` in the csproj, so a publish or a test
run can't lose it) and read once at start-up.

```json
{
  "$schema": "./activity-events.schema.json",
  "events": [
    {
      "kind": "watch_link_opened",
      "label": "Someone watched",
      "shows": "branch",
      "branchFrom": "watch_link_made",
      "route": "GET /watch/{token}",
      "status": "recording",
      "notes": "On the run owner's line. Once per browser per run."
    },
    {
      "kind": "dot_colour_changed",
      "label": "Changed their dot colour",
      "shows": "line",
      "status": "wanted",
      "describe": "When someone changes their dot colour"
    }
  ]
}
```

Fields:

| Field | Required | Meaning |
|---|---|---|
| `kind` | yes | Stable id, `snake_case`, past tense. What the table stores. |
| `label` | yes | The words on the flow. |
| `shows` | yes | `line` (the dot did it) or `branch` (someone else, on this dot's line). |
| `branchFrom` | for a branch | The `kind` of the stop it hangs off. Without one before it, it hangs off the nearest stop. |
| `route` | when `recording` | `"METHOD /route/{pattern}"`, matching the endpoint's route pattern. |
| `status` | yes | `wanted` (asked for, not built) or `recording`. |
| `describe` | for `wanted` | Plain words, for when the route isn't known yet. |
| `notes` | no | Anything the builder needs: what the controller must set, dedupe, detail fields. |

**Asking for an event:** add an entry with `"status": "wanted"` and a `describe`. The server ignores it. To
build it, fill in `route`, do any controller work, and change it to `recording`.

**`server/Activity/activity-events.schema.json`** (JSON Schema) gives VS Code dropdowns for `shows` and `status`,
the `kind` pattern `^[a-z][a-z0-9_]*$`, required fields per status (`route` when `recording`, `describe`
when `wanted`, `branchFrom` when `branch`), and hover text for each field.

**One route can write more than one event** (redeeming a crew code writes `crew_joined` and
`crew_member_joined`): the server maps each route to a list of kinds. `POST /me/activity` is the exception
the other way: one route, and the request body names the kind, which must be a `recording` entry whose
route is `POST /me/activity`.

**Checked in CI** by a server test: the file matches the schema, every `recording` route is a real endpoint,
every `branchFrom` names a kind in the file, and every kind the code writes directly is in the file. The test
output lists the `wanted` entries as still to build.

## Events in the register

The MVP's four are `recording`; the rest start as `wanted`. Once the file exists it is the truth and this
list is only its starting point. Only successful requests (status below 400) are recorded.

**On the line (the dot did it):**

| Kind | Route | Notes |
|---|---|---|
| `app_opened` | `POST /me/activity` (new, below) | |
| `signed_in` | `POST /auth/login` | |
| `registered` | `POST /auth/register` | |
| `signed_out` | `POST /auth/logout` | |
| `run_started` | `POST /casual/start` | MVP |
| `run_stopped` | `POST /casual/stop` | MVP. `detail.durationSeconds` |
| `watch_link_made` | `POST /casual/watch-link` | MVP |
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

| Kind | Route | Notes |
|---|---|---|
| `watch_link_opened` | `GET /watch/{token}` | MVP. From `watch_link_made`. On the run owner's line, no `other_user_id`. |
| `crew_member_joined` | `POST /me/crew/codes/{code}/redeem` | From `crew_code_made`. On the code owner's line, `other_user_id` = who joined. |

Left out on purpose: polling and loads (`GET /casual/live`, `GET /locations/...`, `POST /location`, crew and
nearby reads), everything under `/admin`, the casual clean-up, and account deletion (its rows go with the
account anyway).

## Writing it

1. **The register drives it.** At start-up the server builds `(method, route pattern) → kinds` from the
   `recording` entries. The middleware looks the request up after `next()`; no match or status ≥ 400 means
   nothing is written.
2. **Controllers fill in what the middleware can't see**, through `HttpContext.Items`, like the existing
   `Log:` keys:
   - `Activity:UserId`: set by `AuthController` on login, register **and logout**, and by the watch
     controller (the run's owner). Logout sets it before revoking the token. Setting `Log:Runner` at the same
     place fixes the missing runner on sign-outs in Seq too.
   - `Activity:RunId`: casual start, stop, watch link, route, and the watch controller.
   - `Activity:OtherUserId`, `Activity:SessionId`, `Activity:Detail` where the register's notes say.
3. **Never slow or fail a request.** The middleware puts the event on a `Channel<ActivityEvent>`; a
   `BackgroundService` drains it and inserts in batches. A failed insert is logged to Seq and dropped.

### Not recording the same thing twice

- `watch_link_opened`: `dedupe_key = "watch:{runId}:{X-Browser-Id}"`, insert with `ON CONFLICT DO NOTHING`.
  With no browser id the key falls back to a hash of the request's IP, so a page left open doesn't log 6 a
  minute.
- `app_opened`: the writer skips it if the same device sent one in the last 60 s. That covers flicking
  between apps and the double foreground some Android dialogs cause.

## The "opened the app" event (after the MVP)

`POST /me/activity` with `{ "kind": "app_opened" }`. Signed-in only; returns 204. Fire and forget on the
client: no retry, no error shown.

- iOS: in `ContentView`'s `scenePhase` `.active` branch, beside `fetchPalette()`, when signed in.
- Android: in `MainActivity.onResume`, beside `appViewModel.refreshPalette()`, when signed in.

Older app versions never send it; their lines just start at the first thing they did.

## Retention

`CasualMaintenanceService` also deletes `activity_events` older than `Activity:RetentionDays` (default 30)
on its hourly pass. Not shown on the flow.

## The admin tab

Endpoints, admin-token only like the rest of `/admin`:

- `GET /admin/activity/dots?day=2026-11-19`: who has events that day (user id, name, colour, count).
- `GET /admin/activity?day=…&userIds=a,b`: their events in order.
- `GET /admin/activity/register`: the register, for labels, `shows`, `branchFrom` and the Register list.

In `client/src/AdminPanel.tsx`, a new **Activity** tab:

- Pick a day, then one or more dots. **One line per dot**, stacked, each labelled with the initials dot.
- Stops are **evenly spaced, in order**, not to time scale. The label from the register, the time under it.
  A kind the register doesn't know shows as its raw `kind`.
- The line is thicker and orange while a run is going (from `run_started` to `run_stopped`). After the MVP:
  dashed while signed out, and a break with its length ("32 min later") for a gap over 15 min.
- Branches go below the line, from the stop their `branchFrom` names.
- A read-only **Register** section under the flow: each event's label, kind, route and status, with
  `wanted` ones marked, so you can see what's recorded beside what it draws.

Drawn in plain React, as SVG or positioned elements; no chart library.

## Tests (CI runs them)

MVP:

- The register matches its schema, its `recording` routes are real endpoints, its `branchFrom`s exist.
- A start, a watch link and a stop each write one row with the run id; a 4xx writes nothing.
- Ten polls of `/watch/{token}` from one browser write one `watch_link_opened` on the owner's line; a second
  browser writes another.
- Deleting an account deletes its rows; the maintenance pass deletes rows past retention.
- The admin endpoints refuse a user token.

Later:

- Logout writes `signed_out` against the right user.
- Redeeming a crew code writes `crew_joined` and `crew_member_joined`.
- `POST /me/activity` needs a signed-in user, rejects kinds not registered for it, and merges a repeat within
  60 s.

## Open questions

1. **Runs that end without Stop** (the app was killed, or the clean-up closed it): should the line show "run
   ended" at the run's `ended_at`? That would come from the clean-up, which we said not to show. In the MVP
   such a run simply stays orange to the end of the line.
2. **The branches above the line** in the mock-up (the map refreshing during a run): worth a per-run count,
   for example `live_polls` on the `run_stopped` row from an in-memory counter, or drop them?
3. **Failed sign-ins**: useful as red stops when helping someone who "can't log in", but they have no user
   until we match the username. Later?
4. 30 days of retention: right?

## Status, 20 November 2026

MVP written on `activity-flow-mvp`, not yet built or tested (left to CI):

- Server: `server/Activity/` (register, schema, `ActivityRegister`, `ActivityRecorder` + `ActivityWriterService`,
  `ActivityCapture` called from the request-logging middleware), `Stores/SessionStore.Activity.cs`,
  `Controllers/ActivityController.cs`, the casual and watch controllers, retention in `CasualMaintenanceService`.
- The admin endpoints take `from` and `to` instants (at most 7 days apart) rather than a day: the admin page
  works out the day in its own time zone.
- The watch page now sends `X-Browser-Id` (through `apiHeaders`), so each viewer counts once; the IP is only
  the fallback.
- Web: `client/src/ActivityTab.tsx` and `activityFlow.ts` (with tests), and the Activity tab in `AdminPanel.tsx`.
- Tests: `tests/DotWatcher.Server.Tests/ActivityTests.cs`. The test factory sets `Activity:WriteImmediately`.

Since then, on the same branch:

- `app_opened` and a new `app_backgrounded` are recording, through `POST /me/activity`
  (`Controllers/AppActivityController.cs`). A repeat of the same kind from the same device within 60 s
  is dropped; away-and-back inside a minute still records both. The simulator sends them (a
  foreground/background toggle per phone, and "opened" when a phone first gets going); the iOS and
  Android apps don't yet. The flow draws the line dashed while the app is away.
- The Activity tab has **In order** / **By time**. By time, every line shares one time scale with
  gridlines, fits the panel at 1× (zoom up to 16×), drops crowded labels to lower rows, and scrolls all
  lines together.
- The simulator has a **Go (no session)** mode with a watch link and pretend viewers, an app
  foreground/background switch (Join, Go, Share and Stop need the app on screen), no Pause (Start
  tracking until Stop), and **Delete (other) sim data**.
- Watch viewings: the watch page polls only while its tab is visible (and now at once when it comes
  back), so `WatchViewingTracker` treats a run of polls from one browser with no gap over 30 s as one
  viewing. Each viewing is one `watch_link_opened` event; when it goes quiet (swept every 10 s) its
  length is merged into the event's detail as `watchedSeconds`. Polls after the run ended don't count.
  Accurate to about 10 s; a restart mid-viewing loses that one's length. The Activity tab adds the
  lengths to the branch card and, by time, draws a bar under the line per viewing.
- Privacy: this records how long an anonymous viewer kept the page open (no identity, a hash of a
  random browser id). The privacy page's watch-link sentence (`client/src/LegalPage.tsx`) may want a
  line about it; not changed.

Testing on the live server (branch `activity-flow-test-marking`): an admin marks activity as test
afterwards, rather than ignoring an account (sometimes it's real use).

- `activity_events.is_test` (default false). `POST /admin/activity/test` with `userId` and either `runId`
  (the run's events, plus everything that dot did from the run's first event to its last, such as app
  opens, which carry no run) or `from`/`to`; `isTest: false` marks it back. Nothing is deleted.
- `GET /admin/activity` and `/dots` leave test events out unless `includeTest=true`; a dot with only test
  activity drops off the day's list.
- Activity tab: **Hide test activity** (on by default); a **Test: Go run 09:00–09:03** button per run in
  each line's header (**Not test: …** to undo); By time, drag across a line to pick a stretch, then
  **Mark as test** / **Not test**. Shown test stops are faded, dashed and tagged "test".
- Later, maybe: the app's hidden Developer section gets a "Testing" switch that marks activity as test
  as it happens (needs app releases).

Still to check by hand: the "Done when" run on a phone once deployed.
