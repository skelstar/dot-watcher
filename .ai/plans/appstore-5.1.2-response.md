# App Store Review Response — Guideline 5.1.2(i)

Rejection received 2026-07-24 on DotWatcher submission. Reviewer flagged missing privacy
precautions around showing nearby users' locations on a map.

## Reviewer's original feedback

> **Guideline 5.1.2(i) - Legal - Privacy - Data Use and Sharing**
>
> The app enables the display of nearby users' locations on a map, but does not have the
> required privacy precautions in place.
>
> Next Steps:
> - Since the app shows the user's location to many users on a map, it should be appropriately
>   rated as 18+. If no content descriptors are applicable, you may use the "Override to a
>   Higher Age Rating" option to set the age rating to 18+.
> - Include a privacy policy URL in the App Details page on App Store Connect and ensure that
>   the URL you provide directs users to your privacy policy.
> - Ensure that users have a mechanism to block other users.
> - Request users' permissions to have their location displayed on a map; users must have the
>   option to decline this request.
> - Require users to manually check in each time they wish to have their location displayed on
>   a map; there should be no option to enable automatic check ins.

## Our resolution plan

| # | Requirement | Resolution |
|---|---|---|
| 1 | 18+ age rating | Override to 18+ in App Store Connect. Config only. |
| 2 | Privacy policy URL | Already exists and is live at `https://dot-watcher.skelstar.io/privacy` (`client/src/LegalPage.tsx`) — likely just missing from App Store Connect's App Details field. Add it there. Update the "Sharing and Visibility" section once blocking ships. |
| 3 | Block another user | New block feature, session-scoped (see implementation plan). |
| 4 | Explicit, declinable sharing consent | New in-app consent screen, distinct from OS location prompt. |
| 5 | Manual check-in, no automatic option | Reframe continuous tracking as a bounded, explicitly-started "activity tracking session" with a visible indicator, a manual stop, and an automatic expiry — not indefinite silent background sharing. Add a separate manual check-in path for non-activity "where is everyone right now" use. |

Point 5 is a judgment call, not a bright line — automatic background posting stays in the
product for the duration of an explicitly-started, bounded activity (run/hike/ride), which is
the core feature. We are not implementing an "automatic vs manual" *toggle*, since the
reviewer's note explicitly rules that out ("there should be no option to enable automatic
check ins"). Instead, "automatic" only ever runs inside a session the user just explicitly
started, with a visible on-device indicator that it's running, a defined stop action, and a
hard expiry so it can never become silent indefinite sharing.

## Draft response to reviewer (App Store Connect Resolution Center)

> Thank you for the detailed feedback. We're addressing each point:
>
> 1. **Age rating** — We've set the app's age rating to 18+ using the Override to a Higher Age
>    Rating option, reflecting that the app shows user location to other users on a map.
>
> 2. **Privacy policy** — Our privacy policy is published at https://dot-watcher.skelstar.io/privacy
>    and is now linked in the App Details page in App Store Connect. It describes what location
>    and account data we collect, how it's shared with other users in a session, and how users
>    can request deletion.
>
> 3. **Blocking** — Users can now block another member of a shared session directly from the
>    map or participant list. Blocking immediately hides that user's location from the
>    blocking user, and hides the blocking user's location from the blocked user.
>
> 4. **Consent to share location** — Before a user's location is ever shared with other members
>    of a session, they're shown an explicit in-app prompt asking whether they want their
>    location visible to the other people in that session, separate from the standard iOS
>    location-permission prompt. Users can decline, in which case they can still participate
>    in the session as a viewer without sharing their own position.
>
> 5. **Check-ins, not silent automatic sharing** — We want to clarify how location sharing
>    works in DotWatcher, since we believe it already aligns with the intent of this
>    requirement, though we've tightened it further:
>
>    Location is only ever shared with other users during an **activity tracking session**
>    that a user explicitly starts (e.g. "Start tracking" for a run, hike, or ride) — this is
>    directly analogous to a workout-tracking app recording a live activity. While a tracking
>    session is active:
>    - The device shows a persistent, OS-level indicator (the iOS background-location pill)
>      confirming sharing is live.
>    - The user can stop sharing at any time with a single tap.
>    - Every tracking session now has a maximum duration of 24 hours (to accommodate long
>      races and ultra-distance events), after which sharing automatically stops and must be
>      restarted explicitly.
>
>    There is no mode in which a user's location is shared with others outside of a session
>    they explicitly started, and there is no setting that enables sharing to begin or continue
>    without that explicit action. Outside of an active tracking session, other users only see
>    a user's last manually-shared position — there is no background or periodic sharing at
>    rest.
>
> We believe this satisfies the intent of the guideline (no silent, unbounded, or
> passively-enabled location sharing) while preserving live activity tracking, which is
> central to the app's purpose. We're happy to adjust further if this doesn't fully address
> the concern — please let us know specifically what additional constraint you'd like to see
> (e.g. a shorter max duration, or removing automatic sharing entirely in favor of periodic
> manual check-ins during an activity).

## Open items before resubmission

- [ ] Add the existing privacy policy URL to App Store Connect App Details.
- [ ] Update the "Sharing and Visibility" section of `client/src/LegalPage.tsx` to mention
      blocking once it ships.
- [ ] Confirm final wording once implementation is done — response should describe what's
      actually shipped, not what's planned.
- [ ] Implement first (blocking, consent screen, session expiry), then resubmit with this
      response describing what's actually shipped — replying with unbuilt features risks a
      second rejection if Apple spot-checks the build.
