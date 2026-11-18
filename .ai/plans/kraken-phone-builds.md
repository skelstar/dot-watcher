# Phone builds from Kraken — Plan

*DotWatcher + Kraken — plan, November 2026. Status: **done and in use** (18 November 2026). Kraken has uploaded iOS
1.4.4, 1.4.5 and 1.4.6 to TestFlight and Android 1.4.4 and 1.4.6 to Play internal testing. See
[Status](#status-18-november-2026) at the end; the rest of the file is the plan as written, with boxes ticked.*

Goal: in Kraken (kraken.skelstar.io, repo `skelstar/tatooine-deploy`), pick a branch, type a version such as
`1.4.4`, press **Upload to TestFlight**, and watch it go up. Kraken never builds anything itself (an iOS build needs a
Mac); it starts the `ios-testflight.yml` GitHub Actions workflow and follows the run.

Related: [`docs/ios-testflight.html`](../../docs/ios-testflight.html) (how the workflow, versions and build numbers
work), `.github/workflows/ios-testflight.yml`, and Kraken's own design doc in Tatooine-Configuration at
`docs/kraken/how-it-works.html`.

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI; nothing below
is verified locally.

---

## Decisions (all kept as built)

- **Kraken, not a new app.** It already has a GitHub token, a SQLite history, server-rendered pages, a confirm
  screen, a `DEPLOYS_ENABLED` switch and a fake mode. Phone builds become a new section beside the deploy dashboard.
- **Kraken triggers workflows; it never runs shell scripts.** One `workflow_dispatch` call with the branch and inputs,
  then it reads the run's status from the GitHub API.
- **Versions are typed as `1.4.4`, not `v1.4.4`.** Plain `v*` tags are reserved for server/web releases
  (`release.yml`, read by Kraken); iOS uses `ios-v*`, Android `android-v*`.
- **The version applies to that build only; nothing is committed.** `staging` is protected by rulesets, so a
  workflow can't commit a version bump to it. The workflow passes `MARKETING_VERSION=<input>` to `xcodebuild`
  (`CFBundleShortVersionString` is `$(MARKETING_VERSION)` in `Info.plist`, so the override reaches the app).
- **A successful upload is tagged** `ios-v<version>` on the commit that was built, so git records what went up even
  though the project file wasn't changed. The tag is the source of truth for "latest uploaded version".
- **Known drift, accepted:** the Xcode project keeps saying 1.4.3 until it's bumped by hand in a normal PR. Builds
  archived on a Mac use the project's number; workflow builds use the typed one. Kraken shows the project's version
  beside the input so the gap is visible.
- **Build numbers are unchanged:** still the commit count, stamped by the project's build phase. The branch caveats in
  `docs/ios-testflight.html` still apply.

## Step 0 — prerequisite: the workflow uploads on its own

Branch `ios-testflight-workflow` (merged, PR #352).

- [x] Merge the `ios-testflight-workflow` PR into `staging`.
- [x] Create the App Store Connect API key (Admin role, under **Team Keys**) and the repository secrets `ASC_KEY_ID`,
      `ASC_ISSUER_ID`, `ASC_KEY_P8`, `LINZ_API_KEY`. Not yet proven: no run has reached a step that uses them.
- [x] First manual run succeeds and the build appears in TestFlight: run #3 uploaded 1.4.3 (974) after a GitHub macOS capacity delay (see
      progress log). Branch `ios-testflight-runner` moves the job to `macos-15`; merge its PR.
- [ ] A tag run (`ios-v<current version>`) succeeds, which also proves the tag/version check. **Not needed in
      practice:** every upload so far was dispatched from Kraken, and dispatched runs push their own tag with
      `GITHUB_TOKEN`, which doesn't start the tag trigger. The tag-push path is kept but has never run.

Step 1 went ahead with the tag run still open (see above). The macOS capacity problem didn't come back, so Xcode
Cloud wasn't needed.

### Progress log

**17 November 2026 (NZ morning; runs are 16 Nov UTC)**

- Secrets added. The issuer ID is only shown for Team Keys (Individual keys have none and `xcodebuild` can't sign
  with them).
- Run #1 (`staging`, `runs-on: macos-26`, run 43922308234): sat in Queued with **no job created**. Cancel fails
  ("Failed to cancel workflow") because there is no job to stop, and Delete is only offered for finished runs.
  Force-cancel needs the API (`POST /repos/skelstar/dot-watcher/actions/runs/43922308234/force-cancel`), which
  wasn't available from here; GitHub cancels runs left queued for 24 hours, after which it can be deleted.
- Branch `ios-testflight-runner` (commit 64c0645): `runs-on: macos-15` (has Xcode 26; the Select Xcode step picks
  it) and the concurrency group renamed to `ios-testflight-upload` so the stuck run can't hold up new ones. Not yet
  merged.
- Run #2 (`ios-testflight-runner`, run 43925054638): a job was created this time, but it failed after 15 minutes
  without starting: "The job was not acquired by Runner of type hosted even after multiple attempts", plus GitHub
  notices "Due to capacity constraints, jobs targeting macOS arm64 runners may experience longer queue times."
  None of our steps ran, so the secrets and signing are still untested. Run #1 was probably the same shortage, so
  `macos-26` may have been fine after all.
- Next: press **Re-run jobs** on run #2 at a quieter time (NZ evening); if it queues again, upload from Xcode as
  usual. If the workflow uploads later from the same commit count, it will be refused for a repeated build number;
  that's harmless. A fallback is `runs-on: macos-15-intel` (separate Intel pool, slower, being retired in 2027).
- Run #3 (re-run of #2, about 15 minutes later): got a Mac at once and took 3 minutes. Uploaded 1.4.3 build 974;
  Apple's processing email arrived and the build installed from TestFlight. The shortage was temporary. Xcode Cloud
  is not needed for now.

## Step 1 — dot-watcher: the workflow takes a version

File: `.github/workflows/ios-testflight.yml`. All of this landed in #356 (checked against the workflow on `staging`,
18 November 2026).

