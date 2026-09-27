# Report battery level on `POST /location` — Plan

*DotWatcher — plan, September 2026*

## Context

Came up while investigating a run where 4 hours of tracking used 18% battery (see the
GPS-power/`pausesLocationUpdatesAutomatically` discussion — decided not to change GPS behavior).
Separately from that: session members currently have no way to know when someone else's phone is
about to die. A runner whose battery dies goes quiet exactly like a genuine dropout or a real
"missing location" gap (`findRunnersWithGap` in `useSessionTimelineLogic.ts`) — nothing distinguishes
"phone died" from "lost signal" from "stopped on purpose" (see the related, still-open "signal a
deliberate stop" discussion). Surfacing battery level ahead of time gives other members a warning
before that silence happens, not just an explanation after the fact.

This is the same shape of feature as `isUltraConstrained`/`nextExpectedAt`: a value the phone
already has, self-reported on the existing `POST /location`, no new endpoint.

## Goal

- iOS reports its current battery level on every `POST /location`.
- The server persists it (from day one — see "Learning from precedent" below).
- Any session member viewing the web map sees a distinct icon on a runner's dot once their battery
  drops below one of two thresholds — a "low" icon and a separate "critical" icon, not just a color
  change (per this repo's existing "never color alone" rule — see `Legend.tsx`'s GPS-signal-loss "!"
  badge and `satelite-connectivity.md`'s badge treatment).

## Learning from precedent

`isUltraConstrained` and `nextExpectedAt` both shipped live-only first, then needed a whole separate
follow-up pass (`persist-isUltraConstrained.md`) to make them durable, because `location_updates`
had no columns for them yet. This plan folds that step in up front — the schema/write/read changes
below happen in the same pass as the live wiring, not after.

## Design decisions

**Wire format:** `batteryLevel: number | null` (0–100 integer percentage), matching the
`isUltraConstrained`/`nextExpectedAt` camelCase convention and the same variable name
`ContentView.swift` already uses locally. Percentage rather than iOS's native `0.0...1.0` float —
easier to read in logs/NDJSON, and avoids repeating a `* 100` conversion at every consumer.

**Null means "unknown," never "dead."** `UIDevice.current.batteryLevel` returns `-1.0` when
`isBatteryMonitoringEnabled` is off or the value isn't available (some simulators always report
this). Convert `-1.0` → `nil` on the wire, not `0`. A missing value must never render as a critical
battery — same "unknown ≠ bad" rule this codebase already applies to `isUltraConstrained: false`
defaulting for older clients.

**Two thresholds, two icons.** Decided:
- **Low** — ≤30%.
- **Critical** — ≤10%.

Mutually exclusive per runner at any instant (like the existing missing/sleeping precedence in
`Legend.tsx:88-90` — "a runner is exactly one of missing/sleeping/normal"), so the derived value is
naturally `'low' | 'critical' | null` per runner, not two independent booleans.

**Icon reuse, not new visual language:**
- *Critical* reuses the existing red "!" `inlineWarningBadge` convention already used for GPS
  signal-loss in `Legend.tsx` — zero new color/shape vocabulary for the most urgent state.
- *Low* gets its own stroked SVG battery-outline icon, sourced the same way `SatelliteDishIcon` was
  (a Lucide, ISC-licensed glyph), rendered as an independent overlay in the same slot the satellite
  dish uses — can appear alongside the satellite icon, a countdown, or a missing/sleeping label,
  since all of those are separate facts from "battery is low."

## Proposed changes

### 1. iOS — capture and send

- `LocationManager` needs its own `UIDevice.current.isBatteryMonitoringEnabled = true` rather than
  relying on `ContentView.onAppear` having already set it — `LocationManager` shouldn't depend on
  view lifecycle to work correctly.
- In `captureAndPost()`, capture `UIDevice.current.batteryLevel` synchronously before the `Task`
  fires — same reasoning already documented there for `isUltraConstrained`: the value that should
  accompany this post is the one true at the moment sending was decided, not whatever it drifts to
  by the time the request lands.
- Convert `-1.0` → `nil`, else `Int((level * 100).rounded())`, and add `"batteryLevel"` to the POST
  body in `post(...)` alongside `isUltraConstrained`.

### 2. Server model/validation

- `LocationUpdate.cs` / `RunnerPosition.cs`: add `int? BatteryLevel = null`, doc comment mirroring
  the existing `IsUltraConstrained`/`NextExpectedAt` style.
- `LocationUpdateValidation.cs`: reject out-of-range values — `BatteryLevel is { } b && (b < 0 || b
  > 100)`. Optional field; absent is valid (older clients, Android, monitoring disabled).

### 3. Schema — `SessionStore.Initialize()`

```sql
ALTER TABLE location_updates
    ADD COLUMN IF NOT EXISTS battery_level SMALLINT;
```

Nullable, no default — matches `next_expected_at`'s treatment (absent means "not reported," not a
falsy default like `is_ultra_constrained`'s `FALSE`).

### 4. Write/read paths (same six touch points `persist-isUltraConstrained.md` identified)

- `AddPosition` — extend the `INSERT`, bind with the same nullable-parameter pattern as `heading`.
- `LoadLatestPositionsByRunner`, `LoadUpdatesByTimestamp` (→ `GetRecordingAsNdjson`),
  `GetRecordingWindowAsNdjson` (the scrubber's real read path) — add `battery_level` to each
  `SELECT`, thread into the constructed `RunnerPosition`/`LocationUpdate`.
- `SaveRecording`/`ParseRecordingLine` — round-trip on NDJSON re-import.

### 5. Client — `types.ts` / `useSessionTimelineLogic.ts`

- `RunnerPosition` interface: add `batteryLevel?: number | null`.
- `normalizeUpdate`: `batteryLevel: obj.batteryLevel ?? obj.BatteryLevel ?? null`.
- New `findRunnersWithLowBattery(byRunner, cutoffMs): Map<string, 'low' | 'critical'>` — same
  "latest position at-or-before cutoff" loop shape as `findUltraConstrainedRunners`.

### 6. `Legend.tsx`

- New prop `runnerBatteryStatus?: Map<string, 'low' | 'critical'>`.
- Render as an independent overlay (own slot, like the satellite dish), using the critical/low
  icon treatment described above. Can combine with the satellite icon, a countdown, or a
  missing/sleeping label — battery state is orthogonal to all of them.

### 7. Tests

- `LocationsApiTests`: `BatteryLevel` round-trip on `GET /locations/{id}` (present, absent,
  boundary values 0/100, rejected out-of-range).
- `SessionsApiTests`: extend the existing recording/scrubber/cross-pod/re-import tests the same way
  `persist-isUltraConstrained.md` did for its two fields.
- Client: unit tests for `findRunnersWithLowBattery` mirroring `findUltraConstrainedRunners`'s test
  shape (below both thresholds, between them, unknown/null, no position yet).

### 8. Docs

- `docs/database-schema.html`: add `battery_level` to the `location_updates` table card.

## Backward compatibility

Purely additive, matching every prior field added this way:
- Old iOS builds, and Android (which doesn't yet send `nextExpectedAt`/`isUltraConstrained` either,
  per `android/app/src/main/kotlin/.../network/DotWatcherApi.kt`) simply omit the field — server
  stores `NULL`, client shows no battery badge, exactly like a runner whose phone doesn't support
  it. No badge is a neutral state, never inferred as "critical."
- Existing Postgres rows backfill to `NULL` on the new nullable column — correct, since none of
  them could have reported this before the column existed.
- Wire format is additive only — no renamed/removed fields anywhere in this change.

## Non-goals

- **Android.** Its `LocationUpdate`/`RunnerPosition` models don't send `nextExpectedAt` or
  `isUltraConstrained` yet either — battery reporting follows once those catch up, not bundled here.
- **Native iOS map (`NativeMapView`/`LiveMapSheet`) showing *other* runners' battery.** iOS's own
  `RunnerPositionResponse` doesn't even decode `nextExpectedAt`/`isUltraConstrained` today (only
  `runnerName`/`latitude`/`longitude`/`heading`/`timestamp`) — extending it for any of these fields,
  battery included, is a separate, larger effort. This pass only adds the web `Legend.tsx` badge,
  matching where satellite/countdown currently render too.
- **A self-device low-battery alert.** The existing on-device numeric battery indicator
  (`ContentView.swift`) already covers a runner seeing their own level. A dedicated "your battery is
  low" banner is a small, separable follow-up, not bundled here.
- **Historical battery graph/analytics.** Persisted for scrubback parity like every other field on
  this record, not for trend charting.
- **Push notifications to other members on someone's critical battery.** Badge only.

## Open questions

- Exact icon glyph for "low" (a Lucide battery-outline variant, TBD which one reads best at
  `Legend.tsx`'s small pill size).
- Whether "critical" reusing the plain red "!" is distinctive enough next to GPS signal-loss (which
  uses the same badge) — consider a battery-specific critical icon instead if the two get confused
  in practice.

## Effort estimate

Small–medium — same shape as `persist-isUltraConstrained.md`'s checklist (one column, one write
path, three read paths, one re-import path), plus one new client derive function and one Legend
icon. Slightly larger than that precedent only because this ships live+persisted in one pass instead
of two.

## Implementation status (2026-09-28)

Shipped in full per the design above, on branch `add-battery-level-reporting`:

- iOS captures `UIDevice.current.batteryLevel` in `captureAndPost()` (own
  `isBatteryMonitoringEnabled = true` set in `LocationManager.init()`, not dependent on
  `ContentView`), converts `-1.0` → `nil`, sends `batteryLevel` (0-100) on `POST /location`.
- Server: `LocationUpdate`/`RunnerPosition`/`ValidatedLocationUpdate` all carry `BatteryLevel`;
  `LocationUpdateValidation` rejects out-of-range values. `battery_level SMALLINT` column added to
  `location_updates`, written by `AddPosition`, read by all three Postgres read paths
  (`LoadLatestPositionsByRunner`, `LoadUpdatesByTimestamp`, `GetRecordingWindowAsNdjson`) and
  round-tripped by `SaveRecording`/`ParseRecordingLine` — persisted from day one, not retrofitted.
- Client: `RunnerPosition.batteryLevel` in `types.ts`; `findRunnersWithLowBattery` (30%/10%
  thresholds) in `useSessionTimelineLogic.ts`; wired through `useSessionTimeline.ts` →
  `App.tsx` → `Legend.tsx`, rendered as an independent overlay icon (hand-drawn battery glyph —
  a charge bar for "low", an exclamation mark for "critical", so the two differ in shape, not
  only color).
- Tests: `LocationsApiTests` (validation + round-trip + null-when-absent) and `SessionsApiTests`
  (cross-pod, full recording, windowed recording, re-import — mirroring every
  `persist-isUltraConstrained.md` coverage point) on the server; `findRunnersWithLowBattery` unit
  tests on the client.
- `docs/database-schema.html` updated with the new column.

Not done (per Non-goals above, unchanged): Android, native iOS map display of other runners'
battery, historical battery graphing, push notifications. (The self-device low-battery alert
listed as a non-goal above was later built — see the update directly below.)

Not run locally: `dotnet build`/`test`, `npm run test`/`build` — per repo convention, left to CI on
the PR.

## Update (2026-09-28): pause automatic tracking at critical battery

Two follow-up ideas were considered and rejected/accepted:

**Rejected: slow the post cadence at low battery (e.g. every 5 minutes).** Doesn't help. GPS —
continuous `CLLocationManager` tracking — is the dominant battery cost, already established in this
codebase's own history (`cabb1a8`: *"GPS is the dominant battery drain (~30-100mW continuous), the
marginal cost of one small HTTP POST every 15s is negligible by comparison"*) and in
`satelite-connectivity.md` (*"GPS acquisition cost is unaffected by which network path the upload
eventually takes"*). Slowing POSTs alone doesn't touch GPS, so it barely moves the real cost, while
directly hurting position freshness at the exact moment (phone about to die) that freshness matters
most.

**Accepted: stop tracking entirely at critical battery, runner-controlled.** This targets the actual
dominant cost by calling `clManager.stopUpdatingLocation()`, not just skipping POSTs. Design:

- **Trigger:** `batteryLevel <= criticalBatteryThreshold` (10, matching the client's
  `CRITICAL_BATTERY_THRESHOLD`) and not currently charging (`UIDevice.batteryState`) and not already
  overridden this tracking session.
- **On trigger:** stop `CLLocationManager` updates, skip the automatic post, show a persistent
  banner (`ContentView.runnerRow`) — deliberately not silent, matching this app's existing rule that
  a state change affecting what other people see must be surfaced, not hidden (same reasoning as the
  satellite status badge).
- **Two explicit choices offered, no default action taken on the runner's behalf:**
  - **Send location now** — one-shot `CLLocationManager.requestLocation()` (reusing
    `oneShotLocationContinuation`, scaffolding that already existed in `LocationManager.swift` but
    had no caller) + a single POST. Does not resume automatic tracking.
  - **Turn on automatic updates** — explicit override, resumes `startUpdatingLocation()` and the
    normal cadence for the rest of this tracking session (`batteryOverrideAcknowledged`, reset on
    `start()`).
- **Auto-resume on charging**, even without the override button: if the runner plugs in, the reason
  for pausing no longer applies, so tracking resumes automatically. This is an addition beyond what
  was asked for, included because it has no downside (charging is an unambiguous, immediate signal)
  and never overrides an explicit choice either way.
- **No server/protocol changes.** Other session members don't need a new signal: once posts stop,
  the existing gap/missing detection (`findRunnersWithGap`) kicks in on schedule, and the runner's
  last known position still carries its persisted `batteryLevel`, so the critical-battery badge is
  still showing on that last dot. "Last seen at critical battery, then went quiet" is already the
  correct, honest story with zero new server work.

### Non-goal (unchanged from this update)

Detecting "phone unlocked" directly — iOS has no such signal for a backgrounded app. Foregrounding
the app (which requires unlocking) is the closest equivalent and isn't what this design uses either;
it's fully explicit (a button tap), not foreground-triggered, per the decision to default to
*stopped* rather than *silently resumed on open*.
