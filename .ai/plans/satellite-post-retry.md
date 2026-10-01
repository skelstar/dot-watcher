# Satellite post retry — plan (iOS)

*Drafted 2026-09-30, from the SATELIAM walk (invite A14564, runner SK, 2026-09-29).*

## Status (2026-09-30)

Implemented in `LocationManager.swift`, unverified on device (build/test left to CI):
backoff retry (satellite only), single in-flight post, 25s satellite-session timeout,
send-on-path-return, and `normalPostStats`/`satellitePostStats` counters plus a `[post]` log line
per attempt. Counters aren't shown in any UI yet; read them from the log or a debugger for now.

## Background

On satellite (`NWPath.isUltraConstrained`), `LocationManager` posts every 90s instead of 15s
(`ultraConstrainedInterval`). That one timer does two jobs: how often we *want* a position, and how
soon we *try again* after a failure. There is no retry. A failed `POST /location` is dropped
(`post()`'s `catch` only sets `status`) and nothing happens until the next epoch-aligned tick
(`trackingLoop()` → `nextPostAt()`).

What the walk showed (times NZDT):

| Phase | Time | Notes |
|---|---|---|
| Cellular | 3:26–4:02pm | Steady 15s, 221 points |
| Dead zone | 4:02–4:09pm | No posts (cellular gone, satellite not yet up) |
| Satellite | 4:09–4:15pm | 4 posts, ~90–150s apart |
| Silence | 4:15–4:33pm | No posts for 18.5 min, then one satellite post at 4:33:30 |
| Cellular | 4:34–4:56pm | Steady 15s, 89 points |

We can't tell from server data how many attempts failed in the silent stretches, because only
successful posts are stored. It's also unclear which cadence the app was on during the 18.5 min
silence: if the path went fully unsatisfied, `path.isUltraConstrained` probably reported `false`
and the loop dropped back to 15s. That's worth confirming on device (see "Measuring" below).

## Goal

While on satellite, keep the 90s cadence for successful posts, but when a post fails, retry soon
enough to catch short satellite windows, without piling up requests or noticeably increasing
battery use.

## Design

1. **Separate cadence from retry.** Cadence stays as is (15s normal, 90s satellite, epoch-aligned).
   Retries are extra attempts *between* ticks, only after a failure.
2. **Backoff after a failed post:** 5s → 10s → 20s → 40s, never past the next scheduled tick. A
   success, or reaching the next tick, resets the backoff.
3. **Always send a fresh position.** A retry re-runs capture (`latestLocation`, `Date()` as
   timestamp, current `isUltraConstrained`, battery), and never resends the failed payload. This
   keeps to the existing "Not doing: retry queue" decision in `satelite-connectivity.md`:
   DotWatcher shows where the runner is *now*, so stale positions aren't buffered.
   `nextExpectedAt` on a retry is still the next regular boundary (`nextPostAt()`), so the web
   countdown stays correct.
4. **Only one post in flight.** `captureAndPost()` currently starts an unstructured `Task` per
   tick, so a hung satellite request can overlap the next one. Add an in-flight guard: skip a
   scheduled post or retry while one is still running.
5. **Shorter request timeout.** `ultraConstrainedSession` uses the default 60s request timeout. Set
   `timeoutIntervalForRequest` to about 25s so a dead satellite request fails and frees the
   in-flight slot well before the next 90s tick. This session carries every `POST /location`, so
   the value must also be fine on cellular (it is: normal posts finish in well under a second).
6. **Post when the path comes back.** In `pathMonitor.pathUpdateHandler`, when the path moves from
   unsatisfied to satisfied (or into ultra-constrained) while tracking, trigger an immediate
   capture-and-post, subject to the in-flight guard and debounced (e.g. at most once per 5s). This
   costs nothing while there's no signal. It complements retries rather than replacing them, since
   path callbacks can lag (see `refreshConnectivity()`).
7. **Which failures to retry.** Only transport failures (`URLError`: timeout, not connected,
   connection lost, etc.). Don't retry 4xx (401, 403, 426 `updateRequired`) or other server
   rejections; the next tick handles those as it does today.
8. **Scope: satellite only, to start.** Apply retries when the failed post was captured with
   `isUltraConstrained == true`, or when the path is currently ultra-constrained. The
   path-comes-back trigger (step 6) applies in all modes, since it's free. Whether to extend
   backoff retries to the cellular dead-zone case (like 4:02–4:09pm) is an open question.

## Where the changes go

All in `ios/DotWatcher/DotWatcher/LocationManager.swift`:

- `ultraConstrainedSession`: set `timeoutIntervalForRequest`.
- `captureAndPost()` / `post()`: in-flight flag; `post()` reports success or retryable failure.
- New retry scheduling: a single cancellable retry `Task` with the backoff state, cancelled on
  `stop()`, on session change, and when a scheduled tick fires.
- `pathMonitor.pathUpdateHandler`: detect the unsatisfied → satisfied transition while tracking and
  trigger a post.
- Respect existing gates: battery pause (`pausedForBattery`), tracking expiry
  (`trackingExpiresAt`), consent, and "Waiting for GPS".

No server or web client changes. The server already accepts off-boundary posts and stores them by
timestamp.

## Battery

We don't expect much extra draw, but it hasn't been measured:

- With no path at all, a request fails locally almost immediately without transmitting, so
  retries in a dead zone cost close to nothing.
- Real cost only comes when a path exists and data goes out over satellite, and then the post
  usually succeeds, which is what we want.
- GPS is the dominant cost (see the `criticalBatteryThreshold` comment), not post frequency.
- Baseline: iOS attributed ~5% battery to DotWatcher over the 90-minute SATELIAM walk (with a
  hotspot running, which accounts for most of the phone's 70% → 50% drop).

## Measuring

- Add in-memory counters, visible on the hidden debug surface next to
  `debugForceUltraConstrained`: attempts, successes, transport failures, retries, and path-change
  triggers, split by satellite vs normal.
- Log each attempt with its outcome and whether it was a tick, a retry, or a path-change trigger,
  so one walk shows which cadence was really in use during silent stretches.
- Repeat the SATELIAM route, ideally without the hotspot, and compare: satellite posts landed,
  longest gap, and iOS's battery attribution against the ~5% baseline.

## Testing

- `debugForceUltraConstrained` exercises the 90s cadence and retry scheduling, but not a real
  satellite link. Airplane mode toggling covers unsatisfied → satisfied transitions and fast
  local failures.
- A real test needs iPhone 13+, iOS 26.1+, and One NZ Satellite Data (see "Supported devices for
  satellite" in `satelite-connectivity.md`).
- Build/test verification is left to CI per `AGENTS.md`.

## Non-goals

- No queue or buffering of failed positions (see step 3).
- No server changes, no batch endpoint.
- No Android changes in this pass (Android is building its own offline queue; see
  `offline-location-queue.md`).
- No UI change beyond the debug counters.

## Open questions

- **Parked idea: don't wait for the 200 on satellite.** Assume a send that went out landed, and skip
  retrying on a timeout. Not doing it now. The app uses the response for the roster/positions merge
  and `lastSent`, so it would need another source for those. To see whether it would help, look in
  real-walk server data for a regular on-grid row followed by an off-grid row within ~5–75s: that's
  a send that landed but whose reply was lost, so the phone retried unnecessarily.

- Backoff numbers (5/10/20/40s) and timeout (~25s) are starting guesses. Tune them after a real walk.
- Extend backoff retries to the non-satellite dead-zone case, or keep it satellite-only?
- Should a successful retry also refresh `runnerPositions`/participants like a normal post? It
  would, since it goes through the same `post()`, which seems fine.
