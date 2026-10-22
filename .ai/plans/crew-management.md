# Crew management — Plan

*DotWatcher — plan, October 2026 (revised: mutual, codes only, camera-app QR scanning, crew nickname)*

Source design: [`crew-main-screen.html`](crew-main-screen.html) (interactive mock-up; open it in a browser). It still
shows the username/request flow, which is **dropped**; the code/QR flow below needs adding to it (task M1).
Related concept notes: privacy / "people you know" (artifact linked from the project memory).

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI on the PR;
nothing below is verified locally.

---

## Decisions taken

- **Mutual.** A crew link is one symmetric relationship: both people see each other, either can remove it.
- **Codes only.** The only way to add someone is a **crew code** that person gave you: typed/pasted, tapped as a link,
  or scanned as a QR code **with the phone's own camera app** (DotWatcher has no in-app scanner). There is **no
  username lookup, no search, and no request/accept/decline step**. Nobody can be found by someone who doesn't
  already hold a code they chose to share.
- **First redeemer wins.** A code works once, for 24 hours. If several people receive the same code, whoever redeems
  it first becomes crew and everyone else gets the same "code not valid" answer. The claim is atomic on the server, so
  two people tapping at the same moment still produce exactly one winner.
- **Crew shows a nickname, never the username.** Usernames are not a safe thing to show (see D10): they may become
  email addresses. Crew members see an optional, user-chosen **nickname**, falling back to the account's initials.
- **Crew is per account.** Your crew belongs to your *login*, not to a phone and not to a session. Sign in on a new
  phone and your crew is already there; every session you create or join sees the same crew; deleting your account
  removes you from everyone's crew. Stored server-side keyed by user id.

## How crew-add codes are shared

Same machinery as session invite codes today, plus a QR code. Today iOS `ShareLink` (`ContentView.swift`,
`headerSection`) and the Android `Intent.ACTION_SEND` chooser (`LiveMapScreen.kt`, `shareInviteLink`) share a text
message built by `InviteLink.shareMessage`: a `https://…/join/{CODE}` universal link, the plain code and a web fallback.

| Channel | What the other person gets |
|---|---|
| **Share button** (WhatsApp, Messages, …) | "Add me to my DotWatcher crew! Tap to add me in the app: `https://…/crew/{CODE}` · Crew code: `{CODE}`" |
| **QR code** on screen (in person) | QR encodes the same `https://…/crew/{CODE}` URL, scanned with the phone's **normal camera app**. If DotWatcher is installed the link opens it straight to the confirm screen (iOS universal link / Android App Link); if not, it opens the web fallback page showing the code and the store links. **Needs Android App Links** (new, see A6) — without them an Android camera scan would only reach the web page. |
| **Typed / pasted code** | Into the "Enter a code" field on the add sheet (Paste button, like the join card). |

All three end at one confirmation: **"Add Priya to your crew? They'll see you too."** (the mutual wording is
mandatory per the concept notes). Nothing is added until the person taps Add — a link never adds anyone by itself,
same rule as invite links.

**Code properties** (a crew code names a *person*, so it is stricter than a session invite code):
- 8 characters from an unambiguous alphabet (no `0/O/1/I/L`), ~40 bits — vs 6 hex chars (24 bits) for session
  invites. Generated with `RandomNumberGenerator` like `GenerateCode`.
- **Single use**, **expires after 24 hours**, at most **3 active** per user, revocable any time ("Make a new code").
  First valid redeemer wins (claim = `UPDATE … SET redeemed_by_user_id = @me WHERE code = @code AND redeemed_at IS NULL
  AND expires_at > @now`, success only if one row changed).
- **Redeeming = instant mutual crew** (D9, decided): the owner consented by handing the code out, which is what makes
  "scan when you're together" work. The code is **one-time and meant to be sent directly to the one person you want
  as crew** (a message to them, or a QR shown to them in person), not posted to a group. The share UI says so
  ("Send this to one person. It works once.") and the share sheet is the only way a code leaves the app.
