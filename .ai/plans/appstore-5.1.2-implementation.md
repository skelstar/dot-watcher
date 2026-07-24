# Plan: App Store 5.1.2(i) compliance — blocking, consent, bounded tracking

Status: implemented 2026-07-24 (server + iOS), not yet built/tested on device or committed.
Companion doc: [appstore-5.1.2-response.md](appstore-5.1.2-response.md).

## Implementation notes (post-hoc)

- `GetSessionRunners` changed from `IReadOnlyList<string>` to `IReadOnlyList<SessionRunner>`
  (`userId` + `displayName`) — the only server response shape change beyond the new block
  endpoints, needed because no existing API exposed a participant's `userId` to the client.
  Scoped to the session-runners roster only (used by the join response's `Participants` field
  and `GET /sessions/{id}/runners`), not the position/map pipeline (`RunnerPosition` stays
  name-only) — confirmed out of scope with user.
- iOS keeps `participants: [String]` (display names) as the source of truth for the grid/map,
  and adds a parallel `participantUserIds: [String: String]` lookup so blocking has a `userId`
  without touching everywhere `participants` is consumed.
- Live participant grid does **not** show a persistent "blocked, X overlay" dot as sketched in
  the early mockup — `blockUser` evicts the blocked user from `participants` immediately (they
  vanish from the live roster), and durable block/unblock management lives in the dedicated
  Blocked Users screen (Account → Blocked Users) instead. This diverged from the second mockup's
  in-grid X-overlay concept once the live roster's data flow made that impractical without
  keeping evicted users artificially present in `participants`.
- "Leave" button now reads "Stop" whenever `isTracking` is true, "Leave" otherwise — confirmed
  as a pure relabel, same underlying full-leave behavior in both cases.
- Web client (`client/`) untouched except `LegalPage.tsx` — confirmed no consumer of
  `GetSessionRunners`/`/runners` exists there (runner names are derived from position data).

## Remaining before resubmission
- [ ] Build and manually test on a device/simulator: consent sheet, expiry countdown, block
      flow, Blocked Users screen.
- [ ] Decide whether to commit/PR this work (per project convention: PRs target `staging`).
- [ ] Then proceed with the reviewer response in appstore-5.1.2-response.md.

## Goal

Address the three requirements from Apple's rejection that need real product changes
(the other two — 18+ age rating, privacy policy URL — are App Store Connect config only,
see the response doc):

1. A mechanism to block another user, account-level and cross-session (owner's choice).
2. Explicit, declinable in-app consent to share location with other session members,
   separate from the OS CoreLocation prompt.
3. No indefinite/silent automatic location sharing — automatic posting only runs inside
   a bounded, explicitly-started tracking session with a visible indicator, a manual stop,
   and a hard expiry.

Constraint carried over from the rejection: **no user-facing "automatic vs manual" toggle**
for point 3 — the reviewer explicitly ruled that out. Automatic posting must always be scoped
to a session the user just started, never a standing setting.

## Current state (as of this plan)

- Sessions are invite-code based, not friend/contact based — anyone with the code can join.
  No pairing or per-user permission model exists (`server/Stores/SessionStore.cs`, schema
  at lines 42-88: `users`, `app_sessions`, `session_members`, `location_updates`).
- `session_members` PK is `(session_id, user_id)`, with `role` (`runner`/`viewer`) and
  `left_at` for self-service leave. No block/kick concept anywhere — confirmed via grep,
  only match is the self-leave doc comment (`SessionsController.cs:216-227`).
- `SessionMembership` record (`server/Models/AuthModels.cs:55-61`) is the shape returned by
  create/join/list — independently redeclared in `client/src/types.ts` and
  `LocationManager.swift` (no shared schema/codegen; same pattern noted in
  `session-update-interval.md`).
- `GetMembership` (`SessionStore.cs:440-460`) is the single query backing `CanReadSession`,
  `CanWriteLocation` — the natural choke point for filtering out blocked relationships.
- `GetLatestPositions` (`SessionStore.cs:552-567`) reads from an in-memory
  `ConcurrentDictionary<sessionId, ConcurrentDictionary<userId, List<RunnerPosition>>>`
  (`_sessions`), keyed by `userId` — also a natural filter point for blocked users.
- iOS: `LocationManager.start()` (`LocationManager.swift:392-416`) is the sole entry point
  that begins automatic posting — sets `isTracking = true`, kicks off `trackingLoop()`
  (15s cadence, `LocationManager.swift:158,426-433`) with no bound on duration and no
  consent step beyond the one-time OS `requestAlwaysAuthorization()` prompt.
  `stop()` (`LocationManager.swift:418-424`) is the only way tracking ends today, always
  manual.
- A working, substantive privacy policy already exists and is live at
  `https://dot-watcher.skelstar.io/privacy` (`client/src/LegalPage.tsx:38-95`) — its
  "Sharing and Visibility" section will need a line about blocking once that ships.
- iOS map: `NativeMapView.swift`, shown via `LiveMapSheet` whenever `location.isTracking`
  (`ContentView.swift:306-310`), reads `location.runnerPositions`.
- Per-project convention (memory `feedback_ios_version_stamp`): bump `Info.plist`
  CFBundleVersion/GitCommitSHA when committing iOS changes. PRs target `staging`, not `main`
  (memory `project_pr_base_branch`).

## Design

### 1. Blocking (account-level, cross-session)

New `blocked_users` table: `(blocker_user_id, blocked_user_id, created_at)`, PK on the pair.
Blocking is one-directional in storage but enforced bidirectionally in reads (if A blocked B,
neither should see the other) — matches "second option" decision: block also removes the
blocked user from any session both are currently in.

Server:
- `POST /me/blocks/{userId}` — block a user. Effects, in one transaction:
  - Insert into `blocked_users`.
  - For every session where both users are members: set `left_at` on the **blocked** user's
    `session_members` row (same mechanism as `LeaveSession`, just triggered by the blocker).
- `DELETE /me/blocks/{userId}` — unblock. Does not restore session membership (blocked user
  would need to rejoin via invite code, consistent with how voluntary leave already works).
- `GET /me/blocks` — list current user's blocked users (for a "Blocked users" management
  screen).
