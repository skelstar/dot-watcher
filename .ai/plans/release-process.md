# Release process — Plan

Status: drafted, not started. Written 2026-11-01.

## Goal

Cut a release with as little hand work as possible. Today a release is a run of manual steps, and three of them keep
causing friction:

1. **Version bumps get forgotten.** Android `versionName` and `versionCode`, and iOS `MARKETING_VERSION` (three build
   configurations), are edited by hand on a `release-X.Y.Z` branch. The Play bundle needs a higher `versionCode` every
   time.
2. **The colour scheme changes with each minor version**, and it has to be remembered and applied by hand to both apps.
3. **Building the Android release** is done locally from `local.properties` signing values and the `.aab` is uploaded
   by hand.

Per `AGENTS.md`, build and test verification is left to CI on the PR; nothing below is built or verified yet.

## How a release works today

1. Branch `release-X.Y.Z` from `origin/staging`; edit `android/app/build.gradle.kts` (`versionCode`, `versionName`) and
   `ios/DotWatcher/DotWatcher.xcodeproj/project.pbxproj` (`MARKETING_VERSION` x3). PR into `staging`.
2. PR `staging` into `main`.
3. `./gradlew :app:bundleRelease` locally (signing from `android/local.properties`), upload the `.aab` to Play.
4. Build and deploy iOS from Xcode. `CFBundleVersion` and `GitCommitSHA` in `Info.plist` are stamped by the build, so
   there is nothing to bump (the file shows as modified after a build; ignore it).
5. Write the store notes and create the GitHub release by hand.

Existing CI: `android-gradle.yml` (runs `assembleDebug` only), `server-dotnet.yml`, `client-node.yml`,
`enforce-staging.yml`. Nothing builds a release bundle or checks versions.

---

## Option 1: a release script (versions you forget)

`scripts/release.sh X.Y.Z` does what the hand steps do:

- branches `release-X.Y.Z` from `origin/staging`;
- sets Android `versionName`, increments `versionCode`, and sets iOS `MARKETING_VERSION` in all three configurations;
- commits `Bump to X.Y.Z: ...` (the existing message style), pushes, and opens the PR into `staging`.

### Safety net: a CI version check

A job on PRs into `main` that fails when:

- Android `versionCode` is not higher than the one on `main`;
- Android `versionName` and iOS `MARKETING_VERSION` disagree, or are not higher than `main`'s.

Then a forgotten bump turns the PR red instead of being found at upload time.

### Optional: derive `versionCode` automatically

Compute `versionCode` from the commit count (or CI run number) in `build.gradle.kts`, as iOS already does for its build
number. Nothing to hand-bump, but the numbers jump. Play only needs them to rise.

- [ ] `scripts/release.sh` (macOS `sed -i ''` and GNU compatible).
- [ ] CI version check on PRs into `main`.
- [ ] Decide: keep hand-bumped `versionCode` (script does it) or derive it.

## Option 2: colour scheme per minor version

Two routes:

- **Server-driven palette** — already planned in [`remote-palette.md`](remote-palette.md): the server sends the palette,
  the apps cache it and fall back to built-in colours, so changing colours is a config change and not an app release.
  It can be tried on Staging first (shared database, separate config). Not built yet. This removes the friction rather
  than reminding about it.
- **Checklist or CI nag** (lighter, keeps colours in the app): the release script prints a reminder when the minor
  version changes, or the version check comments on the PR. Cheap, but it is still a manual change in two places
  (`SignPalette.kt`, `LandingSigns.swift`).

Recommendation: build the remote palette as its own project; until then the script's minor-bump reminder is enough.

- [ ] Remote palette (see `remote-palette.md`).
- [ ] Interim: reminder in `release.sh` when the minor version changes.

## Option 3: build the Android release in CI

A workflow triggered by a `v*` tag, or manually, that runs `./gradlew :app:bundleRelease` and attaches the signed
`.aab` to the GitHub release.

- Needs repo secrets for the keystore (base64) and `RELEASE_STORE_PASSWORD`, `RELEASE_KEY_ALIAS`,
  `RELEASE_KEY_PASSWORD`. Only the repo owner can add them.
- The workflow writes the keystore to a temp file and the four values to `local.properties`, which is how
  `build.gradle.kts` already reads them; the release build type falls back to unsigned without them.
- Step two, separate: upload to Play (internal track) with a Play service account. Start with "bundle attached to the
  release" and decide on auto-upload after.

- [ ] Secrets added (owner).
- [ ] `.github/workflows/android-release.yml`.
- [ ] Decide on Play auto-upload.

## Option 4: one-command release (`/release` skill)

A skill that ties it together: runs the script, drafts the "What's new" notes from the PRs merged since the last tag
(short Play version under 500 characters, longer App Store version), and creates the GitHub release (`vX.Y.Z`, titled
with the release's theme, as `v1.2.3` "Berry and cream" and `v1.3.0` "Navy + orange" were).

- [ ] `/release` skill.

---

## Suggested order

1. `scripts/release.sh` and the CI version check (small, stops the forgetting).
2. The Android release workflow.
3. The remote palette, as its own project.
4. The `/release` skill, once 1 and 2 exist.

## Open decisions

- Hand-bump or derive `versionCode`?
- Do we want Play upload automated, or only the bundle attached to the GitHub release?
- Do iOS builds join this later (TestFlight upload from CI), or stay in Xcode?