- Redeeming and previewing are rate-limited and failure is uniform (unknown / expired / used / blocked all look the same).

---

## ⚠️ Breaking changes and rollout hazards (read first)

**Headline: there are no intentional breaking changes to the existing wire contract.** Everything is additive (new
tables, new endpoints, new web route, no changed response shapes, no new validation on existing endpoints).
Installed iOS, Android and web builds keep working untouched. Dropping username lookup removed the hazard that
existing accounts would become discoverable. What remains is **four rollout hazards**, one new permission and two
guard rails.

| # | Severity | What can go wrong for existing apps/users | Mitigation (task) |
|---|----------|-------------------------------------------|-------------------|
| H1 | 🔴 **High** | **Account deletion could start failing (500) on Production.** Staging and Production share one Postgres database (see `shared-database-cutover.md`, and the comment in `SessionStore.GetLatestPositions`). `SessionStore.Initialize()` runs on Staging first and creates the crew tables in the shared DB. Production is still running *old* code whose `DeleteUserAccount` knows nothing about crew rows. If the crew tables reference `users(id)` with a plain foreign key (the style every existing table uses), any Production user who has crew rows and deletes their account hits an FK violation. Account deletion is an App Store requirement (5.1.1(v)), so this must not regress. | Declare **every crew FK `ON DELETE CASCADE`** (or `SET NULL` for the optional redeemer) so old code's plain `DELETE FROM users` still works. Tasks S1, T4. |
| H2 | 🟠 Medium | **Old Production code doesn't know about crew when a user is blocked.** `BlockUser` on the old build won't remove crew links, so a blocked user could stay in the blocker's crew list. | `GET /me/crew` filters out blocked pairs **at read time** (S5), in addition to cleaning up in `BlockUser` (S6). Deploy order also closes the window. |
| H3 | 🟠 Medium | **New app build talking to a server that doesn't have the endpoints yet** (e.g. a TestFlight/Play build pointed at a Production server not yet updated). `/me/crew` returns 404. | Clients treat 404 / non-2xx from `/me/crew` as "feature unavailable" and **hide the crew row silently** — no error banner (I5, A5). Release order: **server to Production first, then app builds** (R1–R3). |
| H7 | 🟠 Medium | **Universal-link side effect on already-installed iOS apps.** `apple-app-site-association` is per domain. Adding `/crew/*` to it makes **old iOS builds** (which only understand `/join/*`) launch when someone taps a crew link, then do nothing (`.onOpenURL` ignores unknown paths). Apple also caches the AASA file, so this can't be quickly undone. | **Publish the AASA change only after the iOS build that handles `/crew/*` is live in the App Store** (R4). Until then crew links open the web fallback page, which shows the code to copy. The share message always includes the plain code, so an old-app recipient can still paste it. Android has no App Links yet, so it is unaffected. |
| H8 | 🟡 New Android manifest entry | **Android App Links** (so a camera-app QR scan or a tapped link opens DotWatcher on Android): a new `autoVerify` intent filter for `/crew/*` and a new `/.well-known/assetlinks.json` on the web hosts. **No camera permission is added on either platform** (no in-app scanner). Old Android builds have no intent filter, so they are unaffected and there is no H7-style side effect; but a wrong signing fingerprint in `assetlinks.json` just silently falls back to the browser. | A5/A6, W3, R3. Verify with `adb shell pm get-app-links nz.skelstar.dotwatcher` on a release-signed build. |
| H10 | ℹ️ Additive column | New nullable `users.nickname` column in the **shared** database. Old Production code names its columns explicitly (`INSERT INTO users (id, username, …)`, `SELECT id, username, display_name, password_hash`), so an extra nullable column is invisible to it. | S1. Never `SELECT *` from `users`; T-tests run against the existing auth suite. |
| H5 | ℹ️ Guard rail | **Do not bump `MinimumApiVersion` / `API_VERSION`.** The 426 gate exists for breaking changes; this isn't one, and bumping would lock out every installed build. | R5: explicit "no bump" check in the PR. |
| H9 | ℹ️ Guard rail | **A leaked code adds a stranger.** A code posted in a public chat lets whoever redeems it first become your crew. Harmless today (crew grants nothing yet) but it matters once crew grants access. | Single use, 24 h expiry, 3-active cap, revoke, a confirm step on the redeemer's side, and the owner can remove anyone at once. Revisit before crew grants access (see below). |

