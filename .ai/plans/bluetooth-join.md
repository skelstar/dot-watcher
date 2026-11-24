# Bluetooth join — Plan

*DotWatcher — plan, 25 November 2026. Status: nothing built. Step 0 (a two-phone spike) decides whether the rest
goes ahead.*

Joining crew, or joining an event or group, by holding two phones near each other, using Bluetooth Low Energy (BLE)
with no pairing. Both apps are open on screen the whole time; nothing here runs in the background.

Related: [`crew-finding.md`](crew-finding.md) ("Find new crew", which this extends),
[`nearby-join.md`](nearby-join.md) (joining a session near its organiser), [`go-and-groups.md`](go-and-groups.md)
(joining is a group join for `groups` accounts), [`crew-management.md`](crew-management.md) (codes).

Per `AGENTS.md`, build and test verification is left to CI. Tick boxes as work lands: `- [ ]` → `- [x]`.

---

## Why

Both ways of joining in person today use GPS, and GPS is the weak part:

- **Find new crew** lists people who also have it on within 50 m, by their latest fix. Indoors, in a car park or
  under trees a fix can be 30-50 m out, so two people standing together may not see each other, and people across
  the car park may.
- **Nearby join** needs the joiner within the organiser's radius (50-500 m) of the Ping location. That's right for
  "everyone at the start line", but it can't say "the person in front of me".
- Both need a recent fix, which can take a while indoors or just after opening the app.

Bluetooth answers a different question: **is this phone within a few metres of mine, right now?** That's what
"add the person next to me" and "join the organiser I'm standing with" actually mean.

## How it works

One phone **advertises** a short random token, the other **scans** for it, and the server turns a seen token into
a name or an event. Tokens are issued by the server, are short-lived and say nothing by themselves. Phones never
send names, account ids or positions over Bluetooth.

**Crew** (both people have Find new crew on; symmetric):

1. Turning on Find new crew returns the caller's own token as well as the list (the token `CrewFinder` already
   makes for the window).
2. While the crew sheet is open, each phone advertises its token and scans for others.
3. Each phone reports the tokens it sees (with signal strength) to the server every few seconds.
4. The server treats a sighting as "nearby" for **both** people, so it's enough for one phone to see the other.
   That covers a phone that can't advertise, and an iPhone whose advertisement Android doesn't pick up.
5. Bluetooth-seen people join the list alongside GPS-near people. Accepting is unchanged: both must accept.

**Event or group** (organiser and joiner; one-way):

1. The organiser opens **Let people join by phone** on their event's card. While that screen is open, the phone
   asks the server for a beacon token (rotated every 60 s) and advertises it.
2. The joiner taps **Join someone next to you** on the start screen. Their phone scans and sends each token it sees
   to the server, which answers with the event's title, organiser, end time and whether it needs a code.
3. The joiner gets the existing join sheet (consent wording, end time, code field) and joins with the token in place
   of a position. For `groups` accounts this is a group join, as nearby and invite-code joins already are.

**What goes over the air:** a fixed DotWatcher service ID, so scanners ignore other devices, plus the token. iOS
won't put custom data in an advertisement, so the token is either in the advertised local name or read from a GATT
characteristic over a short unencrypted connection (no pairing either way). The spike picks one.

**"Next to you":** a sighting only counts above a signal-strength threshold, aiming at about 2-3 m. Phones,
cases and pockets vary a lot, so the spike measures it and the app tells people to hold the phones close.

## Decisions taken

- **Foreground only.** The crew sheet, or the join and "join by phone" screens, must be open. No background
  scanning or advertising, so none of iOS's background limits apply.
- **Adds to GPS, doesn't replace it.** Find new crew keeps its 50 m GPS test; Bluetooth is a second way to appear.
  Nearby join keeps its radius; Bluetooth is a separate way in.
- **Server-issued tokens only.** 8 random bytes, valid for the crew window (2 minutes) or 60 s for a beacon.
  Unknown, expired and wrong tokens all get the same 404, as nearby join does.
- **Names only.** As with Find new crew: no distance, direction or signal strength is ever shown to anyone.
- **Bluetooth permission asked at first use**, not at launch: on iOS when the crew sheet's Find new crew is turned
  on or a Bluetooth join screen opens; on Android the same, as "Nearby devices".
- **Behind a per-account `bluetooth` feature**, granted from the admin Users table like `groups`, until it has been
  tested on enough phones.

## Where it gets harder

