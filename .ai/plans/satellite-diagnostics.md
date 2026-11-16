# Satellite diagnostics — Plan

*DotWatcher — plan, November 2026. Status: drafted, not started.*

Goal: when the phone goes onto satellite and posts fail, find out afterwards what the app actually
did. The phone keeps a small log of each satellite episode, and sends it to the server once it's
back on a normal connection. The log is shown on the admin replay page over the gap it explains.

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI
on the PR.

---

## Why

Go run `cd9e7610-85af-4343-b50b-0032f61c217c` (runner SK, 2026-11-14, 6:49–8:35am NZDT):

- 325 positions, none with `isUltraConstrained: true`.
- A 19-minute gap, 7:08:15 → 7:27:15, about 700 m covered. Battery 95% on both sides.
- The phone showed the "On satellite" notification just after 7:08. That notification only fires
  when `isUltraConstrained` flips from false to true (`LocationManager.handlePathChange`), so the
  app knew it was on satellite.
- WhatsApp, which is also enabled for One NZ satellite data, didn't work either.

So the app detected satellite and should have posted straight away, then every 90 s, with
backoff retries on failure. Nothing reached the server. Most likely the satellite link had no
working data, but we can't prove it:

- **The server only stores successful posts.** Failed attempts leave no trace.
- **The phone's counters are memory-only.** `satellitePostStats` (shown as "Satellite posts" in
  the Developer section of Settings) resets whenever the app relaunches. After this run they read 0,
  which tells us nothing.

`satellite-post-retry.md` hit the same wall on 2026-09-29 (the SATELIAM walk, an 18.5-minute
silence): "We can't tell from server data how many attempts failed in the silent stretches."

---

## What an episode records

One episode runs from the moment `isUltraConstrained` goes true to the moment it goes false, or
until tracking stops.

| Field | Notes |
| --- | --- |
| `episodeId` | UUID made on the phone. The server uses it to drop duplicates, so a resend is safe. |
| `startedAt`, `endedAt` | `endedAt` is null if tracking stopped or the app died while still on satellite. |
| `endReason` | `backToNormal`, `wentOffline`, `trackingStopped`, `appRelaunched`. |
| `attempts`, `successes`, `retries`, `pathTriggers` | Same meaning as today's `PostStats`, for this episode only. |
| `failures` | Counts by kind: `timedOut`, `notConnected`, `connectionLost`, `cancelled`, `other` (taken from `URLError.code`), plus `rejected` (non-2xx, with its status codes). |
| `firstAttemptAt`, `firstSuccessAt`, `lastSuccessAt` | Shows whether the link carried anything at all, and for how long. |
| `pathFlaps` | How many times the path went satellite → not → satellite within the episode (the alert's 5-minute cooldown hides these today). |
| `offlineDuringEpisode` | Seconds the path was `.unsatisfied` (no network at all) inside the episode. |
| `context` | `casualRunId` or `sessionId`, app version/build, iOS version, device model. |

No positions. The log answers "what did the app try, and what happened?", not "where was the
runner?". That keeps it small and avoids storing a second, unpruned track.

---

## Phone (iOS)

- [ ] **`SatelliteEpisodeLog`**, a small type next to `SatelliteAlerts`. Opens an episode on the
  false → true flip in `handlePathChange` and closes it on true → false, on `stopTracking`, or on
  the next launch if one was left open (`endReason: appRelaunched`).
- [ ] **Saved on every change.** Store open and unsent episodes as JSON in a file under Application
  Support (not `UserDefaults`, which is a poor fit for a growing list). An episode then survives a
  relaunch, or iOS killing the app inside a dead zone, which is when it matters most.
- [ ] **Record from `recordPostAttempt`.** It already gets the outcome. Extend `PostOutcome`'s
  `.transportFailure` to carry the `URLError.Code` and `.rejected` to carry the HTTP status, so
  the failure kind can be counted.
- [ ] **Send when back on a normal connection.** After the first successful normal (not
  satellite) `POST /location`, send every closed, unsent episode to `POST /diagnostics/satellite`.
  Also try on launch. Delete an episode from the file only after a 2xx. Never send while
  `isUltraConstrained`: diagnostics must not use up satellite capacity.
- [ ] **Caps.** At most 20 unsent episodes kept (drop the oldest), and per-episode counts are plain
  integers, so the file stays a few KB.
- [ ] **Debug path.** The Developer section's "Force satellite mode" toggle already runs
  `handlePathChange`, so it opens and closes episodes too. Mark those with `forced: true` so they
  can be told apart on the server.
- [ ] **Developer section.** Next to "Satellite posts", show "Unsent satellite logs: N" and the last
  episode's summary, so it's visible on the phone without the server.

## Server

- [ ] **`POST /diagnostics/satellite`**, bearer-authenticated as the runner (unlike
  `/client-errors`, a satellite episode only happens while tracking, so the user is always signed
  in). Body: an array of episodes. Idempotent on `episodeId`.
- [ ] **Table `satellite_episodes`.** Columns for the fields above. `failures` as JSONB.
  `user_id` FK `ON DELETE CASCADE`, nullable `casual_run_id` and `session_id`. Index on
  `(casual_run_id)` and `(session_id)`.
- [ ] **Retention.** Deleted on the same clock as casual positions (`Casual:RetentionDays`, in
  `CasualMaintenanceService`). Named-session episodes go when their session's data does.
- [ ] **Validation.** Cap the array length (20), clamp counts, reject `endedAt < startedAt`, and
  ignore unknown failure kinds rather than failing the request.
- [ ] **Log to Seq as well.** One information-level event per episode, with the summary as
  properties, so it can be searched without the admin page (same idea as `ClientErrorsController`).
- [ ] **Admin read:** `GET /admin/casual/runs/{runId}/satellite-episodes`, and the same for a named
  session.

## Admin replay page

- [ ] `AdminRunReplayPage` fetches the run's episodes and draws each as a shaded band on the
  timeline from `startedAt` to `endedAt`.
- [ ] Hover or tap shows the summary, e.g. "On satellite 7:08–7:26 · 12 attempts, 0 succeeded ·
  11 timed out, 1 connection lost".
- [ ] Episodes with `forced: true` drawn differently (outline only).

---

## Order

1. iOS log, persistence and sending (useful on its own: the Developer section shows the result).
2. Server endpoint, table, retention, Seq event.
3. Admin replay bands.

Steps 1 and 2 can ship together. Step 3 can follow, as the Seq event covers the gap until then.

## Android

Not in scope. Android has no satellite detection (`NWPath.isUltraConstrained` is iOS-only) and no
equivalent of the satellite alert. Revisit if Android gains satellite support.

## Open questions

- **Named sessions as well as Go runs?** The plan covers both, but only Go runs have a replay page
  today. The session map's replay could get the same bands later.
- **Should the episode also record the phone's battery at start and end?** Cheap, and it would
  show whether satellite costs noticeably more. Leaning yes.
- **Show the runner?** A short note after a run ("You were on satellite for 18 min; nothing got
  through") might help runners understand gaps on their own replay (`go-run-replay.md`). Not in v1.

## Related

- `satellite-post-retry.md`: the retry/backoff and the `PostStats` counters this builds on.
- `satellite-alerts.md`: the "On satellite" notification, fired from the same transition.
- `satelite-connectivity.md`: entitlements, iOS 26.1+ requirement, One NZ.
- `go-run-replay.md`: the replay pages the bands would appear on.
