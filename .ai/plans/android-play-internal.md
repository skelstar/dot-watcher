# Android Play internal testing from GitHub — Plan

*DotWatcher — plan, November 2026. Status: drafted, not started.*

Goal: the Android twin of `ios-testflight.yml`. Run a workflow by hand (or from Kraken, or with an `android-v*` tag),
and a signed release build of that branch lands on the Play **internal testing** track, ready to install from the
Play Store on testers' phones. Nothing is ever sent for production review.

Related: `.github/workflows/ios-testflight.yml` and `docs/ios-testflight.html` (the iOS version this copies),
`.github/workflows/android-gradle.yml` (today's debug-only CI build), `android/app/build.gradle.kts`,
`android/local.properties.example`, `.ai/plans/android-app.md` ("Distribution"), and the Kraken phone builds plan
(`.ai/plans/kraken-phone-builds.md` on branch `kraken-phone-builds-plan`), whose "Later" section names this workflow.

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI; nothing below
is verified locally.

---

## Where things stand

- The app (`nz.skelstar.dotwatcher`) is already on Play internal testing, uploaded by hand. Its first upload was
  done in Play Console, which matters: Google's API can't create an app or do its first upload.
- `build.gradle.kts` reads the LINZ key and the release keystore from `android/local.properties` (gitignored). With
  no keystore settings the release build is quietly **unsigned** rather than failing.
- The version is typed by hand: `versionCode = 29`, `versionName = "1.4.3"`. `android-app.md` says to bump
  `versionCode` for every upload because Play rejects reuse.
- CI (`android-gradle.yml`) only builds the debug APK on pull requests, on `ubuntu-latest`.

## Decisions (proposed; confirm before step 1)

- **Mirror the iOS workflow.** Same triggers (`workflow_dispatch` with optional `version` and `request_id`,
  `android-v*` tags), same `run-name` shape, same "tag the built commit after a dispatched upload" rule, same
  concurrency idea. Kraken then treats both platforms alike.
- **Linux runner.** `ubuntu-latest`: no Mac needed, no capacity queue, free minutes.
- **No app code changes for secrets.** The workflow writes `android/local.properties` from repository secrets,
  exactly as the iOS workflow writes `Secrets.xcconfig`. The keystore is decoded into the runner's temp folder.
- **Version code = commit count**, like the iOS build number. Play needs every upload's `versionCode` to be higher
  than any before it, across all tracks. The commit count is about 980 today, well above 29, so the switch is
  one-way and safe. Gradle works it out itself (`git rev-list --count HEAD`) so local builds and workflow builds
  number the same way, as iOS does. A Gradle property (`-PversionCode=`) overrides it for emergencies.
  - Consequence, same as iOS: uploading a branch with more commits, then `staging` with fewer, is refused. Unlike
    TestFlight, Play doesn't reset numbering per version, so bumping the version doesn't get around it; merging
    the branch (or waiting for more commits) does.
- **Version name**: typed `version` input, else `versionName` from `build.gradle.kts`, passed as
  `-PversionName=`. Nothing is committed, as on iOS.
- **Release status `completed` on the internal track** so testers get it straight away. If Play reports the app is
  still a draft (it has never been through review for any track), the upload must use status `draft` instead and
  be rolled out by hand in Play Console. Check this on the first run (see Open questions).
- **Upload with `r0adkll/upload-google-play`** (pinned to a version), using the service account JSON. The
  alternative, the Gradle Play Publisher plugin, means adding a plugin to the app's build for something only CI
  uses.
- **Fail rather than upload unsigned.** The workflow checks all signing secrets are set before building, and
  checks the bundle is signed with the upload key before uploading.

## Step 0 — one-time setup in Google (you)

About 20–30 minutes, all in the browser.

- [ ] **Google Cloud service account.** console.cloud.google.com → pick or create a project → enable the
      **Google Play Android Developer API** → IAM & Admin → Service Accounts → Create (no roles needed in Cloud) →
      Keys → Add key → JSON. Download the JSON file (only offered once, like Apple's `.p8`).
- [ ] **Give it Play access.** Play Console → **Users and permissions** → **Invite new users** → the service
      account's email (`…@….iam.gserviceaccount.com`) → App permissions → DotWatcher → **Release to testing tracks**
      (and "View app information"). No account-wide permissions.
- [ ] **Check Play App Signing is on** (Play Console → DotWatcher → Test and release → App integrity). If it is,
      the keystore in `local.properties` is only the *upload* key, which Google can reset if it leaks. If it isn't,
      stop and decide before putting the key in GitHub: that key would then be the only app signing key.
- [ ] **Repository secrets** (GitHub → Settings → Secrets and variables → Actions):

      | Secret | Value |
      |---|---|
      | `PLAY_SERVICE_ACCOUNT_JSON` | The whole JSON key file |
      | `ANDROID_UPLOAD_KEYSTORE_B64` | The `.jks` file in base64 (`base64 -i dotwatcher-upload.jks` on the Mac; `[Convert]::ToBase64String([IO.File]::ReadAllBytes("dotwatcher-upload.jks"))` in PowerShell) |
      | `ANDROID_UPLOAD_STORE_PASSWORD` | `RELEASE_STORE_PASSWORD` from your `local.properties` |
      | `ANDROID_UPLOAD_KEY_ALIAS` | `RELEASE_KEY_ALIAS` (`dotwatcher`) |
      | `ANDROID_UPLOAD_KEY_PASSWORD` | `RELEASE_KEY_PASSWORD` |
      | `LINZ_API_KEY` | Already there for iOS; shared |

## Step 1 — version code from git (`android/app/build.gradle.kts`)

- [ ] `versionCode`: the `versionCode` Gradle property if given, else `git rev-list --count HEAD` (via
      `providers.exec`, so it works with the configuration cache), else fail with a clear message (no silent
      fallback to a low number that Play would refuse).
- [ ] `versionName`: the `versionName` Gradle property if given, else the value in the file (stays `1.4.3` until
      bumped by hand in a PR, like `MARKETING_VERSION`).
- [ ] Comment explaining the scheme and pointing at `docs/android-play-internal.html`. Update the "Distribution"
      note in `.ai/plans/android-app.md` (no more hand bumps of `versionCode`).
- [ ] Check `android-gradle.yml` still builds: its checkout is shallow (the default depth of 1), so the commit count
      would be 1. Fine for a debug build that's never uploaded, but either give it `fetch-depth: 0` or have the
      count fall back to 1 only for debug builds. Decide when building; failing a debug PR build over it would be
      wrong.

## Step 2 — the workflow (`.github/workflows/android-play-internal.yml`)

- [ ] Triggers: `workflow_dispatch` (inputs `version`, `request_id`, same validation as iOS) and `push` tags
      `android-v*`.
- [ ] `run-name`: `Play internal <version or 'project version'> from <ref> [<request_id>]`.
- [ ] `concurrency: { group: android-play-upload, cancel-in-progress: false }`.
- [ ] `runs-on: ubuntu-latest`, `timeout-minutes: 30`, checkout with `fetch-depth: 0` and tags.
- [ ] Check version (dispatch): `X.Y.Z`; refuse if `android-v<version>` exists on a different commit.
- [ ] Check tag (tag runs): the tag must match `versionName` in `build.gradle.kts`.
- [ ] JDK 17 (temurin) and `gradle/actions/setup-gradle`, as in `android-gradle.yml`.
- [ ] Write secrets: fail if any signing secret is missing; decode the keystore to `$RUNNER_TEMP/upload.jks`;
      write `android/local.properties` with `LINZ_API_KEY` and the four `RELEASE_*` values (warn, don't fail, if
      `LINZ_API_KEY` is empty, as iOS does).
- [ ] Build: `./gradlew bundleRelease -PversionName=<version>` (version code from Step 1).
- [ ] Verify the AAB is signed (`jarsigner -verify`) before uploading.
- [ ] Upload: `r0adkll/upload-google-play` with `packageName: nz.skelstar.dotwatcher`, `track: internal`,
      `status: completed` (or `draft`, see decisions), `releaseFiles` the AAB. Optionally a short release note from
      the commit subject.
- [ ] Tag `android-v<version>` on the built commit after a dispatched upload (`contents: write`), same rules and
      warnings as the iOS step.
- [ ] Summary: version name, version code, branch, commit, "appears on the internal track in a few minutes".
- [ ] Always delete the keystore, `local.properties` and the service account file at the end.

## Step 3 — docs

- [ ] `docs/android-play-internal.html`, modelled on `docs/ios-testflight.html`: what it does, version name and
      version code, what to do when…, branches and version codes, running it, one-time setup, errors (e.g. "Version
      code N has already been used", "The caller does not have permission", "Only releases with status draft may
      be created on draft app", unsigned bundle).
- [ ] Link it from `docs/ios-testflight.html` and the Android README.

## Step 4 — prove it

- [ ] First manual run on the branch uploads, and the build installs from the Play Store on a test phone.
- [ ] A tag run (`android-v1.4.3`) succeeds after the merge.
- [ ] Builds made locally in Android Studio after this get the commit-count version code too (check
      `versionCode` in Build → Analyze APK).

## Step 5 — Kraken (only after the Kraken iOS work exists)

- [ ] Add `{ platform: android, workflow: android-play-internal.yml, tagPrefix: android-v, label: Play internal }`
      to the dot-watcher `phoneBuilds` list. Kraken's code should need nothing else if it was built per platform.
- [ ] The fine-grained token already has Actions read/write on `skelstar/dot-watcher` from the iOS work.
- [ ] Kraken's "project version" for Android reads `versionName` from `android/app/build.gradle.kts`.

## Open questions

- Is the Play app still a **draft app** (never reviewed for any track)? Decides `completed` vs `draft` upload status.
- Is **Play App Signing** on? (Expected yes; the upload key in GitHub is only safe to store if it is.)
- Shallow-checkout debug builds in `android-gradle.yml`: give them full history, or a debug-only fallback?
- Release notes: commit subject, a workflow input, or none?
- Play may warn about missing native debug symbols (MapLibre's `.so` files). Harmless for internal testing, like
  the missing MapLibre dSYM on iOS. Uploading them is a later nicety.
