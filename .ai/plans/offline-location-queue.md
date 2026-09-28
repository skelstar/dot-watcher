# Offline location queue — scoping doc (Android)

## Background

This doc previously existed for iOS, scoping a gap seen on a real run: a genuine dead zone
(no cellular, satellite unavailable) meant `POST /location` calls just failed silently — no
retry, no buffer, that stretch of the run is missing from the map entirely. The original doc
was lost from the repo (never committed) before iOS implemented it; iOS still has no queue as
of 2026-09-27 (`LocationManager.swift`'s tracking loop has no retry/backoff wiring — see its
`trackingLoop()`/`captureAndPost()` comments). This version recreates the design for Android,
which is building it first.

**The general case, not satellite-specific:** any time the phone has no path to the server —
dead zone, backgrounded past whatever budget the OS allows, server hiccup — a naive
capture-and-post loop drops that position instead of holding onto it.

## Goal

When a `POST /location` fails (network error, timeout, non-2xx), buffer the location locally
instead of dropping it. Flush the buffer once connectivity returns, oldest first, without
disrupting live posting.

## Design decision: persisted queue

A queue held only in memory doesn't survive the process being killed — and a foreground
service being killed mid-dead-zone is exactly the scenario this feature exists for (a long
run through a dead zone is plausible, and Android can and does reclaim foreground services
under memory pressure despite the "foreground" leniency). So the queue must be persisted to
disk (a JSON file in the app's private files directory), not just held in a list.

## Key design decisions

1. **Cap the queue.** Unbounded growth during a long outage (flight mode left on, a very long
   dead zone) isn't acceptable. Cap at ~200 entries (≈50 minutes at the current 15s cadence —
   see `LocationTrackingService.kt`'s `POST_INTERVAL_SECONDS`), dropping the oldest entry when
   a new capture would exceed the cap, so the queue always holds the most *recent* history
   rather than the earliest history of an outage.
2. **Flush strategy.** Draining one at a time on the existing 15s cadence would take a long
   time to catch up after a real gap (60 queued points at 15s apart = 15 minutes just to
   flush). Flush the whole backlog back-to-back (no inter-post delay) as soon as a live post
   succeeds, then resume normal cadence — the live post having just succeeded is itself the
   signal that connectivity is back.
3. **Batch vs. repeated single POSTs.** The server's `POST /location`
   (`server/Controllers/LocationsController.cs`) takes exactly one `LocationUpdate` per call.
   Flushing a backlog of 60 points as 60 sequential requests works with zero server changes,
   chattier than a batch endpoint but simpler and ships without touching the server. Start
   here; revisit only if flush time in practice turns out to matter.
4. **Ordering/timestamps.** Each queued point carries its own captured `timestamp`
   (`server/Models/LocationUpdate.cs`'s `Timestamp` field), so out-of-order *arrival* relative
   to wall-clock POST time is fine as long as the server trusts the payload's timestamp over
   receipt time, which it does (`SessionStore.AddPosition` stores whatever `ValidatedUpdate`
   it's given, keyed by capture time, not append/receipt order).
5. **What triggers "connectivity is back."** Don't poll connectivity separately — just attempt
   the live post on the normal cadence as always; a successful response is the only signal
   needed to trigger a flush of anything queued.

## Where this lives in the Android codebase

- `location/LocationUpdateQueue.kt` (new): persisted, capped queue — `enqueue(Capture)`,
  `drain(): List<QueuedCapture>`, backed by a JSON file via kotlinx.serialization in the app's
  `filesDir`.
- `location/LocationTrackingService.kt` (existing): on a failed `postLocation` call, enqueue
  instead of just recording the error; on a *successful* post, flush the queue first (oldest
  first) before returning to the normal delay.
- Each queued entry needs the same fields `DotWatcherRepository.postLocation` takes
  (`sessionId`, `latitude`, `longitude`, `heading`, `timestamp`) — the session ID matters
  because a queued point should only ever be flushed against the session it was captured for
  (if the runner left and rejoined a different session while offline, dubious but possible,
  don't cross-post).

## Non-goals

- No server-side changes (no batch endpoint) — see decision 3 above.
- No cross-session merging or dedup logic beyond capping and ordering.
- No UI to inspect/clear the queue manually in this pass — it should just work silently; revisit
  if real usage shows a need to expose queue depth to the runner (e.g. "12 positions queued,
  will send when back online").
