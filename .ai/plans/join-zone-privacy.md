# Join zone privacy — Plan

*DotWatcher — plan, 26 November 2026. Status: the core rule is already true in code; nothing below is built.*

A nearby session's join zone (the meeting point plus its radius) tells anyone who sees it where the event is and how
big the area is. **Rule: only the organiser ever sees the join zone.** Nobody else gets it: not people nearby, not
members who joined, not watchers.

Related: [`nearby-join.md`](nearby-join.md) (the feature, "Location is for discovery only"),
[`one-map-screen.md`](one-map-screen.md) (the map merge, which must keep this gate). The simulator's yellow capture
zone (branch `simulator-groups-prod`, commit `4470ea4`) is an operator view and isn't affected.

Per `AGENTS.md`, build and test verification is left to CI. Tick boxes as work lands: `- [ ]` → `- [x]`.

---

## What's already true

- **Server:** the meeting point is stored but never sent. `MeetingPoint` in `server/NearbyJoin.cs` is marked
  "Never returned to clients", and `NearbyStatus` carries only `nearby` and `radiusMeters`. People nearby get a
  title, the organiser's name and a distance rounded to 50 m, with no bearing (`/me/events/nearby`).
- **iOS:** `SessionMapScreen.joinRange` draws the dotted ring only when `nearbySessionId` is set, which needs the
  session to be in `ownedSessionIds`. The centre comes from `LocationManager.nearbyMeetingPoints`, the phone's own
  Ping fix.
- **Android:** `LiveMapScreen` passes `joinRange` only from `nearbyOrganiser`, which only the organiser has. The
  centre is the phone's own fix (`AppViewModel.nearbyMeetingPoints`).

So a joiner's phone can't draw the zone, because it never has the point. That is the guarantee worth keeping.

## Gaps

1. **Nothing tests the rule.** A later change could add coordinates to a reply without any test failing.
2. **The organiser loses the ring after a relaunch.** `nearbyMeetingPoints` is in memory only, so the ring is gone
   until they Ping again. That's confusing, but it isn't a privacy problem.
3. **The map merge could drop the gate.** When the Go and session maps become one shell, the join-range input must
   stay organiser-only.

## Work

- [ ] **J1 Server test: no coordinates out.** In `tests/DotWatcher.Server.Tests/NearbyJoinApiTests.cs`, check that
      the replies from `PUT /sessions/{id}/nearby`, `GET /me/events/nearby`, `POST /nearby/sessions/{id}/join` and
      the session/membership replies a joiner gets contain no latitude or longitude for the meeting point. Check the
      raw JSON, not the model, so a new field is caught.
- [ ] **J2 iOS: keep the organiser's meeting point across relaunch.** Save `nearbyMeetingPoints` for owned sessions
      on the device (UserDefaults, keyed by session id) and clear it on switch off, End and archive. Keep it on the
      device only: nothing new goes to the server.
- [ ] **J3 Android: the same as J2,** with DataStore, in `AppViewModel`.
- [ ] **J4 Comment the gate** where the ring is drawn on both platforms ("organiser only; see
      `.ai/plans/join-zone-privacy.md`"), and add a line to `one-map-screen.md` so the merge keeps it.
- [ ] **J5 (optional) Restyle the ring** as a transparent yellow fill with no outline, to match the simulator.
      Organiser only, so privacy is unchanged. Decide after seeing it on a real map: the outline may be needed over
      LINZ topo tiles.

## Left out on purpose

- **Showing the zone to joined members** (an organiser switch, off by default). It would need the server to send
  the meeting point to members, which breaks the "never returned" rule, and members can't do anything with it,
  since they've already joined. Revisit only if someone asks for it, and then send the point only to members, never
  to people nearby.
- **Recovering the point from the server** after a reinstall or on a second phone. A new owner-only read would be
  simple, but it would be the first route that sends coordinates out. Re-Pinging covers this case.

## Open questions

1. J5: dotted ring (now) or yellow fill on the apps?
2. Rounded distance plus walking around could still narrow down where the meeting point is. The existing limits
   and small radii are the defence (`nearby-join.md`). Is that enough, or should distances be coarser (100 m) for
   the 250 and 500 m radii?
