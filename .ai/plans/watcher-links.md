# Watcher links: a permanent link to follow someone's Go runs

Status: plan only, nothing built. Decisions below are proposals until the open questions are answered.

## The ask

"Could I give someone a permanent link to my Go session? Maybe it would work only if they logged in?"

Someone (a partner, a parent, a coach) should be able to follow every Go run without being sent a new link
each time.

## What already exists

- **Watch links** (`POST /casual/watch-link`, `GET /watch/{token}`, web `WatchPage.tsx`): a secret link to a
  web map of one runner. No account needed. Each link belongs to **one run** and stops working when the run
  ends; "Turn off links" (`DELETE /casual/watch-link`) ends them sooner. The token is stored hashed
  (`casual_watch_links.token_hash`) and shown once.
- **Crew** (`CrewController`): mutual, per account, added by a one-time 8-character code (24 h, first redeemer
  wins). Crew see each other by name on Go whenever they're out. Someone not tracking can still watch their crew
  in the app (`GET /casual/live` with no position returns crew only). So for two people who both have the app,
  crew already gives a permanent "follow". The catch is that it's two-way.
- **Web sign-in** (`SignInPanel.tsx`, `AccountButton.tsx`): the web map already has an optional sign-in and
  account creation, opened from the account button. Anonymous viewing is unchanged. So "only works if they
  log in" doesn't need new sign-in work on the web.
- **Groups** (in progress, `.ai/plans/go-and-groups.md`): people in the same active named session see each other
  by name on Go. They're temporary, until the group ends, so they don't cover this ask.

## Proposal: one-way watchers

A **watcher** is an account that can see your Go runs by name, while you don't see theirs. It's crew with one
direction removed.

1. **You make a watcher link** in "Who can see me" → **Add a watcher**. It's a link with a one-time code, shared
   the same way as a crew code (share sheet, QR).
2. **They open it and sign in** (in the app, or on the web map with the existing sign-in). The first signed-in
   account to claim it becomes your watcher. After that the link is used up, so forwarding it does nothing.
3. **From then on, whenever you're out on Go**, they see you by name:
   - in the app, the same way crew show (on their Go map, and on the start screen while they aren't out);
   - on the web, at a permanent page such as `/following`, which lists the people they watch who are out now.
4. **Between runs they see nothing:** "Mia isn't out right now". There's no last-known position, so a watcher
   never has a standing view of where you are.
5. **You can see and remove watchers** in "Who can see me", with a remove button per person and the date each
   one was added. Removing someone is immediate. They can also stop watching from their side.

Why claim-by-account rather than a permanent secret URL:

- A permanent URL that anyone can open is a standing view of your location that leaks once it's forwarded,
  screenshotted or left in a chat. Tying it to one account means a leaked link is worthless once claimed.
- It reuses the crew-code machinery (one-time code, 24 h expiry, first redeemer wins, confirm screen, blocks).
- Removing a watcher is per person, so you never have to "turn off links" for everyone at once.

### Privacy rules (same as crew unless an open question says otherwise)

- Blocking works both ways and removes the watcher relationship.
- "My crew" off (`VisibleToCrew = false`) hides you from crew. **Open question 3:** does it hide you from watchers
  too, or do watchers get their own switch?
- Watchers see your name (nickname, else initials), your position, and your route if you loaded one. They don't
  get battery or speed, matching crew.
- Being a watcher doesn't make you visible: a watcher who goes out on Go isn't shown to the person they watch,
  unless the two are also crew.
- Activity log: "Watched by Sam" events, like today's "Someone watched" on watch links, so you can see it
  being used.

## Rough shape of the work

**Server**
- `watchers` table: `(owner_user_id, watcher_user_id, created_at)`, PK on the pair, FKs cascade (Staging and
  Production share the database, see crew).
- `watcher_codes` table, like `crew_codes`: one-time, 24 h, hashed or plain like crew codes. Decide while
  building.
- Endpoints: create, list and revoke codes; look up a code (for "Watch Mia's runs?"); redeem; list my watchers and
  remove one; list who I watch and stop.
- `CasualVisibility.ForViewer`: watchers are added to the viewer's named set, the same way groups add co-members
  (`crewLabels`), but only in the owner→watcher direction.
- A feature flag (`watchers`), off for everyone until granted, like `groups`.

**iOS and Android**
- "Who can see me": a Watchers section (list, remove, Add a watcher → share link/QR).
- A universal link / app link for the watcher code, handled like `/crew/{code}`.
- Watched people appear wherever crew appear. Perhaps with a small marker so you can tell them apart from crew
  (open question 4).

**Web**
- `/watcher/{code}`: sign in (existing panel), confirm, claim.
- `/following`: the signed-in viewer's watched people who are out now, on the map. Polls like `WatchPage`.

**Tests**: claiming (first wins, expired, used, blocked, own code), one-way visibility, removal from each side,
watchers hidden when the owner isn't out, the feature flag.

## Open questions

1. **Is one-way needed, or is crew enough?** If the people you'd give this to have the app and don't mind you
   seeing them too, crew already does it, with nothing to build. Who is this really for: someone without the app,
   or someone you don't want to see?
2. **App, web, or both for watchers?** Web-only is lighter (no app changes for the watcher side, only the owner's
   "Who can see me"). App-only skips `/following`. The proposal assumes both.
3. **Which switch hides you from watchers?** Reuse "My crew" (simple, but then you can't hide from crew while
   still showing a parent), or a separate "Watchers" switch in "Who can see me".
4. **How do watched people look in the watcher's app?** Exactly like crew, or with a marker (for example an eye)
   so the watcher knows they're one-way?
5. **Limits.** How many watchers per person (crew has a cap; reuse it?), and how many live watcher codes at once
   (crew keeps three)?
6. **Notifications.** Should a watcher get "Mia is out" when you start a run? That's the same idea as crew-out
   alerts, but for someone who isn't tracking, so it needs push (APNs/FCM), which the app doesn't have yet.
   The proposal leaves it out.
7. **Named sessions and groups.** When you're in a named session or a group, do watchers see you (with the
   padlock, as crew do today), or only on plain Go runs?
8. **Existing watch links.** Keep today's one-run links alongside watchers (for a one-off "watch my race"), or
   fold them in? The proposal keeps both.
9. **Accounts for watchers.** Signing in on the web needs an account. Is asking a parent or partner to create
   one acceptable, or does that defeat the point (back to question 1)?

## Checklist

- [ ] Answer the open questions
- [ ] Server: tables, endpoints, visibility, feature flag, tests
- [ ] iOS: Watchers in "Who can see me", code link, watched people on the map
- [ ] Android: the same
- [ ] Web: `/watcher/{code}` claim, `/following`
- [ ] Docs: API reference, schema, in-app help
