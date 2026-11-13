# Audio cues while tracking with the phone locked

Branches: `audio-cues-ios`, then `audio-cues-android`. Client-only for the first events; the
"new pin nearby" cue needs one small server change (see Events).

## Why

A runner tracking with the phone in a pocket or armband can't look at the map. Things worth knowing
mid-run ("Sam is 200 m behind", "5 minutes of sharing left", "on satellite") should be heard, through
headphones or the speaker, without unlocking.

Today the only signal is the satellite alert: a local notification with the default sound. That is
muted by the silent switch and Focus modes, so it isn't reliable for a runner.

## Decisions

- **Spoken cues, not sound files.** `AVSpeechSynthesizer` on iOS and `TextToSpeech` on Android. No
  audio assets to ship, and a cue can say who and how far. A short chime before the speech can come
  later.
- **Plays over music.** The other app's audio ducks (gets quieter) for the cue and comes back after.
- **Ignores the silent switch** (iOS `.playback` category), as a run app is expected to.
- **Only while tracking.** No cues when the person isn't out: there is nothing to hear about, and the
  app isn't running in the background then anyway.
- **Off by default, one switch plus a switch per cue type**, in a new "Audio cues" section of Settings.
- **Rate-limited per cue type** so a dot bouncing across a threshold, or a flapping satellite link,
  can't spam the runner.
- **Not push notifications.** "Your crew just started a run" while *you* aren't tracking needs server
  push (APNs/FCM); see Out of scope.

## What can trigger a cue with the phone locked

With the screen locked, the app is alive only because of the location background mode, and it only
hears from the server through its own `POST /location` (every `interval` seconds). So a cue must come
from either:

1. **Something on the phone:** the network path (satellite), the battery, the tracking timer.
2. **The reply to this device's own post:** the session reply carries every runner's latest position;
   the Go reply is a `CasualLiveResponse` (crew positions, and pins once decoded, see below).

GET polling (`loadLive`, `loadSessionPins`) only runs with the app on screen, so it can't drive cues.

## Events (first set)

| Cue | Example | Source | Notes |
|---|---|---|---|
| Satellite | "On satellite. Updates slowed down." / "Back on mobile signal." | `handlePathChange` | Same 5 min cooldown as the alert. |
| Time left | "5 minutes of sharing left." | tracking timer (`trackingExpiresAt`) | Once at 5 min; again at 1 min. |
| Battery paused | "Battery critical. Updates paused." | `pausedForBattery` | Once per pause. |
| Crew distance | "Sam is 200 metres behind." | positions in the post reply | Only crew/session members, never grey dots. Thresholds below. |
| New pin nearby | "New pin from Sam, 300 metres ahead: water here." | pins in the post reply | Needs the server change below. |

**Crew distance** is the open design question (see Open questions). Proposed first cut: speak when a
named runner first comes within 100 m of you, and when someone you were near drops beyond 500 m. "Ahead"
and "behind" need a direction of travel; use the phone's course, else just say the distance.

**Server change for pins:** the Go post reply already is a `CasualLiveResponse`, and once the
`map-pins` branch lands ([session-map-pins](session-map-pins.md)) it carries `pins`, so iOS only needs
to decode it. The session
post reply has no pins: add an optional `pins` field to it (additive). Then diff pin ids against a
seen set (persisted, so a relaunch doesn't re-announce) and speak new ones within ~1 km that aren't
mine. This is the same work as the plan's "local notifications for new pins".

## iOS

- `AudioCuePlayer` (new file): owns one `AVSpeechSynthesizer` and a queue.
  - Before speaking: `AVAudioSession.setCategory(.playback, mode: .voicePrompt, options:
    [.duckOthers, .interruptSpokenAudioAndMixWithOthers])`, then `setActive(true)`.
  - After the last utterance finishes (`AVSpeechSynthesizerDelegate`): `setActive(false, options:
    .notifyOthersOnDeactivation)` so the music comes back to full volume.
  - Drops a cue that would play more than ~10 s late (stale by then), and caps the queue at 3.
  - Per-type cooldowns live here, keyed by cue type.
- `Info.plist`: add `audio` to `UIBackgroundModes` (next to `location`). Location alone probably keeps
  the app awake enough, but `audio` makes playback while locked reliable and is the honest declaration.
  App Review notes: "Spoken run updates (distance to crew, time left) play while the screen is locked."
  This is the same use Strava and Nike Run Club make of it.
- `LocationManager`: call the player from the existing places (path change, timer, battery pause) and
  from the post-reply handling (crew distance, pins).
- Settings (`AuthSheet`): "Audio cues" section: master switch, a switch per cue type, and "Play a test
  cue". The Developer section gets "Speak test cue in 5 s" so it can be heard with the phone locked.
- Respect the user: no cue during a phone call (`AVAudioSession.secondaryAudioShouldBeSilencedHint` /
  interruption notifications), and none while VoiceOver is speaking.

## Android

- `AudioCuePlayer` in the `location` package, used from `LocationTrackingService` (the foreground
  service keeps running while locked, so no new permission).
- `TextToSpeech` initialised once with the service; utterances use `AudioAttributes` with
  `USAGE_ASSISTANCE_NAVIGATION_GUIDANCE` and `CONTENT_TYPE_SPEECH`, which is what turn-by-turn apps
  use and what ducks music correctly.
- Request `AudioFocusRequest` with `AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK` before speaking; abandon it in
  `UtteranceProgressListener.onDone`.
- Settings screen gets the same switches as iOS.

## Steps

### iOS (`audio-cues-ios`)

- [ ] `AudioCuePlayer` with session handling, queue, cooldowns; Developer "Speak test cue in 5 s".
- [ ] `audio` background mode; App Review note in the 1.x submission checklist.
- [ ] Settings: Audio cues section (off by default).
- [ ] Cues: satellite, time left, battery paused.
- [ ] Cues: crew distance (after the open question is settled).
- [ ] Cue: new pin nearby (with the server change).
- [ ] Device check, phone locked, with music playing through headphones: each cue is heard, music
      ducks and comes back, silent switch on still speaks, no cue during a call.

### Server

- [ ] Optional `pins` on the session `POST /location` reply (additive; Go reply already has them).

### Android (`audio-cues-android`)

- [ ] Same as iOS, in `LocationTrackingService`.

## Out of scope

- **Cues when not tracking** (for example "Sam just started a Go run"). Needs server push: APNs/FCM
  device tokens on the server, a sender, and a trigger when a run or session starts. Its own plan.
- Recorded sound files or a chime picker.
- Distance/pace announcements about yourself ("1 km, 5:30 pace"): useful, but a separate feature.

## Open questions

- Crew distance thresholds: 100 m "joined you" and 500 m "dropped back" as a start? Should they be
  settings?
- Which cue types should default on once the master switch is on? Proposed: all except crew distance,
  which is the chattiest.
- Speak names as the dot shows them (crew nickname or initials)? Initials read badly ("S K is 200
  metres behind"); prefer the nickname, fall back to the initials spelled out.
