# Plan: Per-session configurable update interval

Status: drafted, not started. Written 2026-07-12.

## Goal

Let the session creator choose the location POST interval (e.g. 15s / 1min / 5min)
when creating a session. Every phone that joins that session must learn the
chosen interval and use it — the interval is a property of the session, not a
per-device setting.

## Current state (as of this plan)

- iOS posts location on a hardcoded `let interval: TimeInterval = 15` in
  `ios/DotWatcher/DotWatcher/LocationManager.swift:142`, consumed by an
  epoch-aligned `Task.sleep` loop (`trackingLoop()`, lines 373-380) — not a
  `Timer`, not CLLocationManager filters. Background location updates are
  already enabled (`allowsBackgroundLocationUpdates = true`,
  `UIBackgroundModes: location` in Info.plist), so switching to a longer
  interval needs no new background-mode plumbing.
- `CLLocationManager.distanceFilter = 10.0` only gates when `latestLocation`
  updates internally; it does not control POST cadence.
- Stale-pin detection is client-side only, in two places, both currently
  independent constants tied to the old 15s interval:
  - iOS: `NativeMapView.swift:7` — `staleAfter: TimeInterval = 30`
  - Web: `client/src/sessionLiveness.ts:1` — `LIVE_STALE_MS = 5 * 60 * 1000`
- Server has no stale/timeout logic at all (purely client-side today).
- Session model is a flat record with no config beyond identity:
  `SessionMembership(SessionId, SessionName, InviteCode, Role, DisplayName)`
  — `server/Models/AuthModels.cs:55-61`. Same shape independently redeclared
  in `client/src/types.ts:21-27` and `LocationManager.swift:42-50` (no shared
  schema/codegen between the three).
- `sessionName` is the closest existing precedent for a creator-chosen value
  that flows through create → DB → every join/fetch response. Follow the same
  three-place pattern for the new field.
- No `GET /session/{code}` detail endpoint exists — joiners only learn session
  state via the join response (`POST /session-invites/{code}/join`) or
  `GET /me/sessions`. Both return `SessionMembership`, so adding the field
  there covers both paths.

## Design

Add `updateIntervalSeconds` (name TBD) as a session-wide field, creator-chosen
at session creation, persisted server-side, returned in every
`SessionMembership` response (create + join + list), consumed by both clients
to drive their local POST loop and stale-threshold calculation.

Open question to resolve before/at implementation: what interval options to
offer (freeform seconds vs. a fixed set like 15s/1min/5min), and how to handle
a stale app version that ignores the field and keeps posting every 15s (is
that acceptable, or does it need a min-supported-version gate?).

## Implementation steps

### 1. Server (`server/`)
- [ ] Add `update_interval_seconds INTEGER NOT NULL DEFAULT 15` column to
      `app_sessions` table (schema in `server/Stores/SessionStore.cs:50-58`).
- [ ] Add `UpdateIntervalSeconds` to `CreateSessionRequest`
      (`AuthModels.cs:45-48`), default 15 if not provided.
- [ ] Add `UpdateIntervalSeconds` to `SessionMembership`
      (`AuthModels.cs:55-61`).
- [ ] Thread the value through `CreateSessionForUser`
      (`SessionStore.cs:336-366`) — persist on insert, return in the record.
- [ ] Thread the value through `JoinSessionByInvite`
      (`SessionStore.cs:368-396`) — read from the session row, return in the
      record so joiners get it.
- [ ] Verify `GET /me/sessions` (`SessionsController.cs:13-20`) picks up the
      field automatically (it reuses `SessionMembership`, so it should).

### 2. Web client (`client/`)
- [ ] Add `updateIntervalSeconds` to `SessionMembership` type
      (`client/src/types.ts:21-27`).
- [ ] Add a picker/input to the create-session form in `SessionPrompt.tsx`
      (createSession around lines 63-95), default 15s, offer 15s / 1min / 5min.
- [ ] `joinSession` (lines 97-127) needs no changes beyond the type — it
      already parses the full response.
- [ ] Update `sessionLiveness.ts` stale threshold to derive from the joined
      session's interval (e.g. ~2x) instead of the fixed 5-minute constant.
      Check `useRunnerMarkers.ts` / `ReplayControls.tsx` for any other
      per-pin staleness constant that also needs to scale.

### 3. iOS (`ios/DotWatcher/DotWatcher`)
- [ ] Add `updateIntervalSeconds` to `SessionMembership` struct
      (`LocationManager.swift:42-50`) — free pickup via existing
      `JSONDecoder().decode(T.self, ...)` as long as the property name/coding
      key matches.
- [ ] Change `LocationManager.interval` from a hardcoded `let` to a `var`,
      set from the selected session's `updateIntervalSeconds` in
      `selectSession(_:)` (`LocationManager.swift:314-317`), falling back to
      15 if absent (e.g. talking to an older server).
- [ ] Add the same interval picker to `CreateSessionView.swift` (currently
      UI-only, calls back into `LocationManager.createSession(name:displayName:)`
      at `LocationManager.swift:286-298` — extend that call with the chosen
      interval).
- [ ] Update `NativeMapView.swift:7` stale threshold (`staleAfter = 30`) to
      derive from the session's interval instead of a fixed constant — pass
      it down from wherever `NativeMapView` gets constructed.
- [ ] Confirm `CLLocationManager.distanceFilter` / background-mode config
      (`LocationManager.swift:174-179`) still make sense at 5-minute cadence;
      no code change expected here, just a real on-device background test
      (lock the phone for 10+ minutes, confirm posts land at the configured
      cadence) before considering this done.

### 4. Bump iOS build/version
- Per project convention: bump `Info.plist` CFBundleVersion/GitCommitSHA when
  committing iOS changes (see memory `feedback_ios_version_stamp`).

## Not doing (out of scope unless asked)
- No BGTaskScheduler / background refresh changes — existing background
  location mode already supports arbitrary intervals.
- No server-side stale/timeout detection — staleness stays client-side,
  just made interval-aware.
- No general per-device settings screen — interval is chosen once, at
  session creation, by the creator only.

## Notes
- A parallel `LocationManager.swift` exists under
  `.claude/worktrees/agent-a6d69db60789cb315/` — ignore it, not authoritative.
- PRs for this work target `staging`, not `main` (project convention).
