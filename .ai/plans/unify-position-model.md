# Unify the position model (Go and named sessions)

Branches: `crew-unify-position-model` (server first), then `crew-unify-position-model-ios` and
`crew-unify-position-model-android`. Builds on `crew-go-dot-initials`, which added `Initials`.

## Why

Casual had its own `CasualPosition` (same fields as `RunnerPosition` plus `InSession`, `IsSelf`, and
now `Initials`). Each client mirrors it with a second model, Android copies field by field into
`RunnerPosition` in `CasualMapScreen.kt`, and a fix to the dot letters had to touch both models and
both map screens. A Go dot showed "SE" for a crew nickname "Sean" because the letters came from
`RunnerName` (the nickname), not the account's initials.

## Endpoints today (unchanged by this plan)

| | Named session | Go (casual) |
|---|---|---|
| Send | `POST /location` | `POST /location` (server branches on `IsCasualSession`) |
| Read | `GET /locations/{id}` -> `RunnerPosition[][]` | `GET /casual/live` -> `{participants, positions, nearby}` |
| Start/stop | `/sessions` endpoints | `POST /casual/start`, `/casual/stop` |

Keep the two reads separate: they enforce different access rules (membership vs crew and proximity),
and merging them would risk the casual privacy guarantees. `nearby` and crew labelling stay casual-only.

## Target

One position type on the wire, `RunnerPosition`, with optional `inSession`, `isSelf`, `initials`
(null/false for named sessions). Casual responses use it; `CasualPosition` goes away.

## Steps

- [x] Server: `Initials` on casual positions (branch `crew-go-dot-initials`)
- [x] Server: add `InSession`, `IsSelf`, `Initials` to `RunnerPosition`; `CasualLiveResponse` uses it; delete `CasualPosition`. Casual JSON is unchanged; named responses gain three additive fields.
- [ ] Server: tests green in CI (casual API tests, session API tests); add a named-session decode test
- [ ] iOS: fold `CasualPosition` into `RunnerPositionResponse` (add `isUltraConstrained`, `inSession`, `isSelf`, `initials`); one shared pin model fed by `CasualViews.swift` and `SessionMapScreen.swift`
- [ ] Android: delete `CasualPosition`; add the three fields to `RunnerPosition`; drop the copy in `CasualMapScreen.kt`; update `CasualWireTest.kt`
- [ ] Later cleanup PR: remove anything only old clients needed

## Compatibility and rollout

Old apps decode strictly, so only additive changes are safe: server first (both shapes populated),
then the apps. The web client does not consume positions (`WatchPage` has its own DTO). Dots in a
named session already draw letters from the account/display name, so they need no change.

## Open questions

- Should named-session positions also send `initials`? Today `RunnerName` there is the member's display name, never a nickname.