- [x] `workflow_dispatch` inputs:
  - `version` (optional). Blank means "use the project's version", which is today's behaviour, so the Actions-tab
    button keeps working unchanged.
  - `request_id` (optional). Set by Kraken so it can find the run it started (see Step 2, "Finding the run").
- [x] `run-name:` shows the version, branch and request id, e.g. `TestFlight 1.4.4 from staging [k-42]`.
- [x] Validate `version` as `X.Y.Z` (digits only) before anything else; fail with a clear message.
- [x] Fail early if the tag `ios-v<version>` already exists on a different commit (that version was already uploaded
      from somewhere else). Same commit is allowed, and the run will then fail at upload only if the build number
      repeats.
- [x] Archive with `MARKETING_VERSION=<version>` when given.
- [x] Keep the tag-push trigger and its "tag must match the project" check for runs started by a tag.
- [x] After a successful upload of a dispatched run, create and push `ios-v<version>` on the built commit
      (`permissions: contents: write`). Tags pushed with `GITHUB_TOKEN` don't start workflows, so this can't loop
      back into the tag trigger. Check the repository rulesets allow tag creation by Actions.
- [x] Summary shows "version 1.4.4 (typed; project says 1.4.3)" when they differ.
- [x] Update `docs/ios-testflight.html`: the version input, the automatic tag, and the drift note.

## Step 2 — Kraken: a Phone builds section

Repo `skelstar/tatooine-deploy`. Built in Kraken PR #9, switched on by `PHONE_BUILDS_ENABLED` and the
`phoneBuilds` entry on Tatooine-Configuration's `kraken-phone-builds` branch, and in use since 17 November 2026.
The boxes below are ticked from that; they weren't re-checked one by one against the Kraken code from this repo.

### Config

- [x] `projects.yaml` gains an optional `phoneBuilds` list per project, e.g. for dot-watcher:
      `{ platform: ios, workflow: ios-testflight.yml, tagPrefix: ios-v, label: TestFlight }`. Add it to
      `config/projects.example.yaml` and to the ConfigMap in Tatooine-Configuration
      (`deployments/kraken/k8s/projects.yaml`).

### GitHub port (`src/ports.ts`, `src/real/github.ts`, `src/fake/`)

- [x] `listBranches(repo)`: for the branch picker (`staging` first, then most recently updated).
- [x] `listTags(repo, prefix)`: latest `ios-v*` for the suggestion and the "lower than last upload" warning.
- [x] `projectVersion(repo, ref)`: read `MARKETING_VERSION` from the `.pbxproj` at that ref, shown beside the input.
- [x] `dispatchWorkflow(repo, workflow, ref, inputs)`.
- [x] `findRun(repo, workflow, requestId)` and `getRun(repo, runId)` including job steps, for progress.
- [x] Fake versions of all of the above, with runs that move through their steps on a timer, like fake deploys.

### Finding the run

`workflow_dispatch` historically returns no run id. Kraken sends a `request_id` (its own build id) and finds the
run whose name contains it, polling the workflow's recent runs for up to about a minute. If GitHub's dispatch
response now includes the run id, use that and keep the name search as a fallback.

### Domain (`src/domain/`)

- [x] `phoneVersion.ts`: parse/compare `X.Y.Z`; suggest the next patch after the latest tag; flag an input that is
      lower than or equal to the latest tag ("1.4.3 was already uploaded; TestFlight will refuse it once approved")
      as a warning, not a block.
- [x] Map workflow steps to stages: Queued → Checking out → Archiving → Uploading → Done / Failed, with the failing
      step's name and a link to the run log.

### Store (`src/store.ts`)

- [x] `phone_builds` table: id, project, platform, branch, commit, version, request_id, run_id, status, stage,
      started_at, finished_at, error. Picked up again on restart (the run carries on in GitHub regardless).

### Views (`src/views/`)

