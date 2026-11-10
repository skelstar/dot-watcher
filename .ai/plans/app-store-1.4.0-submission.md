# App Store submission checklist: DotWatcher 1.4.0

Written for doing this at a Mac later. Nothing here was built or tested; the privacy manifest in particular
is unverified until it has been through an Xcode build.

## Where things stand

- Marketing version in the project is **1.4.0** (last shipped: 1.3.1).
- A privacy manifest was added at `ios/DotWatcher/DotWatcher/PrivacyInfo.xcprivacy`. Before this the app had
  none, which App Store Connect flags at upload because the app uses `UserDefaults`.
- The Xcode project uses a synchronized folder group, so the manifest is bundled automatically. No project edit.
- Already fine: account deletion (Settings > Delete account, plus the web `DeleteAccountPage`), a Blocked Users
  screen, specific location usage strings, `UIBackgroundModes = location`, `ITSAppUsesNonExemptEncryption = false`.
- Sign in with Apple is not required: sign-in is email and password only. It becomes required if Google or another
  social login is added.

## What the manifest declares

- Tracking: none, no tracking domains.
- Required-reason API: `UserDefaults`, reason `CA92.1` (the app's own data). Used in `CasualStore.swift` and `CrewStore.swift`.
- Collected data, all linked to the user, not used for tracking, purpose App Functionality:
  Precise Location, Email Address, Name (the two-letter display initials), User ID.

This list came from reading the code and the sign-in form. If the server or the Android client collects more
(crash logs, a device ID), add it here and to the App Store Connect labels.

## On the Mac

1. Check out the branch with the manifest merged (or this branch).
2. Open `ios/DotWatcher/DotWatcher.xcodeproj` in the current Xcode (it must use the current iOS SDK; Apple requires this for new uploads).
3. Confirm `PrivacyInfo.xcprivacy` shows in the file navigator and is ticked for the DotWatcher target
   (Target Membership). If it is not, tick it.
4. Optional: Product > Archive, then in the Organizer choose "Generate Privacy Report" and compare it with the
   labels below.
5. Deployment target: the app target is set to 17.6 (the project-level 26.5 is overridden). Decide whether iOS 17
   still needs supporting. iOS 17 shipped 18 Sept 2023. Raising it is optional, not required for review.
6. Product > Archive > Distribute App > App Store Connect. Wait for processing.

## In App Store Connect

1. **App Privacy** (left sidebar): the data types must be exactly Precise Location, Email Address, Name, User ID.
   For each: Linked to the user = Yes, Used for tracking = No, purpose = App Functionality.
   Remove anything not collected. Click Publish if changed.
2. **Privacy Policy URL**: open it, check it loads and matches what is collected. Check the support URL too
   (App Information).
3. **Age Rating** (App Information > Age Rating > Edit): redo the questionnaire. Apple revised it and an
   outdated one can block submission.
4. **New version**: Distribution page, + next to iOS App, enter 1.4.0. Fill in What's New. Update screenshots only if the UI changed.
5. **Build**: under Build, + and pick the processed build.
6. **App Review Information**: give a working test account (username and password) because the app needs sign-in.
   Suggested note:

   > DotWatcher shares a runner's live location with their group during a run. Background location is the core
   > feature, because the phone is usually locked while running. Tracking only starts when the user taps Go or
   > joins a session, and they can stop at any time.

   A short demo video helps.
7. **Export compliance**: should be skipped because `ITSAppUsesNonExemptEncryption` is false. If asked, answer no non-exempt encryption.
8. **Add for Review**, then Submit to App Review. Choose manual or automatic release.

## If it is rejected

The rejection message names the guideline number. Likely candidates: background location justification (4.5.4 /
5.1.1), missing or wrong privacy manifest entries, missing demo account. Reply in Resolution Center with the
explanation from the review notes above.
