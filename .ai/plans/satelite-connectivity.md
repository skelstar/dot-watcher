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
- A satellite-backed connection may reduce the "aggressively hunting for signal" battery cost of a true dead zone, since there's something for the baseband to lock onto — untested hypothesis, not confirmed.

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

## Open questions

- **iOS 26.0 support or 26.1+ minimum?** Leaning toward 26.1+ to avoid the NWConnection fallback path, but needs a call.
- **Fully hide the map, or show a dimmed/frozen last-known frame behind the status message?** Leaning toward full hide + message, since a frozen frame risks being misread as live.
- **Build this into the current external-browser map flow, or fold it directly into the planned native MapKit view?** Since the polling loop this logic hooks into is also central to the native map work already on the roadmap, it may make sense to build it once as part of that scaffold rather than twice.
- Whether to visually reuse the SVG/design language from the existing paused-state treatment for the satellite status badge.