- `GetMembership` (`SessionStore.cs:440-460`) gains a check: if either party has blocked the
  other, `CanReadSession`/`CanWriteLocation` return false as if not a member — belt-and-braces
  in case a stale membership row exists.
- `GetLatestPositions` / `GetSessionRunners` filter out any `userId` blocked by (or who has
  blocked) the requesting user — needs the caller's `userId` threaded into these calls, which
  today take only `sessionId` (`SessionStore.cs:484-500,552-567`). Check current call sites in
  `SessionsController.cs` for what's already available in scope.
- `POST /session-invites/{code}/join` (`SessionsController.cs:88-131`) rejects with 403 if the
  joining user is blocked by the session owner.

iOS:
- Add a "Block" action reachable from a participant's map pin or a participant list (need to
  check `NativeMapView.swift`/`ContentView.swift` for the best existing UI hook — no
  participant list UI currently found in the explore pass, may need a small new sheet).
- "Blocked users" management list in account settings (`AuthSheet.swift` is the existing
  account sheet — natural home), calling the new endpoints.
- On block, if the blocked user was in the current session, remove them from
  `location.runnerPositions`/participants immediately (optimistic) ahead of the next poll.

Web client (`client/`): mirror whatever minimal UI is needed if the web client also shows
session participants — confirm scope with user before building (may be iOS-only if the web
client is admin/viewer-only today).

### 2. Explicit consent to share location with other users

New one-time-per-session (or one-time-per-app-install, TBD) consent step, distinct from the
OS CoreLocation prompt, presented before the first `start()` call in a given session:

- New sheet/alert: "Share your location with other members of '<session name>'? Other people
  in this session will be able to see your live position on the map while tracking is active."
  Two explicit actions: "Share my location" / "Don't share" — no default/dismiss-as-accept.
- Declining does not block the user from the session — they remain a viewer (can see others
  who consented, per existing `role` semantics) but `start()` is refused.
- Store the decision (`hasConsentedToLocationSharing`, scoped per session or globally — TBD)
  so it's not re-asked every single tracking start, only on first share attempt or after a
  policy-version bump.
- Implementation hook: gate `LocationManager.start()` (`LocationManager.swift:392-416`) on
  this consent state before calling `clManager.requestAlwaysAuthorization()`.

### 3. Bounded automatic tracking (no indefinite/silent sharing)

Keep `trackingLoop()`'s automatic 15s posting exactly as-is *while a tracking session is
active*, but make "active" bounded rather than open-ended:

- Add a max duration constant: 24 hours — covers long races/ultras/multi-leg events while
  still being a hard, finite bound rather than indefinite background sharing.
- `start()` schedules an automatic `stop()` at `Date() + maxDuration` alongside starting
  `trackingLoop()`. Surfacing remaining time in the UI (e.g. in the existing tracking status
  area) is worth doing so the expiry isn't a surprise.
