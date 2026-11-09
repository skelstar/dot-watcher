# Demo crew member ("Try demo")

Branches (suggested): `crew-demo-server`, then `crew-demo-ios` and `crew-demo-android`. Web client later.

## Why

A new user who starts Go sees only their own dot. They have nothing tangible to look at, so the app
looks empty. The demo gives them a crew member walking a real path near them, so the map, crew and
liveness ideas are visible straight away.

## Principle: as real as possible

The demo is not a special mode with fake UI. It is a **real crew member in a real Go session**. The
only fake thing is where the demo dot is. Everything else (the user's live dot, the crew list, the
dot colours and letters, liveness states, the Go start/stop flow) runs the normal code path.

Consequences:

- The demo member is a real crew entry (marked "Demo"), not a client-side ghost marker.
- The user's own dot is live (real GPS) and goes through the normal Go flow.
- The demo dot arrives through the same read as real dots (`GET /casual/live`), so no second marker
  pipeline is needed in the apps.
- Because it is real data, the server must make sure it can never leak: the demo member's
  position is never visible to anyone but the user it was created for.

## Entry point

- A "Try demo" tile to the right of "Go" on the start screen, in a different colour from Go.
- Starts only from the tile, never automatically from a Go session.
- Loops until dismissed (see below).

## Route ("nearest path")

- Overpass query for `highway=path|footway|track` near the user's location.
- Pick a connected, walkable segment of about 300-500 m and loop it (out-and-back or circuit).
- **Wait for the path.** Show a short "Finding a path near you" state until Overpass returns. No
  generated-loop fallback shown straight away.
- If Overpass fails or finds nothing, show an error with Retry. A generated loop is an open question
  (see below), not part of phase 1.
- Put route building behind a `DemoRouteProvider` so the source can change later.

## Dismissing

- Persistent "Demo" chip on the map with an **Exit demo** button.
- Leaving the map ends the demo. It is never restored after the app is killed or relaunched.
- "Remove demo" on the demo row in the crew list.
- Ends automatically if the user starts a normal Go session or joins a real crew, so real dots never
  share a map with the demo.
- After first use the tile is quieter (outlined or smaller); it is not removed.

## Open design question: how does the demo dot get its position?

Because the demo must be real, the server is involved. Options:

1. **Server computes on read (recommended):** the client sends the route polyline once when the demo
   starts; the server stores it with the demo session and, when answering `/casual/live`, returns the
   demo member at the point along the route for the elapsed time (looped). No background job, no
   posting loop, no extra writes. Dismissal deletes the demo rows.
2. **Client posts for a demo account:** the app posts positions for a system account on a timer.
   Closer to the normal write path, but a backgrounded app stops the demo and it adds write traffic.
3. **Background service on the server:** most realistic, but a hosted job just for a demo.

Whichever is chosen:

- The demo member is a system account (or a flagged row), excluded from admin session lists, stats
  and the public crew link flows.
- Staging and Production share one database (see crew conventions), so demo rows must cascade-delete
  with the user and have a TTL cleanup in case the app never calls "exit".
- A demo member must not count toward crew limits or show in other users' crews.
- Old apps decode strictly: demo positions must use the existing `RunnerPosition` shape. Add only
  optional fields (for example `isDemo`), server first.

## Phases

### Phase 1: demo crew member

- [ ] Decide the position approach (see open question) and write the server design
- [ ] Server: start/stop demo endpoints, demo member flagged and hidden from everyone but its owner, TTL cleanup, tests
- [ ] iOS: "Try demo" tile, Overpass `DemoRouteProvider`, "Finding a path" state, Demo chip and Exit demo, crew-list row with Remove demo
- [ ] Android: same as iOS
- [ ] Help text in `Help.md`

### Phase 2: richer behaviour

- [ ] Demo dot goes "signal lost" partway through to show liveness states
- [ ] One fake alert

### Phase 3: "who can see me" (grey dots)

Blocked on the privacy model (`privacy-watch-list-concept`), which is concept-only. Do not ship a
demo of behaviour the server does not enforce.

- [ ] "As seen by" toggle (friend / stranger / organiser) once the server really greys dots
- [ ] Label it so it never implies a guarantee the product does not give

## Later

- Web client: it does not use browser geolocation today. Use a fixed demo location (a known park or
  trail with a stored route) behind a "Try demo" button on the landing page, or add a geolocation
  permission prompt.

## Open questions

- Position approach: server-computed on read, client-posted, or a server job?
- If Overpass fails, offer a generated loop after a timeout, or only Retry?
- Should the demo member have a fixed name ("Sam (demo)") or a random one?
- Should the demo end after a maximum time as well as on dismissal (battery and server cleanliness)?
