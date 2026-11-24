# Server alerts — Plan

*DotWatcher — plan, 25 November 2026. Status: nothing built. Fixes the crew-out alert bug below, and gives the
server a general way to have a phone show a notification.*

The server decides when a phone should show a notification. It puts a small flag in a reply the phone is already
getting; the phone then fetches the alerts and shows them as local notifications. No push service (APNs, Firebase)
is involved: this works wherever the phone is already talking to the server, which today means while it is
tracking.

Related: [`crew-out-alerts.md`](crew-out-alerts.md) (the phone-side alerts this replaces),
[`go-and-groups.md`](go-and-groups.md) (why group members look like crew), [`satellite-alerts.md`](satellite-alerts.md)
(the other local notifications), [`casual-tracking.md`](casual-tracking.md).

Per `AGENTS.md`, build and test verification is left to CI. Tick boxes as work lands: `- [ ]` → `- [x]`.

---

## The bug

On 25 November an iPhone on a Go run got **"DW: MR has joined"** for someone who isn't crew. MR had been removed
from crew before a group was created, then joined that group.

- For `groups` accounts the server adds the members of your active groups to the same named list as your crew
  (`GetCasualLive` in `server/Stores/SessionStore.Casual.cs`, `includeGroups`). In the Go reply they look exactly
  like crew: nothing says why someone is named.
- Both phones work out crew-out alerts themselves, by comparing one Go reply's named people with the last
  (`CrewOutAlerts.swift` on iOS; the same on Android, merged from `crew-out-alerts-android`). Anyone named who isn't
  you and isn't in a named session counts as crew.
- So every group member who starts a Go run sets off a "has joined" alert. Both apps have this.

Marking each person as crew or group on the phone would fix this one alert, but each new kind of alert would then
be more comparison logic written twice, once per app. Deciding on the server fixes it once and covers later
alerts.

## How it works

1. **Something happens on the server** that someone should hear about, for example a crew member starts a Go run.
   The server stores an alert for each person who should get it: `id`, `kind`, `title`, `body`, `createdAt`,
   `expiresAt`.
2. **The next reply that person gets carries a flag**: `"alerts": 1` (the number waiting) at the top level of the
   reply. It is left out when nothing is waiting, so a normal reply doesn't grow at all.
3. **The phone fetches them**: `POST /me/alerts/take` returns the waiting alerts and removes them on the server.
4. **The phone shows each one** as a local notification, using the server's `title` and `body` and the alert `id`
   as the notification identifier, so fetching the same alert twice can't show it twice. `kind` decides what a
   tap does (for `crew_joined`, open the Go map). An unknown `kind` is still shown; tapping just opens the app.

Because the server writes the text, a new kind of alert needs no app update unless its tap needs to do something
new.

**Where the flag goes:** to start with, the reply to the Go `POST /location`, which a tracking phone gets every few
seconds to minutes, locked or not. The same field can later be added to other replies the phone polls (`GET
/casual/live`, `GET /me`) without changing the phones' handling.

## First alert: crew joined

**Rule:** when someone starts a Go run, each of their crew who is **on a Go run at that moment** gets "DW: MR has
joined" / "Started a Go run". Not group members. Not someone who starts after MR is already out (as today, people
already out when you start don't alert). The usual visibility rules apply: blocked people never, and nobody whose
or whose viewer's crew visibility is off.

- **"Starts a Go run"** is the first position of a new casual run (`AddCasualPosition` starting a run), so a phone
  that loses signal and comes back doesn't count as starting again. That replaces the phones' 30-minute cooldown.
- **Several at once** (two crew start within the same few seconds): one alert each. The phone can group them later
  if that's noisy.
- **Expiry:** 10 minutes. If the person's phone doesn't post in that time, the alert is no longer news and is
  dropped.

## Decisions taken

- **Flag then fetch**, not the alerts inside every reply: the reply stays as it is unless something is waiting.
- **Server memory only** (an `AlertBox`, like `CrewFinder` and the rate limiters), capped per person. A deploy or
  restart drops waiting alerts. They are short-lived by nature, so that's acceptable.
- **Taken once.** `take` removes them. If the phone dies between fetching and showing, that alert is lost, which is
  better than repeats.
- **Settings stay on the phone for now.** The phone's existing "crew joined" switch decides whether a fetched
  `crew_joined` alert is shown. (Moving it to the server so it isn't fetched at all is an open question.)
- **Rolled out per account** with a `server-alerts` feature, granted from the admin Users table. A phone whose
  account has it switches its own comparison off and uses server alerts; without it, nothing changes. That also
  stops a phone showing the same alert twice, once from each path.

## Compatibility

- Old phones ignore the new field and keep their own (buggy) alerts. The fix reaches people as they update.
- Old servers never send the field, and the feature is never granted, so new phones keep their own alerts there.
- When the feature has been on for everyone for a release or two, remove the phone-side comparison
  (`CrewOutTracker` and its Android twin).

## API (sketch)

- Go `POST /location` reply: optional top-level `alerts` (number waiting). Absent when 0.
- `POST /me/alerts/take` → `{ alerts: [{ id, kind, title, body, createdAt }] }`, oldest first, then removed.
- Kinds start with `crew_joined`. Later kinds are added to the docs as they're built.

Details go in `docs/api-reference.html` with the server PR.

## Other alerts this would allow (not scheduled)

- **Your group ends in 10 minutes** (organiser and members).
- **Someone joined your group** (organiser).
- **The organiser moved the meeting point** (Ping) while you're out.
- **A new message pin** near you ([map message pins](session-map-pins.md)).
- **Someone accepted you** in Find new crew, when the sheet isn't on screen.

Each would be a producer on the server plus, at most, a tap action on the phones.

## Steps

### Step 1 — Server

- [ ] **1a.** `AlertBox` (memory, per-person cap, 10-minute expiry, `take`), with tests.
- [ ] **1b.** `crew_joined` producer on a new casual run: crew only, on a Go run now, visibility and blocking
      rules; tests for a group member (no alert: the bug), crew (alert), someone who starts later (no alert), a
      signal drop-out (no alert).
- [ ] **1c.** `alerts` count on the Go `POST /location` reply; `POST /me/alerts/take`; `server-alerts` feature
      grantable from the admin page; API docs.

### Step 2 — iOS

- [ ] **2a.** When the reply has `alerts`, call `take` and show each as a local notification (identifier = alert
      id, `crew_joined` tap opens the Go map, the crew-joined switch filters it).
- [ ] **2b.** With `server-alerts` on the account, skip `CrewOutAlerts.handle`.

### Step 3 — Android

- [ ] **3a-3b.** As 2a-2b.

### Step 4 — Test on two phones

- [ ] Crew starts a Go run while you're on one: one alert. A group member who isn't crew: none (the bug). Crew
      loses signal and comes back: none. Account without the feature: today's behaviour. Phone locked: alert still
      shows.

## Open questions

1. **Settings on the server?** Should the "crew joined" switch move to the account, so the server doesn't create
   alerts the phone will throw away (and so it follows you to a new phone)?
2. **Satellite:** on a satellite connection each request is costly. Should the reply carry the alerts themselves
   (not just the count) when the phone says it's on satellite, to save the extra fetch?
3. **Outside Go runs:** add the flag to `GET /casual/live` and `GET /me` from the start, or wait for an alert that
   needs it?
4. **Real push later?** Alerts while you're not tracking (for example "your crew is out" while you're at home)
   need APNs and Firebase. This design keeps the same alert shape, so push could deliver the same alerts later.