**Explicitly out of scope because it *would* be breaking:** the mock-up says crew "can join and view without an
invite code". That changes the authorization model (`CanReadSession` / `JoinSessionByInvite` today = invite code or
membership only) and the privacy promise of "only people with the code can see you". It needs its own plan and a
version-gate decision. This plan builds only the relationship (who is in your crew), not what crew can do yet.

---

## Context

The mock-up proposes a "Your crew" row at the top of the main (start) screen that opens a crew sheet and an add
sheet. The ring around an avatar means "crew".

Today there is no social graph at all. The nearest existing things are `blocked_users` (one-directional, account
level) and `session_members` (per session). Main-screen UI lives in `ios/.../ContentView.swift` (`noSessionView`,
the join card) and `android/.../ui/session/SessionScreen.kt` (`SessionScreen`, "Join a Session" card). The web client
is an anonymous viewer; it only gains a fallback page for crew links. There is **no camera/QR code anywhere in the
apps today**, so scanning and QR generation are new on both platforms.

## Goal

- A signed-in user can add someone to their crew with a code (typed, pasted, linked, or scanned with the camera app),
  show/share their own code as text, link and QR, choose the nickname their crew sees, and remove someone from their crew.
- The main screen on iOS and Android shows the slim crew row and opens the crew sheet and add sheet.
- The ring "crew mark" exists as a reusable avatar treatment (row and sheet now; map dots later).
- Zero impact on installed builds (subject to H7 ordering).

## Design decisions

**Wire format** — camelCase JSON, ISO-8601 UTC strings, matching `BlockedUser`/`SessionMembership`. One read endpoint
returns everything the main screen needs; wrapped in an object so fields can be added later without a break:

```jsonc
// GET /me/crew
{ "crew": [{ "userId": "…", "nickname": "Priya", "displayName": "PR", "since": "2026-03-04T…Z" }] }
```

