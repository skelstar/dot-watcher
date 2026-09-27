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

**Two thresholds, two icons.** Proposed defaults, open to adjustment:
- **Low** — ≤20% (matches iOS's own Low Power Mode suggestion point, a familiar mental model).
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

- Confirm the 20%/10% threshold defaults, or make them configurable.
- Exact icon glyphs for "low" (a Lucide battery-outline variant, TBD which one reads best at
  `Legend.tsx`'s small pill size).
- Whether "critical" reusing the plain red "!" is distinctive enough next to GPS signal-loss (which
  uses the same badge) — consider a battery-specific critical icon instead if the two get confused
  in practice.

## Effort estimate

Small–medium — same shape as `persist-isUltraConstrained.md`'s checklist (one column, one write
path, three read paths, one re-import path), plus one new client derive function and one Legend
icon. Slightly larger than that precedent only because this ships live+persisted in one pass instead
of two.