- [x] Project page: a **Phone builds** card with the last build per platform (version, build number if known, branch,
      when, status).
- [x] Form: branch picker, version input pre-filled with the suggestion, the project's own version shown beside it,
      any warning, then **Upload to TestFlight** leading to Kraken's usual confirm screen. The confirm screen says it
      uses real data (Release → Production) and that nothing is submitted for review.
- [x] Progress page, same pattern as `/deployments/:id/progress`.
- [x] Builds appear in `/log` alongside deploys.

### Safety and access

- [x] The button is hidden unless `DEPLOYS_ENABLED=true` (or a separate `PHONE_BUILDS_ENABLED`, decide when
      building).
- [x] Fine-grained token gains **Actions: read and write** on `skelstar/dot-watcher`. Update the token table in the
      Kraken README.
- [x] Refuse to start a second build while one for the same platform is queued or running (the workflow queues them
      anyway; Kraken should say so rather than look stuck).

### Tests

- [x] Domain tests for version parsing, suggestion and warnings, and for the step-to-stage mapping.
- [x] A fake-mode scenario per state: idle, running, failed at upload (build number refused), token without Actions
      permission.

## Alternative: Xcode Cloud (not pursued)

Not pursued: GitHub's macOS shortage on 17 November was temporary, and every run since has got a Mac straight away.
Kept for reference in case it comes back. If GitHub's macOS runners keep failing for capacity, Apple's Xcode Cloud
could build and upload instead. It's a medium change, not a rewrite.

**What it gives:** Apple's own Macs (no GitHub queue), 25 compute hours a month included with the developer
membership, and signing plus TestFlight upload built in (no `ASC_*` secrets needed for the build itself).

**Setup:**

- [-] In Xcode on the Mac: Integrate → Create Workflow. Grant Apple access to the GitHub repo; Archive action;
      post-action "TestFlight (internal testing only)"; start condition manual (and/or `ios-v*` tags).
- [-] `ios/DotWatcher/ci_scripts/ci_post_clone.sh` (next to the `.xcodeproj`): writes `Config/Secrets.xcconfig`
      from an Xcode Cloud secret environment variable `LINZ_API_KEY`, so the map isn't blank.
- [-] First build: check the build number. Xcode Cloud numbers builds itself, which replaces the commit-count
      scheme; set its "next build number" above the last uploaded build. How it interacts with the project's
      build-number build phase is unchecked.

**What it changes in this plan:**

- Step 0 becomes "an Xcode Cloud build reaches TestFlight", and `ios-testflight.yml` is kept as a fallback or removed.
- Step 1 (typed version) is unclear: Xcode Cloud workflows don't take inputs per build the way `workflow_dispatch`
  does. Needs checking whether the App Store Connect API (`ciBuildRuns`) can pass a version through, or whether a
  `ci_pre_xcodebuild.sh` script can set `MARKETING_VERSION` from something else (e.g. the tag being built).
- Step 2: Kraken would start builds and read progress through the App Store Connect API (`ciWorkflows`,
  `ciBuildRuns`, `ciBuildActions`) using the API key already created, instead of the GitHub Actions API. The rest of
  Kraken's side (config, store, views, safety) stays the same.

**Suggested:** retry GitHub a few times first; GitHub's arm64 shortages are usually temporary. Switch if they keep
recurring.

## Later, not in this plan

- ~~Android: a `android-play-internal.yml` workflow (Play internal testing, `android-v*` tags) would slot into the same
  `phoneBuilds` config.~~ Done: see [`android-play-internal.md`](android-play-internal.md).
- Submitting for App Store review from Kraken. Deliberately left manual.
- ~~Bumping `MARKETING_VERSION` in the project automatically through a PR.~~ Done: Kraken's version-bump button opens
  the PR (for example #359 for iOS 1.4.5), and you merge it.

## Open questions (answered)

- ~~Kraken has no login mentioned in its README. Is it reachable from outside the home network?~~ Kraken sits behind
  basic auth.
- ~~Should a run with no typed version also create its `ios-v*` tag?~~ Yes, as built: every dispatched run tags the
  version read from the built app's `Info.plist`, typed or not, and leaves an existing tag on another commit alone.

## Status, 18 November 2026

- **Uploads so far from Kraken:** TestFlight 1.4.4 (k1), 1.4.5 (k2), 1.4.6 (k4); Play internal 1.4.4 (k3) and 1.4.6.
  Tags `ios-v1.4.6` and `android-v1.4.6` are both on `d3a02fc`.
- **Phone versions are kept in step.** Android skipped 1.4.5 so both platforms are on 1.4.6. Remote palettes are
  assigned by version number regardless of platform, so a shared number means a shared palette (see
  [`remote-palette.md`](remote-palette.md)).
- **Kraken's token** needs Actions and Pull requests read/write, with `skelstar/dot-watcher` in its repository list.
- **Still open:** the tag-push run in step 0, which nothing currently uses.
