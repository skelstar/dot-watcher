# Go run replay for everyone — Plan

*DotWatcher — plan, November 2026. Status: drafted, not started.*

Goal: someone who has been out on a Go (casual) run can watch it back afterwards, with their crew's dots where the
rules allow. They can also **export** a run so it can be watched later, by themselves or by someone they send it to,
after the 7-day retention has deleted the original. Related: [`casual-tracking.md`](casual-tracking.md) (the Replay
design section and the open question about crew replaying each other's runs).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR.

---

## Where things stand

- **Admin replay exists** (PRs #342, #344): `GET /admin/casual/runs`, `GET /admin/casual/runs/{runId}/recording(/meta)`,
  and a web page at `/admin/go/{runId}` using the session map player. It shows the runner only, with ← / → stepping
  one position at a time.
- **The data is already there.** Every casual position stores `casual_run_id`; `casual_runs` has exact start and end
  times. `SessionStore.CasualReplay.cs` holds the run-scoped queries this plan can reuse.
- **Retention is 7 days** (`Casual:RetentionDays`, `CasualMaintenanceService`). After that, positions and finished runs
  are deleted.
- **The player is reusable.** `useSessionTimeline` takes a `TimelineSource` (recording base, optional live URL,
  headers), so a new replay page is mostly wiring.

---

## Principles

1. **A live replay needs sign-in.** Whether you may see a crew member's dot depends on who you are (crew, blocks,
   both people's "My crew" setting). The server can only decide that for a signed-in account, and listing "my runs"
   needs an owner anyway. So the replay of a run as it is stored (with crew) is for the signed-in runner only.
2. **Strangers never appear in any replay or export.** Grey nearby dots are anonymous by design; storing or replaying
   them would let someone build a track from them.
3. **An export is a frozen copy, and it is the runner only.** It is the way to watch without signing in, and it
   outlives retention. Because whoever has it may not be crew, it never contains crew positions.
4. **When unsure, hide.** Visibility settings are only stored as they are now, not as they were during the run (see
   below), so a crew dot is shown only when every rule allows it now.

---

## Who sees what

| Viewer | How | Runner | Crew who were out | Strangers |
| --- | --- | --- | --- | --- |
| The runner, signed in | Replay of their own run | Yes | Yes, if the crew rule below passes | Never |
| A crew member, signed in | Replaying someone else's run | Not in v1 (open question in `casual-tracking.md`) | — | Never |
| Anyone with an export link | Export viewer, no sign-in | Yes (frozen copy) | Never | Never |
| Admin | `/admin/go/{runId}` | Yes | No (unchanged) | Never |

**Crew rule.** A crew member's positions from the run's time window are included only if all of these are true at the
moment of the replay:

- they are still in the runner's crew;
- neither has blocked the other;
- they have "My crew" (`casual_visible_to_crew`) on;
- the runner has "My crew" on (visibility is reciprocal: if they couldn't see you, you don't see them).

Only their positions that belong to one of *their* Go runs count (`casual_run_id IS NOT NULL`), with
`timestamp` inside the runner's run. Labelled by the runner's nickname for them, or their display name, as in
`GET /casual/live`.

**Why "now" and not "then".** Nothing records the settings in force during the run. Being strict now can only hide
someone who was visible at the time; it can never reveal someone who was hidden, as long as they haven't since turned
"My crew" back on. That last gap is accepted for v1. Snapshotting the settings per run (a few columns on
`casual_runs` set at start and on change) would close it, and is listed as an option below.

---

## Export

An export lets a run be watched after retention has deleted it, by the runner or anyone they send it to.

- **What is copied:** the runner's positions for that run (same fields as the recording NDJSON), the run's start/end,
  and the runner's label (initials, as a watch link shows them). Never crew, never strangers, no user id, no username.
- **Storage:** a new `casual_run_exports` table (`id`, `owner_user_id` FK `ON DELETE CASCADE`, `created_at`, `label`,
  `positions` as compressed NDJSON or JSONB, `share_token_hash` nullable). Not touched by the casual purge, so it
  outlives the run. Deleted with the account.
- **Watching it:** `/replay/{token}` on the web, no sign-in, using the same player with a `TimelineSource` whose
  recording base is the export (no live URL). Like a watch link, only a hash of the token is stored, and it can be
  revoked.
- **Download:** the same export as a GPX file (for Strava and the like) and as NDJSON. GPX is the format most people
  will expect.
- **Start-area privacy:** the run's start is usually someone's home. Offer to trim the first and last N metres
  (the casual `HideStartArea` idea) when exporting; default on for share links. *Decision below.*

---

## Endpoints

| Method | Path | Auth | What it does |
| --- | --- | --- | --- |
| `GET` | `/me/casual/runs` | User | The caller's runs, newest first (reuses `GetCasualRunsForAdmin`, filtered by owner) |
| `GET` | `/me/casual/runs/{runId}/recording/meta` | User, owner only | Range of the run, as the admin endpoint |
| `GET` | `/me/casual/runs/{runId}/recording?since=&until=` | User, owner only | The runner's rows plus crew rows that pass the crew rule; `404` for someone else's run (not `403`, so run ids can't be probed) |
| `POST` | `/me/casual/runs/{runId}/export` | User, owner only | Creates an export; body chooses share link on/off and start-area trim. Returns its id and, if shared, `/replay/{token}` |
| `GET` | `/me/casual/exports` | User | The caller's exports |
| `DELETE` | `/me/casual/exports/{id}` | User, owner only | Deletes an export and its link |
| `GET` | `/replay/{token}` (+ `/recording`, `/recording/meta`, `.gpx`) | Token | The shared export; `404` once revoked |

All behind the `casual` feature, like the rest of casual.

---

## Clients

- **Web, signed in:** `/go/{runId}` replay page (the admin page minus the admin token, using the viewer's user token).
  The web already has an optional sign-in (`SignInPanel`); if not signed in, it prompts for it.
- **Getting from the app to the web replay:** the app opens `/go/{runId}` with a short-lived token in the URL
  fragment, as route upload does (`POST /sessions/{id}/route-upload-token`). Avoids asking for a second sign-in on
  the web.
- **Finding runs:** the apps have no run history today. Smallest version: a "Watch your run back" action after
  pressing stop, plus a simple "Recent runs" list under Go.
- **Export UI:** from the replay page and the recent runs list: "Save & share" (link) and "Download GPX".
- **Native replay screen (iOS/Android):** out of scope here; the web page comes first.

---

## Decisions for you

1. **Crew in v1?** Ship runner-only first (like admin) and add crew next, or include crew under the rule above from the
   start? *Recommended: runner-only first; the crew rule is the riskiest part and is easier to review on its own.*
2. **Snapshot visibility settings per run?** Closes the "turned My crew back on later" gap; adds columns and write
   paths. *Recommended: not for v1.*
3. **Retention for the original run.** Keep 7 days, with export as the way to keep a run? *Recommended: yes; exports
   are the opt-in keep.*
4. **Export expiry.** Do exports last forever (until deleted), or expire (e.g. 1 year)?
5. **Start-area trim on exports.** Default on for share links, off for the runner's own GPX download? And how far
   (300 m like the casual start fuzz)?
6. **Where people find runs in the apps.** "Watch it back" after stop only, or a recent runs list too?

---

## Tasks

### Server
- [ ] `GET /me/casual/runs`, owner-filtered variant of `GetCasualRunsForAdmin`.
- [ ] `GET /me/casual/runs/{runId}/recording(/meta)`, runner only (owner check, `404` otherwise).
- [ ] Crew rows in the recording under the crew rule (if decision 1 says so), with labels.
- [ ] Short-lived web replay token for app → web handoff.
- [ ] `casual_run_exports` table (additive, `ON DELETE CASCADE`), create/list/delete endpoints.
- [ ] `/replay/{token}` recording, meta and `.gpx`; start-area trim.
- [ ] Tests: owner-only access, crew rule (each condition on its own), strangers never included, export contains no
      crew/ids, revoked link `404`, export survives the casual purge.

### Web
- [ ] `/go/{runId}` signed-in replay page (reuse `AdminRunReplayPage` pieces).
- [ ] `/replay/{token}` export viewer.
- [ ] Save & share / Download GPX actions.

### Apps
- [ ] "Watch it back" after stop (opens the web replay with a handoff token).
- [ ] Recent runs list (if decision 6 says so).

---

## Risks

| Risk | Mitigation |
| --- | --- |
| A crew member's dot shown in a replay when they hid it at the time | Crew rule checks current settings; per-run snapshot (decision 2) if this matters |
| Export link forwarded beyond who the runner intended | Runner only, no crew; revocable; start-area trim on by default |
| Run ids guessed to read someone else's run | Owner check; `404` not `403`; ids are GUIDs |
| Exports grow without bound | Expiry (decision 4) and a per-account cap |
| Old app builds | All new endpoints and tables; nothing existing changes shape |
