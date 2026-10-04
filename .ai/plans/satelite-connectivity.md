# Satellite Connectivity — Design Notes

*DotWatcher — discussion summary, August 2026*

## Context

Explored whether DotWatcher could use satellite connectivity as a fallback for runners out of cell coverage, specifically via One NZ's Starlink-based Direct-to-Cell developer toolkit.

## Two satellite systems — don't conflate these

| | Apple's own satellite stack | Carrier Direct-to-Cell (what applies here) |
|---|---|---|
| Backing constellation | Globalstar | Starlink |
| Examples | Emergency SOS, Find My, Messages via Satellite | One NZ Satellite Data, T-Mobile T-Satellite |
| Third-party app access | Closed; broader API reportedly still upcoming | **Open now** via standard networking APIs |
| How it connects | Proprietary Apple protocol | Satellite acts as an ordinary cell tower; phone's existing LTE radio connects to it like any tower, using standard 3GPP protocols |

The One NZ toolkit is the second kind. It rides the phone's normal cellular baseband — no custom protocol, no extra hardware, no BLE accessory needed.

## Key technical facts

- **Entitlement:** `com.apple.developer.networking.carrier-constrained.app-optimized`
- **Detection (reactive only):** `NWPath.isUltraConstrained` via `NWPathMonitor`. There is no way to pre-check whether a device/carrier/plan is satellite-eligible — Apple's guidance is explicitly to detect after attempting a connection, not before, to avoid race conditions.
- **URLSession support:** `URLSessionConfiguration.allowsUltraConstrainedNetworkAccess` landed in **iOS 26.1**. On iOS 26.0, URLSession requests reportedly don't go through on the ultra-constrained path at all — a raw `NWConnection` with `NWParameters.allowUltraConstrainedPaths = true` is needed instead.
- **Minimum requirements:** iPhone 13+, iOS 26+, and a carrier that has enabled Direct-to-Cell data (currently One NZ in NZ; T-Mobile's T-Satellite is the closest US analog — not guaranteed to have identical app-level support).
- **Design intent:** apps that work in bursts (small messages, small payloads) are the intended use case — a good match for DotWatcher's existing position payload.
- **Handoff between cellular and satellite is automatic**, mirroring normal cell tower handover — no user toggle, no manual switching. Ground towers are always preferred; satellite only engages when no terrestrial signal is found. Handoff isn't instantaneous — brief drops during the transition are possible, especially while moving.

## Power management takeaways

- This is **not** the same as duty-cycling a dedicated satellite radio (e.g. Garmin inReach-style hardware) — the baseband's idle/search/registration behavior isn't something the app controls either way.
- What the app *does* control: request frequency and payload size. A 5-minute interval with a small JSON payload is well within the range these systems are designed for.
- GPS acquisition cost is unaffected by which network path the upload eventually takes.
- A satellite-backed connection may reduce the "aggressively hunting for signal" battery cost of a true dead zone, since there's something for the baseband to lock onto. Updated 2026-09-28: the underlying mechanism is well-established general cellular-hardware behavior, not specific to this app — a radio with no tower to register to scans continuously across bands at or near max transmit power with none of the sleep cycles a registered idle radio gets (the same reason "phone died fast in that dead zone" is a common experience, and why Airplane Mode is the standard workaround). So a true no-coverage dead zone is likely the worst-case battery scenario a runner can be in, worse than steady cellular and plausibly worse than satellite too. Still not measured on this app/hardware specifically, and nothing the app can do about it either way — this happens at the baseband/radio-firmware level, with no API for a third-party app to influence it (see the "sleep and retry" non-fix noted below).
- **A "sleep, then retry the search" approach doesn't work, even in principle.** iOS gives no third-party app any API to pause/resume the cellular radio's own tower search — that's baseband firmware territory, same as GPS acquisition above. And even indirectly, having the app stop attempting POSTs for a while wouldn't help: the modem searches for a tower continuously and automatically regardless of whether any app currently wants network access, since the phone always tries to stay reachable for calls/texts. App-level request cadence has no bearing on it, same lesson as GPS acquisition being unaffected by network path. The only thing that would actually stop the search is Airplane Mode, which no app can toggle programmatically — the runner would have to do it manually, and would need to remember to undo it once back in range.

## Detectability — payment plans and OS version

- **Carrier/plan eligibility:** not detectable ahead of time, and doesn't need to be. If satellite isn't available for any reason (no plan, unsupported carrier, etc.), the request just fails/times out like any other no-coverage scenario — existing "queue and retry" logic already covers this. **No onboarding question needed.**
- **OS version:** detectable and should be handled via `#available(iOS 26, *)` (and a further check/branch for 26.0 vs 26.1+ if supporting 26.0). Purely a code-level capability gate — no user-facing prompt needed.

## Proposed iOS app changes

1. **Detection layer** — `NWPathMonitor` watching `isUltraConstrained`, gated behind `#available(iOS 26, *)`. Decide whether to support iOS 26.0 (needs NWConnection fallback) or require 26.1+ (simpler, URLSession-only).
2. **Networking layer — split essential vs. non-essential traffic:**
   - `POST /location` (outgoing position) → opt in to constrained/expensive/ultra-constrained access.
   - `GET /locations/{sessionCode}` (polling for map) → do **not** opt in; let it fail silently while ultra-constrained.
   - Likely needs two separate `URLSessionConfiguration`s (or NWConnection setups) rather than one shared session.
3. **UI/state changes:**
   - When ultra-constrained: stop polling, stop rendering/updating the map, show a status message instead.
   - Follow the existing "never color alone" pattern — e.g. a pill/badge: "📡 On satellite — sending position, map paused."
   - Show "last sent" timestamp if cheaply available.
   - Auto-resume polling/map when the path returns to normal. No user action either direction.
4. **Retry behavior tweak** — consider a longer backoff while ultra-constrained (satellite acquisition is slower than a normal handshake), rather than retrying on the same short interval used for normal cellular failures.

## Implementation status (2026-08-11)

The "Proposed iOS app changes" above (detection layer, essential/non-essential traffic split,
own-device map-freeze + status badge, and the longer-cadence-as-backoff behavior) were already
built prior to this note — see `LocationManager.isUltraConstrained`/`interval` and
`NativeMapView.isUltraConstrained` for the shipped versions. That work covered the runner's own
device experience only; nothing about a runner's satellite state was visible to anyone else.

What shipped in this round is the piece the plan above didn't cover — telling *other session
members* that a runner is on satellite:

- iOS now reports `NWPath.isUltraConstrained` (captured at post time) as `isUltraConstrained` on
  every `POST /location`, alongside `timestamp`/`nextExpectedAt`.
- Named for what the OS actually classifies, not `isSatellite` — Apple's own guidance (DTS forum
  response) is that the property describes expected network behaviour, not a specific medium.
  Satellite just happens to be the only real-world case that sets it today.
