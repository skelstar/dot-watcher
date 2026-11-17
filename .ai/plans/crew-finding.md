# Find new crew — Plan

*DotWatcher — plan, 18 November 2026. Mock-up: https://claude.ai/artifact/7tQUaosi68TimYJzaP9hSk (v4).*

Adding crew in person, without a code. Two people standing together open the crew sheet, turn on
**Find new crew**, tap each other's name and accept. They become crew once **both** have accepted. Related:
[`crew-management.md`](crew-management.md) (codes, the crew sheet).

Per `AGENTS.md`, build and test verification is left to CI. The iOS and Android code was written on a
Windows machine without either toolchain, so it has not been compiled or run until CI or a device says so.

## Decisions

- **Both accept.** Tapping someone and accepting is half of it; nobody becomes crew because someone else tapped
  them. Same consent rule as codes.
- **Only people who opted in, within 50 m.** Others appear only while they also have it on and have sent a fix in
  the last 30 s. The 50 m test forgives each fix's accuracy up to 50 m, as nearby join does.
- **Names only.** Nickname, else initials. No position, distance or direction, and people are identified by a
  random token for the window, never an account id. The list order is when people turned it on, not distance.
- **Short-lived.** Off after 2 minutes, off when the crew sheet closes, and off every time the sheet opens. Turning
  it off drops unanswered accepts. No banner and no push: the people adding each other have the sheet open.
- **Blocked people and existing crew are never listed.** Accepting a blocked person's token is a 404, the same as a
  token that isn't there.
- **Server memory only** (`server/CrewFinder.cs`), like the rate limiters. A restart just turns everyone off.
- **Ways in:** the start screen's "Your crew" row and Go's "N crew out" chip (both already open the crew sheet), and
  a new **Crew** button on the session map's right-hand column, which has no crew chip.

## API

`POST /me/crew/finding` (on, with a fix), `PUT /me/crew/finding` (fresh fix every few seconds; 410 once the window
has ended), `DELETE /me/crew/finding` (off), `POST /me/crew/finding/{id}/accept`. All return the same state:
`expiresAt`, `people` (`id`, `nickname`, `displayName`, `youAccepted`, `acceptedYou`) and `added` (crew made in
this window). Details in `docs/api-reference.html`.

## Tasks

- [x] F1 Server: `CrewFinder`, endpoints on `CrewController`, `SessionStore.AddCrewLink`, `CrewFindingTests`, API
      docs. Branch `crew-finding-server`.
- [x] F2 iOS: "Find new crew" section at the top of the crew sheet (toggle, countdown, list, Accept dialog),
      polling in `CrewStore`, Crew button on the session map, which now presents the crew sheet itself. Branch
      `crew-finding-ios` (stacked on the server branch).
- [x] F3 Android: the same in `CrewModel` and `CrewSheet`, Crew button on `LiveMapScreen`, wire test. Branch
      `crew-finding-android` (stacked on the server branch).
- [ ] F4 Two-phone test on each platform: both on within 50 m, see each other, one accepts (the other sees the
      badge), the other accepts, both get the confirmation; close the sheet mid-way (accept dropped); wait out the
      2 minutes; location off shows the explanation.
- [x] F5 iOS `Help.md`: "Add someone next to you" under Crew (on the iOS branch).

## Open

- Should the session map's Crew button also show a count, like Go's chip?