| Risk | Answer |
| --- | --- |
| Signal strength varies by phone, case and grip | Threshold tuned in the spike. Generous is fine for crew (both must accept anyway); events lean on the 60 s token and the entry code. |
| Someone shares a beacon token remotely (relays or reads it out) | It expires within 60 s, joins are rate-limited, the organiser's entry code still applies, and the organiser can remove people. |
| Some Android phones can't advertise | Crew works if either phone sees the other (step 4 above). An organiser whose phone can't advertise is told so and uses Ping or the invite code. |
| Android 8-11 (`minSdk` 26) need location permission to scan | The app already has it. Android 12+ uses `BLUETOOTH_SCAN` (marked `neverForLocation`), `BLUETOOTH_ADVERTISE` and `BLUETOOTH_CONNECT`. |
| Bluetooth is off | Say so and offer to turn it on (Android) or point to Settings (iOS). The GPS and code routes still work. |
| A busy start line with many phones | Scanners only report DotWatcher's service ID; the server caps reported tokens per call. |
| iOS↔Android quirks (scan-response handling, connection reliability) | That's the main thing step 0 tests, in all four combinations. |

## API (sketch)

Crew, on the existing `/me/crew/finding` endpoints:

- `POST /me/crew/finding` accepts no fix when the client says it has Bluetooth, and returns `myToken`.
- `POST /me/crew/finding/sightings` `{ tokens: [{ token, rssi }] }`: records sightings for this window and returns
  the usual state. Sightings above the threshold make both people list each other.

Events and groups:

- `POST /sessions/{sessionId}/beacon` (organiser only): returns `{ token, expiresAt }`. Called again before expiry
  to rotate.
- `GET /beacons/{token}`: the event's title, organiser name, end time and `needsCode`, while the token is valid.
- `POST /beacons/{token}/join` `{ displayName, entryCode }`: the same response and refusals as
  `POST /nearby/sessions/{id}/join`, with the token standing in for the position check.

Details go in `docs/api-reference.html` with the server PR.

## Steps

### Step 0 — Spike: does it work across phones?

- [ ] **0a.** A throwaway branch (`bluetooth-join-spike`) with a hidden debug screen on each app: advertise a fixed
      service ID plus a token, scan, show what was seen and its signal strength. No server.
- [ ] **0b.** Test all four combinations (iPhone→iPhone, iPhone→Android, Android→iPhone, Android→Android) with
      the apps open: time to first sighting, and signal strength at about 0.1, 1, 3 and 10 m, in hand and in a pocket.
- [ ] **0c.** Decide: token in the local name or in a GATT characteristic; the signal-strength threshold; go or
      no-go. Write the results here.

### Step 1 — Mock-ups

- [ ] **1a.** Crew sheet with Bluetooth: the Bluetooth permission moment, a "Hold your phones close" hint, and
      Bluetooth off.
- [ ] **1b.** Organiser's **Let people join by phone** screen (big "Hold your phone up" state, count of people who
      joined) and joiner's **Join someone next to you** scanning sheet leading into the existing join sheet.

### Step 2 — Server

- [ ] **2a.** Crew: `myToken`, optional fix, `sightings` endpoint, sightings counted both ways in `CrewFinder`;
      tests.
- [ ] **2b.** Events: beacon tokens (memory only, like `CrewFinder`), `GET`/`POST /beacons/{token}`, entry code
      and rate limits shared with nearby join; tests.
- [ ] **2c.** `bluetooth` feature, grantable per account from the admin page. API docs.

### Step 3 — iOS

- [ ] **3a.** A small Bluetooth module (`CoreBluetooth`: peripheral to advertise, central to scan, threshold),
      `NSBluetoothAlwaysUsageDescription`.
- [ ] **3b.** Crew sheet: advertise and scan while Find new crew is on; report sightings.
- [ ] **3c.** Organiser screen and joiner sheet; join through `/beacons/{token}/join`.
- [ ] **3d.** `Help.md`: "Hold phones together" under Crew and under joining.

### Step 4 — Android

- [ ] **4a.** The same module (`BluetoothLeAdvertiser`, `BluetoothLeScanner`), permissions by Android version,
      "can't advertise" handling.
- [ ] **4b-4c.** As 3b-3c.

### Step 5 — Two-phone tests

- [ ] All four phone combinations, for crew and for an event with and without an entry code; Bluetooth off;
      permission refused; a token used after 60 s is refused.

## Open questions

1. **Entry code:** should a Bluetooth join skip the organiser's entry code (being next to the organiser is proof
   enough), or still ask for it? Recommendation: still ask, since the organiser chose it on purpose.
2. **Organiser's screen:** is it OK that the organiser keeps the "join by phone" screen open while people join, or
   should it stay on for a few minutes after they leave it?
3. **Crew without GPS:** if Bluetooth works, should Find new crew still ask for location at all?
4. **Names:** "Join someone next to you", "Let people join by phone" and "Hold your phones close" are working
   wording.