- Server threads it through `LocationUpdate` → `RunnerPosition`, and the web client renders it as
  an independent 📡 overlay in the Legend pill — deliberately not tied to the `nextExpectedAt`
  countdown, since a slower cadence and this flag are unrelated facts (see POST-nextExpectedAt.md).
- Phone simulator (`tools/simulator`) got a matching "Satellite" mode for testing without a real
  device.

**Update (2026-08-12): now persisted.** The live-only limitation this section used to describe is
fixed — see `.ai/plans/persist-isUltraConstrained.md`. `location_updates` now has an
`is_ultra_constrained` column, written on every `POST /location` and read back by every
recording/playback path (`GetRecordingAsNdjson`, `GetRecordingWindowAsNdjson`, the cross-pod
`LoadLatestPositionsByRunner` fallback), so scrubbing into history, reloading after a server
restart, or re-importing a downloaded session now shows the satellite badge exactly as it
happened. `nextExpectedAt` was persisted in the same pass (see POST-nextExpectedAt.md). Rows from
before 2026-08-12 still read back as `false` (they backfilled with that default), since none of
them could have reported satellite before the column existed.

## Update (2026-09-28): app not appearing in Settings' satellite-ready app list

DotWatcher wasn't showing up in the iOS Settings screen that lists apps a carrier (One NZ)
considers satellite-ready. Cause: `DotWatcher.entitlements` only declared
`carrier-constrained.app-optimized`, never the separate
`com.apple.developer.networking.carrier-constrained.appcategory` entitlement. That's the
self-select array carriers read to decide which apps to enable for satellite data — with no
category declared, there was nothing for One NZ to allow. `app-optimized` alone doesn't populate
that list; it only certifies the app once optimised and suppresses the constrained-network
permission alerts.

