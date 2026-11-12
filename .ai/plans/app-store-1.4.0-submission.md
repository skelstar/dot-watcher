# App Store submission checklist: DotWatcher 1.4.0

Status as of 12 Nov 2026. Paused part way through. Nothing here was built or tested locally; CI verifies the PR,
and the privacy manifest is only truly checked by an Xcode archive.

## Done

- [x] Privacy manifest `ios/DotWatcher/DotWatcher/PrivacyInfo.xcprivacy` added (9 Nov), then extended on branch
      `app-store-privacy-labels` with Other User Content (GPX routes) and Other Diagnostic Data (battery level,
      satellite flag).
- [x] Privacy policy (`client/src/LegalPage.tsx`) updated on the same branch: email, crew and "Who can see me",
      device status, GPX routes, who sees what on Go runs, watch links. Last updated 12 Nov 2026.
- [x] App Store Connect **App Privacy** published. Six data types, all linked to the user, no tracking,
      App Functionality: Precise Location, Email Address (fixed: was "not linked"), Name, User ID,
      Other User Content, Other Diagnostic Data.
- [x] Version **1.4.0** created in App Store Connect (Prepare for Submission).

## Still to do

1. [ ] **Merge `app-store-privacy-labels`** into staging (open the PR, let CI pass), then deploy so
       https://dot-watcher.skelstar.io/privacy shows the new text. Re-read the new paragraphs to confirm they
       describe the app correctly.
2. [ ] **Support URL** on the 1.4.0 version page is currently `https://dot-watcher.skelstar.io/privacy`. Point it
       at a real support/contact page if there is one (or add one); a privacy page as support URL can be flagged.
3. [ ] **Age rating**: currently **18+** (A18 Brazil, 19+ Korea), far higher than a running app needs. Now that
       1.4.0 exists, Edit should be available (App Information > Age Rating). Click View first to see which answer
       caused 18+; likely "Unrestricted web access" or a user-generated content / messaging question. Location
       sharing with chosen people plus blocking should not need 18+.
4. [ ] **New build on the Mac** (none of the uploaded builds 883/899/916 can be used: they predate the manifest
       change, and they are greyed out in Add Build):
       - Pull staging with the merged branch, open the project in current Xcode.
       - Confirm `PrivacyInfo.xcprivacy` has Target Membership for DotWatcher.
       - Product > Archive > Distribute App > **App Store Connect** (not "TestFlight Internal Only", which can
         never be submitted, and is the likely reason the old builds are greyed out).
       - Optional: Organizer > Generate Privacy Report, compare with the six labels above.
       - If `MARKETING_VERSION` gets bumped (e.g. 1.4.1), rename the version in App Store Connect to match;
         it stays editable until submission.
       - Archive may complain about the managed `carrier-constrained` (satellite) entitlement if the provisioning
         profile lacks it.
       - Optional cleanup: remove the `jakkuu.local` ATS exception from Info.plist for release.
5. [ ] **Attach the build**: Build > + on the 1.4.0 page. If the new build is greyed out, check its status in the
       TestFlight tab (Missing Compliance / processing / internal-only).
6. [ ] **What's New**, draft:
       > New topographic map of New Zealand. Add a GPX route to your run or session. Choose how big the dots are
       > on the map. Clearer "Who can see me" controls before you start, including going fully private. Smoother
       > map following and a faster hold-to-stop.
7. [ ] **Screenshots**: update; the map changed a lot since 1.3.1 (LINZ basemap, dot sizes, "Who can see me").
8. [ ] **App Review Information**:
       - Working demo account (email + password). Ideally a second account or bot running a session the reviewer
         can join, with instructions, so they see more than their own dot.
       - Notes:
         > DotWatcher shares a runner's live location with their group during a run. Background location is the
         > core feature, because the phone is usually locked while running. Tracking only starts when the user
         > taps Go or joins a session, and they can stop at any time. Other people see you only according to the
         > "Who can see me" settings; users can be blocked (Settings > Blocked Users) and accounts deleted
         > (Settings > Delete account).
       - Attach a ~30 s screen recording of Go > run > stop (helps with guideline 2.5.4).
9. [ ] **Export compliance** should skip itself (`ITSAppUsesNonExemptEncryption` = false). If asked: no
       non-exempt encryption.
10. [ ] **Add for Review > Submit**. Choose manual or automatic release.

## If it is rejected

The message names the guideline. Likely candidates: background location justification (2.5.4 / 5.1.1),
privacy manifest or label mismatch, missing demo account. Reply in Resolution Center using the review notes above.
