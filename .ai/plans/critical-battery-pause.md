# Pause automatic tracking at critical battery — Plan

*DotWatcher — plan, September 2026*

## Context

Follow-on from `.ai/plans/battery-level-reporting.md` (self-reported battery level, persisted, with
a low/critical badge for other session members). Once battery is visible, the natural next question
was raised in discussion: can DotWatcher actually *do* something about a runner's battery running
out mid-run, not just warn other people about it?

Two ideas were weighed.

## Rejected: slow the post cadence at low battery (e.g. every 5 minutes)

Doesn't meaningfully help. Continuous `CLLocationManager` tracking — not the network — is this
app's dominant battery cost, already established in the codebase's own history:

- `cabb1a8`: *"GPS is the dominant battery drain (~30-100mW continuous), the marginal cost of one
  small HTTP POST every 15s is negligible by comparison."*
- `satelite-connectivity.md`: *"GPS acquisition cost is unaffected by which network path the upload
  eventually takes."*

Slowing the POST interval alone doesn't touch GPS, so it barely moves the real cost — while directly
hurting position freshness at the exact moment (phone about to die) that freshness matters most. A
stale position right before a phone goes dark is a worse outcome for a safety-adjacent tracking app
than one that's fresh right up to the end.

## Accepted: stop tracking entirely at critical battery, runner-controlled

Targets the actual dominant cost — `clManager.stopUpdatingLocation()`, not just skipping POSTs —
with the runner always in control, never a silent behavior change.

### Trigger

`batteryLevel <= criticalBatteryThreshold` (10 — same number as the client's
`CRITICAL_BATTERY_THRESHOLD` in `useSessionTimelineLogic.ts`; kept in sync manually, no shared
source between the two codebases) **and** not currently charging (`UIDevice.batteryState`) **and**
not already overridden this tracking session.

### On trigger

- Stop `CLLocationManager` updates and skip the automatic post — this is what actually saves power,
  not just silence on the wire.
- Show a **persistent banner** (`ContentView.runnerRow`), not a transient toast or a silent switch.
  Matches this app's existing rule that any state change affecting what other people see must be
  surfaced, not hidden (same reasoning as the satellite status badge). Decided explicitly against a
  silent auto-switch or a dismissible one-time prompt — the runner should always be able to look at
  the screen and know what's currently happening.

### Two explicit choices, no default action taken on the runner's behalf

- **Send location now** — a one-shot `CLLocationManager.requestLocation()` + a single POST. Does
  *not* resume automatic tracking; it's one position, not a mode change. Wires up
  `oneShotLocationContinuation`, scaffolding (`private var oneShotLocationContinuation:
  CheckedContinuation<CLLocation?, Never>?`) that already existed in `LocationManager.swift`,
  declared and resumed in the `CLLocationManagerDelegate` callbacks, but had no caller anywhere
  until this feature needed one.
- **Turn on automatic updates** — explicit override. Resumes `startUpdatingLocation()` and the
  normal cadence, and is sticky (`batteryOverrideAcknowledged`) for the rest of this tracking
  session — an explicit choice, once made, shouldn't need repeating every 15s tick. Reset on the
  next `start()`, so a fresh tracking session isn't silently pre-overridden by a previous one.

### Auto-resume on charging (an addition beyond what was asked for)

If `UIDevice.batteryState` reports `.charging`/`.full`, tracking resumes automatically even without
the runner tapping anything — the reason for pausing no longer applies. Included because it's a
strict improvement with no downside: charging is an unambiguous, immediate signal, and it never
overrides an explicit "keep it off" choice in the other direction (there is no such choice offered —
the runner can only ever choose to *turn tracking on*, not to force it to stay off while charging).

### No server/protocol changes

Other session members don't need a new field or signal. Once posts stop, the existing gap/missing
detection (`findRunnersWithGap` in `useSessionTimelineLogic.ts`) kicks in on schedule, and the
runner's last known position still carries its persisted `batteryLevel` from
`battery-level-reporting.md`, so the critical-battery badge is still showing on that last dot.
"Last seen at critical battery, then went quiet" is already the correct, honest story with zero new
server work — a case of one feature's persistence directly paying for a second feature's UX for
free.

## Non-goal: detecting "phone unlocked" directly

The original framing of this idea was "stop posting unless the phone is unlocked." iOS has no API
that tells a backgrounded app the device just unlocked. Foregrounding the app is the closest
equivalent (it does require an unlock) but isn't what this design actually uses — the chosen
behavior is fully explicit (a button tap in a banner the runner sees when they do open the app), not
something that fires automatically the moment the app happens to come to the foreground. That
distinction was itself a decision point: the alternative (auto-resume-on-foreground, no banner) was
considered and rejected in favor of always defaulting to *stopped*, with the runner's next action
being an explicit choice rather than an invisible resume.

## Known limitations / open questions

- **`batteryOverrideAcknowledged` doesn't persist across an app relaunch.** It's an in-memory flag,
  reset by `start()`. If the app is killed and relaunched mid-tracking-session while paused, the
  override (if the runner had set one) is lost and the pause logic re-evaluates from scratch on the
  next capture. Not expected to matter in practice — the OS killing a foregrounded-recently app
  mid-run is rare — but worth knowing if a bug report ever traces back to this.
- **Threshold is not configurable** — hardcoded to match the client's badge threshold (10%). No UI
  to change it; would need a paired change in two codebases (iOS + `useSessionTimelineLogic.ts`) if
  it ever needs to move.
- **No equivalent on Android.** Android's `LocationUpdate` doesn't yet send `batteryLevel` at all
  (see `battery-level-reporting.md`'s non-goals), so this entire feature is iOS-only for now.

## Implementation status (2026-09-28)

Shipped on branch `add-battery-level-reporting`, commit `a268d0f`:

- `LocationManager.swift`: `criticalBatteryThreshold`, `pausedForBattery`,
  `batteryOverrideAcknowledged`; pause/resume decision folded into the top of `captureAndPost()`;
  `resumeAutomaticTrackingOverridingBattery()`, `sendLocationNowWhilePaused()`,
  `requestOneShotLocation()`, `currentBatteryPercentage()` added; `start()`/`stop()` reset the new
  state.
- `ContentView.swift`: `runnerRow`'s countdown-ring branch excludes `pausedForBattery` (a countdown
  to a post that isn't coming would be actively misleading); new `batteryPausedBanner` with the two
  buttons described above.
- No server, client (web), or Android changes.

**Not build-verified.** This session has no macOS/Xcode toolchain (Windows), and there's no iOS CI
in this repo (`server/README.md`'s CI section: *"iOS build, simulator, TestFlight, Keychain,
background-location, and real-device GPS behavior are manually verified outside GitHub Actions for
now"*) — so `build-ios-simulator` couldn't run here either (needs `xcodebuild`/`xcrun`, neither
present). This compiles only by careful manual review of the diff, not an actual build. **Verify
with `build-ios-simulator` or Xcode directly before merging.**