Fix: added the `appcategory` array with two categories:

- `health-fitness-8014` — "apps for health and fitness".
- `hiking-adventure-8003` — "apps that support hiking and outdoor activities". The closer fit for
  trail runners, and One NZ's published list of satellite-enabled apps leans heavily this way
  (AllTrails, Plan My Walk, Te Araroa, NZTopo50, GetHomeSafe).

One NZ doesn't publish which categories it enables, so declaring both improves the odds without
claiming anything untrue about the app. Deliberately *not* declared: `emergency-8007` (DotWatcher
isn't an emergency service) and `maps-8002` (that category is for navigating to a destination).
Full list of valid values:
<https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.networking.carrier-constrained.appcategory>

### Release checklist for entitlement changes

1. **App ID capability.** `app-optimized` already signs, so the App ID has that capability. The
   `appcategory` entitlement may be a separate capability in the Apple Developer portal
   (Certificates, Identifiers & Profiles → Identifiers → DotWatcher's App ID). If it isn't enabled
   there, archiving fails with a provisioning profile / entitlement mismatch. Enable it and
   regenerate (or let Xcode refresh) the profile.
2. **Verify what's actually signed.** The `.entitlements` file is only an input. On the archived
   app (`.xcarchive` → Products/Applications/DotWatcher.app):
   ```bash
   codesign -d --entitlements - DotWatcher.app
   ```
   Both `carrier-constrained.app-optimized` and `carrier-constrained.appcategory` (with its array)
   must appear.
3. **Check the Settings list** on an iPhone 13+ with One NZ Satellite Data, while still in normal
   coverage. Unconfirmed whether TestFlight builds appear there. If a build that definitely has the
   entitlements (step 2) still doesn't show, TestFlight is the next suspect, then One NZ's
   category allowlist.

### Supported devices for satellite

Satellite posting needs **iPhone 13 or later on iOS 26.1 or later**, plus a One NZ plan with
Satellite Data. The app itself still installs from iOS 17.6. Below 26.1 it behaves as it always
has: no satellite path, no satellite detection.

iOS 26.0 is excluded on purpose. The entitlements exist from 26.0, but
`URLSessionConfiguration.allowsUltraConstrainedNetworkAccess` (and `NWPath.isUltraConstrained`)
only arrived in 26.1. Covering 26.0 would take a hand-rolled `NWConnection` HTTP client (as One NZ's
Smudge sample does) for a population that has all but disappeared. Decided 2026-09-28; this closes
the "iOS 26.0 support or 26.1+ minimum?" open question below.

### Not doing

- **Retry queue for failed `POST /location` calls.** A satellite handover can drop a post, but
  DotWatcher is about where the runner is *now*. The next post, one cadence later, replaces a lost
  one, so buffering stale positions isn't worth it here.

The `URLSession`/`NWPathMonitor` side (essential-vs-non-essential session split,
`allowsUltraConstrainedNetworkAccess`, reactive `isUltraConstrained` detection) was already correct
and needed no change — see "Implementation status" above.

## Open questions

- ~~**iOS 26.0 support or 26.1+ minimum?**~~ Decided 2026-09-28: 26.1+ for satellite (see "Supported devices for satellite" above).
- **Fully hide the map, or show a dimmed/frozen last-known frame behind the status message?** Leaning toward full hide + message, since a frozen frame risks being misread as live.
- **Build this into the current external-browser map flow, or fold it directly into the planned native MapKit view?** Since the polling loop this logic hooks into is also central to the native map work already on the roadmap, it may make sense to build it once as part of that scaffold rather than twice.
- Whether to visually reuse the SVG/design language from the existing paused-state treatment for the satellite status badge.