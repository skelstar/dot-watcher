# Adaptive Send Intervals — `nextExpectedAt` Design

*DotWatcher — discussion summary, August 2026*

## The problem

DotWatcher runners no longer all post positions on the same fixed interval. Normal cellular posting uses one interval (existing clock-aligned strategy); satellite-connected runners post every 90s instead of the usual 30/60s, because satellite bursts are more expensive.

Once the interval isn't fixed and shared, any "stale after N seconds" staleness detection breaks: a single threshold either false-flags satellite runners constantly or is too lax to catch a real dropout for everyone else. There's no way to tell "on-schedule but slow" apart from "actually stuck/offline."

## Proposed solution

Add a field to the `POST /location` payload where the client tells the server when it expects to post next — a self-reported heartbeat / soft lease pattern.

- **Field:** `nextExpectedAt` — an absolute ISO 8601 timestamp, not a duration/interval. Phones are already NTP-aligned to ~50–200ms via the existing clock-aligned posting design, so an absolute value avoids "interval from when?" ambiguity and stays consistent with how the existing `timestamp` field (GPS capture time) already works.
- **Keep it distinct from `timestamp`.** `timestamp` = when this position was captured. `nextExpectedAt` = when the *next* post is expected. Document clearly so they don't get conflated.
- **Generic, not satellite-specific.** Don't name or gate it as satellite-only — treat it as "whatever the client's current cadence is." Keeps satellite as transparent plumbing (existing design principle) and leaves room for other adaptive-interval reasons later (e.g. low battery) without new special cases.

## Server-side handling

- Store `nextExpectedAt` alongside each runner's latest position.
- Compute the "overdue" decision **server-side**, not per-client — have `GET /locations/{sessionCode}` return a derived status (e.g. `active` / `overdue` / `paused`) so the web client and the planned native MapKit view both consume one source of truth instead of each reimplementing staleness math with their own clocks.
- Add a **grace buffer server-side** (not client-side) before flagging overdue — client reports honest intent, server decides how much jitter/slack to tolerate. Should be a shared constant so client "due now" state and server "not yet overdue" state agree.
- Validate like other payload fields: reject values in the past relative to `timestamp`, or absurdly far in the future.
- Optional stretch goal (agreed as valuable, not required for v1): persist `nextExpectedAt` on the recording/history side too, so post-run analysis can show exactly where/when a runner's cadence degraded during a race.

## Client-side countdown timer

Because `nextExpectedAt` is absolute, a countdown UI is pure local math — `remaining = nextExpectedAt - now()` — with **no extra network requests**. Each fresh poll response overwrites the stored value and naturally resyncs the countdown, correcting for client clock drift.

- **iOS:** `TimelineView(.periodic(from: .now, by: 1))` — re-renders every second without manual `Timer` lifecycle management. Compute the displayed value inside the view body from the stored `nextExpectedAt`, not a separately-ticking counter.
- **Web:** `setInterval` at 1s, computed against `Date.now()` per runner, resynced naturally on the existing 10–15s poll cycle.

### Three visual states (not just counting down → broken)

1. **Counting down** — "next update in 42s"
2. **Due now** — countdown hits zero, brief grace-window state ("due any moment"), grace length shared with server-side buffer
3. **Overdue** — grace elapses with no new post → existing overdue/stale treatment (label + color, not color alone, per existing design principle)

- Only show the live countdown for actively-reporting runners; paused runners keep the existing "Paused" label.
- UI consideration: a live countdown on every dot on a busy map may be noisy — consider reserving the numeric countdown for a selected/focused runner or a list view, using simple active/due/overdue dot states on the map itself.

## Backward compatibility (three separate concerns)

1. **Old iOS clients not sending `nextExpectedAt` on POST.** Field is optional; server falls back to its current fixed-interval heuristic for those runners. A session can have a mix of updated/non-updated runners simultaneously — no forced update requirement.
2. **Old clients reading the GET response after the field is added.** Must be purely additive — new field appended, nothing renamed/removed. Plain JS ignores unknown keys automatically. A future `Codable`-based native client should declare it as optional (`Date?`) so missing/unknown keys don't throw.
3. **Derived status field on the server response** (if added) — same additive rule. Old clients keep their own staleness logic; new clients can switch to trusting server-derived status instead of reimplementing countdown math.

## Open items

- Exact grace-buffer duration (shared constant between client "due" state and server "overdue" threshold) — not yet decided.
- Whether/when to introduce the derived `active`/`overdue`/`paused` status field on `GET /locations/{sessionCode}`, versus leaving staleness computation to clients for now.
- UI placement decision: per-dot countdown vs. reserved for focused/list view only.
- Recording-side persistence of `nextExpectedAt` — agreed as a good stretch goal, not scoped yet.
  `isUltraConstrained` (added 2026-08-11, see satelite-connectivity.md) shares this exact
  limitation and for the same reason — neither field has a column in `location_updates`, so both
  are live-only: present for anything a viewer polled in real time, always absent/default for
  anything read back via the recording endpoint. Persisting either is the same shape of schema
  change; worth doing together if/when this gets picked up.