`nickname` is nullable (clients show `displayName`, the account's initials, when it is null). **`username` is
deliberately absent from every crew payload**, and a contract test asserts it stays absent (T6) — so switching
usernames to email addresses later cannot leak them to crew members.

**Endpoints** (all user-token auth; mirror the `/me/blocks` style):

| Method | Path | Purpose |
|---|---|---|
| GET | `/me/crew` | my crew |
| DELETE | `/me/crew/{userId}` | remove from crew (immediate, both sides) |
| POST | `/me/crew/codes` | create my crew code → `{ code, expiresAt }` (clients build the share URL from their own web base URL, as they do for invites) |
| GET | `/me/crew/codes` | my active codes (so "My code" shows the same one until it expires) |
| DELETE | `/me/crew/codes/{code}` | revoke a code |
| GET | `/me/crew/codes/{code}` | preview whose code this is → `{ userId, nickname, displayName, alreadyCrew }` for the confirm screen (auth required; rate-limited; uniform 404) |
| POST | `/me/crew/codes/{code}/redeem` | redeem ⇒ instant mutual crew (single use, first redeemer wins) |
| GET | `/me/profile` | my crew nickname → `{ nickname }` |
| PUT | `/me/profile` `{ "nickname": "Priya" }` | set or clear (`null`/empty) my nickname. There is no account-edit endpoint today (only `DELETE /me`), so this is new. |

**Data model** (all FKs cascade — see H1):

```sql
ALTER TABLE users ADD COLUMN IF NOT EXISTS nickname TEXT;   -- nullable, no default (H10)

CREATE TABLE IF NOT EXISTS crew_links (
    user_low_id  TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    user_high_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at   TEXT NOT NULL,
    PRIMARY KEY (user_low_id, user_high_id),
    CHECK (user_low_id <> user_high_id)    -- app stores the pair in ordinal order, one row per pair (mutual); no `<` CHECK because Postgres compares text by DB collation, not ordinal
);
CREATE INDEX IF NOT EXISTS idx_crew_links_high ON crew_links(user_high_id);

CREATE TABLE IF NOT EXISTS crew_codes (
    code                TEXT PRIMARY KEY,
    owner_user_id       TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at          TEXT NOT NULL,
    expires_at          TEXT NOT NULL,
    redeemed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
    redeemed_at         TEXT
);
CREATE INDEX IF NOT EXISTS idx_crew_codes_owner ON crew_codes(owner_user_id);
```

**Behaviour rules**
- Redeeming: own code ⇒ 400 ("That's your own code"); expired/used/unknown/blocked pair ⇒ identical 404; already crew
  ⇒ success no-op (the code is *not* consumed); otherwise insert the link and mark the code redeemed in one transaction.
- Removing someone deletes the link immediately for both people and does not notify them.
- No push notifications exist in either app (only local satellite alerts), so a new crew member shows up for the code
  owner on **next load (app foreground / pull-to-refresh)**.
- Limits (proposed, constants in one place): crew ≤ 50, active codes ≤ 3, 20 code previews+redemptions/hour/user.

**Identity shown to others:** `nickname` (optional) + `displayName` (initials) + `userId`; only ever shown to the
person who redeems a code and to existing crew. `userId` is already exposed by `/me/blocks`. Avatar colour: reuse
each platform's `RunnerColorPalette`, deterministic from `userId`.

**Why a nickname and not the username.** Today `NormalizeUsername` (`AuthController`) only requires 3–64 characters
after trim/lowercase — no character rules — so a username can be long, odd, or (soon) an email address. And the
account's `displayName` is a 2-letter initials tag by design, so two crew members can both be "PR". A separate
optional **nickname** solves both: short, friendly, user-chosen, and decoupled from the login identifier.
Rules: trimmed, 1–30 characters, no control characters, not all whitespace; `null` clears it. The user is prompted
to choose one the first time they tap "Show my code" (skippable → initials are shown). A nickname is user-generated
text shown to other people, so the existing Block feature is the abuse control (same as display names today).

---

## Phase 0 — Decisions (do before coding)

- [x] **D1** Mutual. *Decided.*
- [x] **D-codes** Crew-add via short code + QR, shared like invite codes. *Decided; mechanics above.*
- [x] **D-account** Crew is per account. *Decided.*
- [x] **D2** Username lookup removed; codes only. *Decided.* (Removes the discoverability change for existing accounts, the enumeration problem, and the whole request/accept/decline flow.)
- [x] **D6** Codes are **single use, 24 h, first redeemer wins.** *Decided.* Still to confirm: crew ≤ 50, ≤ 3 active codes, 20 preview/redeem attempts per hour per user.
- [x] **D7** iOS crew row on the start screen only, not in-session. *Decided.* iOS's main screen is going to adopt Android's layout soon, so build the row as a self-contained component (I4) that moves with it.
- [x] **D8** No in-app QR scanner; people scan with the camera app. *Decided — no camera permission, no scanner dependency.* Needs Android App Links (A6, W3) for a good Android experience. Remaining privacy-copy check: the nickname is shown to crew — `client/src/LegalPage.tsx`, `.ai/plans/appstore-5.1.2-*.md`.
- [x] **D9** Redemption is **instant mutual**; the code is one-time use and sent directly to the intended person. *Decided.* Revisit when crew starts granting session access (H9).
- [x] **D10** Crew shows an optional **nickname**; **username is never exposed** to crew. *Decided (your suggestion).* Still to confirm: nickname 1–30 chars, prompted (skippable) at first "Show my code".

## Phase 1 — Server (`server/`)

- [x] **S1** Schema: add `crew_links` and `crew_codes` to `SessionStore.Initialize()`, plus `ALTER TABLE users ADD COLUMN IF NOT EXISTS nickname TEXT` (follow the existing `ADD COLUMN IF NOT EXISTS` blocks). Idempotent, FKs `ON DELETE CASCADE` / `SET NULL` (**H1**, **H10**).
- [x] **S2** Models in `Models/AuthModels.cs` (next to `BlockedUser`): `CrewMember`, `CrewOverview`, `CrewCode`, `CrewCodePreview`, `UpdateProfileRequest`. Do **not** add `nickname` to `AuthenticatedUser`/login responses (keeps existing responses byte-identical).
- [x] **S3** `SessionStore` methods (the store already holds all persistence; stay with that convention): `GetCrewOverview`, `RemoveCrewMember`, `CreateCrewCode` (8 chars, unambiguous alphabet, enforces the 3-active cap), `GetActiveCrewCodes`, `RevokeCrewCode`, `PreviewCrewCode`, `RedeemCrewCode` (single transaction: atomic claim `UPDATE … WHERE redeemed_at IS NULL AND expires_at > now`, proceed only if one row changed, then insert the link), `GetNickname`, `SetNickname`. Crew queries select `nickname` explicitly and never `username`.
- [x] **S3a** Nickname validation in the controller (trim, 1–30 chars, no control characters; empty/null clears), error text in the style of the existing `{ error = "…" }` responses.
- [x] **S4** Rate limiter for code preview/redeem, modelled on `ClientErrorLimiter` / `AuthAttemptLimiter`, registered in `Program.cs`.
- [x] **S5** `GetCrewOverview` excludes any pair where a `blocked_users` row exists in either direction (**H2**, read-time safety net).
- [x] **S6** `BlockUser`: inside its existing transaction, delete the pair's `crew_links`.
- [x] **S7** New `Controllers/CrewController.cs` with the endpoints above (including `/me/profile`), `[ProducesResponseType]` in the style of `SessionsController`; do not log codes at Information (they are short-lived credentials) — log the owner id only. Note `Program.cs` request logging captures response bodies (≤2 KB) except `/auth*`: the code response from `POST /me/crew/codes` would be written to Seq, so either exclude `/me/crew/codes` from body capture or accept that expired single-use codes appear in logs (decide, then document).
- [x] **S8** Expired-code cleanup on write (delete codes expired > 7 days, like `DeleteExpiredRevokedTokens`).
- [ ] **S9** Confirm `DeleteUserAccount` needs **no change** because of the cascades and still works with crew data present (proven by T4, not assumed).

## Phase 2 — Server tests (`tests/DotWatcher.Server.Tests/`)

- [x] **T1** `CrewApiTests`: create code → preview as another user → redeem → both appear in each other's `crew`; code now consumed.
- [x] **T1a** **First redeemer wins:** one code, several users redeem (including concurrently, e.g. `Task.WhenAll`) → exactly one 200 and the rest identical 404; only the winner is in the owner's crew.
- [x] **T1b** Nickname: set / clear / validation (too long, blank, control chars); shown in `GET /me/crew` and the code preview; null falls back to initials client-side; `GET /me/profile` round-trip.
- [x] **T2** Code rules: single use; expired code rejected (use the injectable `TimeProvider`); own code 400; already-crew redeem is a no-op that doesn't burn the code; 3-active cap; revoke; uniform 404 for unknown/expired/used/blocked.
- [ ] **T3** Remove is immediate for both; unauthenticated → 401; rate limit enforced; crew-size limit enforced.
- [x] **T4** **Deleting an account that has crew links and codes succeeds** and leaves no orphans (guards **H1**); the other user's overview no longer lists them.
- [ ] **T5** Blocking removes the crew link (S6); a pre-existing link with a block present is hidden by the read-time filter (S5); blocked users cannot redeem each other's codes.
- [x] **T6** `ContractTests`: raw-JSON field-name contract for `GET /me/crew`, `POST /me/crew/codes`, preview and `/me/profile`, **asserting `username` and any email-like field are absent from crew payloads**; all existing contract and auth tests unchanged and green (login/register responses must not gain fields).
- [ ] **T7** `ApiVersionGateTests`: no change expected (**H5**); just confirm still passing.

## Phase 3 — Web fallback (`client/`)

- [ ] **W1** `parseUrl()` in `client/src/App.tsx` recognises `/crew/{CODE}`; new `CrewLinkPage.tsx` modelled on `JoinPage.tsx`: shows the code (with copy), "Open DotWatcher and paste this code in Your crew → Add", App Store / Play links (`appStore.ts`). Additive; existing routes untouched.
- [ ] **W2** Update the route comment above `parseUrl()` and `client/README.md`.
- [ ] **W3** **Do NOT yet add `/crew/*` to `client/public/.well-known/apple-app-site-association`** — gated by R4 (**H7**). Prepare the change but hold it.
- [ ] **W4** Add `client/public/.well-known/assetlinks.json` for Android App Links: package `nz.skelstar.dotwatcher`, the SHA-256 fingerprint(s) of the **release / Play App Signing** certificate (get it from the Play Console; a debug fingerprint will not verify a store build). Serve as `application/json` with no redirect — extend `client/nginx.conf` the way the AASA file is handled. Safe to publish any time (only apps carrying the intent filter ever read it).

## Phase 4 — Docs

- [x] **DOC1** `docs/api-reference.html`: the endpoints.
- [x] **DOC2** `docs/database-schema.html`: the new tables.
- [x] **DOC3** `server/README.md` (+ root `README.md` if it lists endpoints): "Crew" section incl. code properties and limits; `ios/README.md` link flow.

## Phase 5 — iOS (`ios/DotWatcher/`)

`apiVersion` stays `1` (**H5**).

- [ ] **I1** Models + API calls (in `LocationManager.swift` alongside the existing session/block calls, or a small extension); `crew` state exposed to views.
- [ ] **I2** Load crew with `loadSessions()` and on foreground; pull-to-refresh reloads it.
- [ ] **I3** Reusable crew-ring avatar (extend `RunnerCircle.swift` or add a sibling) + deterministic colour from `userId` via `RunnerColorPalette`.
- [ ] **I4** `CrewRow` on the start screen (`noSessionView`, above the join card; **not** in `sessionView`, D7): empty state ("Your crew / Add people that you know"), populated state (≤4 overlapping ringed faces, "+N", "N people"). Keep it a self-contained view with its own state so it moves unchanged when iOS adopts Android's main-screen layout.
- [ ] **I5** **Unavailable handling (H3):** `/me/crew` 404/non-2xx (except 401/426) ⇒ hide the row, no banner. 426 keeps using the Update Required flow.
- [ ] **I6** `CrewSheet` (tall sheet): "In your crew · N" dot grid, dot detail with Remove, "Add someone" button.
- [ ] **I7** `AddCrewSheet`: "Enter a code" (`CodeBoxField` with length 8 + Paste) · "Show my code". A hint explains "To scan someone's QR code, just open your camera." No in-app scanner.
- [ ] **I8** `MyCrewCodeSheet`: QR + code in large type + expiry ("expires in 23 h") + **Share** (`ShareLink`, message via a new `CrewLink.shareMessage`, mirroring `InviteLink.shareMessage`) + "Make a new code". Copy states plainly: "Send this to one person. It works once." QR generated on-device with CoreImage `CIQRCodeGenerator` (no dependency).
- [ ] **I9** `CrewLink` (new, beside `InviteLink.swift`): parse `/crew/{CODE}`, build share URL/message. Handle it in `.onOpenURL` → preview → confirm sheet. Keep the pending code across sign-in, same as invite links.
- [ ] **I10** Nickname: a "Name your crew sees" field (`GET`/`PUT /me/profile`) in the account area, and a skippable prompt the first time "Show my code" is tapped. Show `nickname ?? displayName` everywhere a crew member is named.
- [ ] **I11** Confirm screen: "Add *Priya* to your crew? They'll see you too." with Add / Cancel; "This code has already been used or has expired" on the uniform 404; success toast; Remove confirmation (takes effect immediately). A camera scan of a QR in the system Camera app arrives here via the universal link (I9).
- [ ] **I12** Accessibility: labels for every avatar/dot (the ring is never the only signal), the QR has a text alternative (the code), Dynamic Type check on the row.
- [ ] **I13** `Help.md`: "What is a crew?" and how to add someone.
- [ ] **I14** No entitlement change needed (same associated domains). Ship the build; gate the AASA change on it (R4).

## Phase 6 — Android (`android/`)

`API_VERSION` stays `"1"` in `DotWatcherApi.kt` (**H5**). No Android App Links exist yet (they were out of scope in
`ios-universal-invite-links.md`), so this plan adds them for `/crew/*` only; `/join/*` on Android stays a separate
follow-up. Without App Links, a camera scan or tapped link on Android would only open the web fallback page.

- [ ] **A1** Retrofit endpoints + DTOs in `DotWatcherApi.kt`; repository in `DotWatcherRepository.kt`; crew state + actions in `AppViewModel.kt`.
- [ ] **A2** Refresh crew with the existing session refresh and on resume.
- [ ] **A3** Ring treatment: `isCrew` option on `ui/InitialsDot.kt`; colour from `RunnerColorPalette.kt`.
- [ ] **A4** `CrewRow` composable at the top of `SessionScreen`, same states as iOS.
- [ ] **A5** **Unavailable handling (H3):** hide the row on 404/non-2xx, no error UI; existing `UpdateRequiredState` for 426 untouched.
- [ ] **A6** Android App Links (**H8**): in `AndroidManifest.xml` add to `MainActivity` an `<intent-filter android:autoVerify="true">` (VIEW / DEFAULT / BROWSABLE, `https`, hosts `dot-watcher.skelstar.io` and `dot-watcher-staging.skelstar.io`, `pathPrefix="/crew/"`); parse the code in `MainActivity` and hand it to the ViewModel for preview → confirm; keep a pending code across sign-in (as iOS does for invites); must also work when the activity is already running (`onNewIntent`). Publish `assetlinks.json` via W4. QR **generation** only: add ZXing **core** (`com.google.zxing:core`) to `gradle/libs.versions.toml`; check `proguard-rules.pro`. No scanner dependency, no CAMERA permission.
- [ ] **A7** Crew sheet, add sheet (enter/paste/show-my-code) and My-code sheet (QR + code + expiry + "Send this to one person. It works once." + Share via `Intent.ACTION_SEND` chooser, like `shareInviteLink` in `LiveMapScreen.kt`) as Compose `ModalBottomSheet`s; IME-aware layout.
- [ ] **A8** Confirm screen with the mutual wording and the "already used / expired" state; Remove confirmation; nickname field in `AccountSheet.kt` plus the skippable first-time prompt; show `nickname ?? displayName` for crew members.
- [ ] **A9** Content descriptions for dots/avatars/QR; TalkBack pass.
- [ ] **A10** Unit tests: crew-code parsing (App Link URL → code, rejects bad input) and the row's state logic, in the style of `RestoreDestinationTest.kt`.
- [ ] **A11** Bump `versionName`/`versionCode` per the Android release convention.

## Phase 7 — Mock-up

- [ ] **M1** Update `crew-main-screen.html`: remove the username field and Requests/pending/"Not now" UI; add "Show my code" (QR + Share + nickname prompt), "Enter a code" (no scanner), and the confirm screen, and drop the "N waiting" and request-pill states, so the build matches an agreed design.

## Phase 8 — Rollout order (protects existing apps)

- [ ] **R1** Merge server PR to `staging`; CI green. After deploy confirm the new tables exist and an **existing** account can still log in, create/join a session and delete a throwaway account.
- [ ] **R2** Promote the server to **Production before any app release** (closes **H1/H2/H3**).
- [ ] **R3** Deploy the web client with the `/crew/{CODE}` fallback page and `assetlinks.json` (both safe on their own). Ship iOS (TestFlight → App Store) and Android (internal → production) builds. Confirm Android App Link verification passed on a Play-signed build.
- [ ] **R4** **Only after the iOS build with crew-link handling is live in the App Store**, add `/crew/*` to the AASA file and redeploy the web client; verify headers (`application/json`, no redirect) on staging and prod (**H7**). Until then links open the web fallback page.
- [ ] **R5** In the PR description state: *no `MinimumApiVersion` / `X-Api-Version` change* (**H5**), the H1 cascade rationale, and the H7 AASA ordering.
- [ ] **R6** After release, watch Seq for 5xx on `/me/crew*` and `DELETE /me`, and 404s from old servers.
- [ ] **R7** Manual device checks, **iOS and Android**: scan the QR with the system camera (app installed / not installed), tap a shared link in WhatsApp and Messages, redeem an expired and a used code, two people redeeming the same code (second is refused), redeem your own code, block then redeem, nickname unset (initials shown) and set.

## Non-goals (deliberately later)

- Finding people by username, phone number or contacts; request/accept/decline flows.
- Crew joining/viewing sessions without an invite code, the "Crew + invites" start option, and the Private/Race cards
  (all "Coming soon" in the mock-up). **Breaking-risk work; separate plan** (see top).
- Showing the crew ring on live-map dots.
- Push notifications for new crew members.
- An in-app QR scanner (camera permission, VisionKit/ML Kit) — people use their camera app.
- Android App Links for `/join/*` session invites (only `/crew/*` is added here).
- Showing or editing usernames/emails anywhere in crew; the planned move to email-address usernames needs its own plan.
- Web client crew management (anonymous viewer, no account).
- Admin panel crew visibility.

## Open questions

- All decisions are made; D6/D10 only have small values to confirm (limits, nickname length).
- Should "Remove" also offer "Block" in the same detail card? (`BlockedUsersView` already exists on iOS.)
- Should the code owner see a "Priya just joined your crew" notice somewhere beyond the list (no push exists)?
- Android App Links need the Play App Signing SHA-256 fingerprint — who has Play Console access to fetch it?
- When usernames become emails: registration/login changes are outside this plan, but nothing here depends on the
  username format (crew never exposes it).
- When crew later grants session access, should crew codes get a shorter life or an owner-approval step (H9)?

## Effort estimate

Medium. Server: small (two tables, ~7 store methods, one controller, one limiter, tests). Each client is medium:
models and state, a row, three sheets, an avatar ring, plus QR generation and scanning, which neither app has today.
The web change is small. The risk is concentrated in rollout ordering (H1–H3, H7), not in the code.

## Implementation status (2026-10-23)

Server branch `crew-server-api` (Phases 1, 2, 4): written, **not built or run locally** (per `AGENTS.md`; CI on the PR
is the check). A ticked box means the code or test exists.

Differences from the design above, decided while building:
- `POST /me/crew/codes` returns `{ code, expiresAt }` only; clients build the share URL from their own web base URL.
- The code preview also returns `alreadyCrew`.
- `crew_links` has no `<` ordering CHECK (Postgres compares text by collation, not ordinal); the app stores the pair
  in ordinal order and the check is `<>`.
- Creating a fourth live code retires the oldest rather than failing.
- Log hygiene: `Program.cs` no longer copies `/me/crew/codes*` response bodies into the logs. The code in the request
  path of preview/redeem/revoke is still logged, as session invite codes are.

Not covered by a test yet (left unticked on purpose):
- **T3** rate limit and unauthenticated cases are tested; the crew-size cap (50) is not (needs 50 accounts).
- **T5** blocking removes the link and blocks redeeming (tested); the read-time hide for a pre-existing link plus a
  block (the old-Production case, **H2**) is not, because the API can't create that state.
- **T7** `ApiVersionGateTests` unchanged; to be confirmed green in CI.
- **S9** `DeleteUserAccount` untouched; proven only once T4 passes in CI.