- Visible indicator: iOS already forces the background-location pill
  (`showsBackgroundLocationIndicator = true`, `LocationManager.swift:130`) — confirm this
  is still true and surfaced; no change likely needed here, just verify.
- No new toggle/setting for "automatic mode" — this is the only mode, just time-boxed.
- Optional, out of scope unless asked: a lighter-weight one-shot "check in now" action (single
  POST, no loop) for users who want to share a single position without starting a full
  tracking session — this would be the "manual" counterpart, additive, not a replacement.

## Implementation steps

### 1. Server (`server/`)
- [ ] Add `blocked_users` table to schema (`SessionStore.cs:28-88`).
- [ ] `POST /me/blocks/{userId}`, `DELETE /me/blocks/{userId}`, `GET /me/blocks` in
      `SessionsController.cs`, backed by new `SessionStore` methods (`BlockUser`,
      `UnblockUser`, `GetBlockedUsers`).
- [ ] `BlockUser` also ends shared-session memberships (reuse `LeaveSession`'s `left_at`
      update, `SessionStore.cs:588-...`, targeted at the blocked user's row).
- [ ] Thread blocking checks into `GetMembership` (`SessionStore.cs:440-460`),
      `GetLatestPositions` (`552-567`), `GetSessionRunners` (`484-500`) — these need the
      requesting user's ID in addition to `sessionId`; check/update call sites.
- [ ] `JoinSession` (`SessionsController.cs:88-131`) rejects joins from users blocked by the
      session owner.

### 2. iOS (`ios/DotWatcher/DotWatcher`)
- [ ] Consent sheet before first `start()` per session; new state in `LocationManager`
      (e.g. `hasConsentedToSharing(sessionId:)`), gate at `LocationManager.swift:392-416`.
- [ ] Max tracking duration constant + auto-stop scheduling in `start()`
      (`LocationManager.swift:392-416`), surfaced in tracking status UI.
- [ ] Block action UI (pin tap or participant list — confirm exact placement) calling new
      `/me/blocks/{userId}` endpoints.
- [ ] "Blocked users" list in account settings (`AuthSheet.swift`).
- [ ] Add new `SessionMembership`/response fields if any are needed client-side (e.g. whether
      current user has already consented) — check against server response shape changes above.
- [ ] Bump `Info.plist` CFBundleVersion/GitCommitSHA (project convention).

### 3. Web client (`client/`) — scope TBD
- [ ] Confirm with user whether the web client needs block UI or just needs to respect
      server-side filtering (likely the latter if web is viewer/admin-only).
- [ ] Update `LegalPage.tsx` "Sharing and Visibility" section to mention blocking.

### 4. App Store Connect (no code)
- [ ] Age rating → 18+ override.
- [ ] Add privacy policy URL to App Details.

## Not doing (out of scope unless asked)
- No "automatic vs manual" toggle — ruled out by the rejection itself.
- No general friends/contacts system — blocking is additive to the existing invite-code model,
  not a rebuild of session membership into a paired-contacts model.
- No change to the 15s posting cadence itself — only bounding its duration.

## Decisions
- Max tracking duration: **24 hours** (covers long races/ultras).
- Consent scope: **per-session** — asked the first time the user starts tracking in each
  session, not once globally. More defensible to Apple since each session has a different
  set of viewers.
- Blocking: **account-level, cross-session** — blocking removes the blocked user from any
  session both are currently in, and prevents them rejoining any session owned/shared with
  the blocker.

- Block UI surface: **touch-and-hold on a participant dot** in the existing Participants row
  of the session card (`ContentView.swift`) — opens an action sheet ("Block <name>" /
  "View on Map" / Cancel). A blocked participant stays visible as a greyed dot with an X
  overlay; touch-and-hold again offers Unblock. Confirmed via mockup review, see
  https://claude.ai/code/artifact/20b9844d-54dd-44e5-8d3a-a61c1ba55a86.
- Tracking status row becomes: pulsing red dot + "Tracking" + "7s" post-interval countdown
  (replacing the static green dot + "Sending"), with a green progress bar + "Auto-stops in
  15h 48m" caption underneath. Leave button relabels to "Stop" with no behavior change.
- Web client (`client/`): **out of scope for this pass** — it's view-only, so it benefits
  automatically from server-side filtering (blocked users excluded from position/runner
  queries) with no UI changes needed. Revisit if web ever grows session-management UI.
