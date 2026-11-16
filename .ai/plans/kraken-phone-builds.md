# Phone builds from Kraken — Plan

*DotWatcher + Kraken — plan, November 2026. Status: step 0 in progress. Workflow merged and secrets added; no run
has reached a Mac yet because of GitHub's macOS capacity (see the step 0 progress log).*

Goal: in Kraken (kraken.skelstar.io, repo `skelstar/tatooine-deploy`), pick a branch, type a version such as
`1.4.4`, press **Upload to TestFlight**, and watch it go up. Kraken never builds anything itself (an iOS build needs a
Mac); it starts the `ios-testflight.yml` GitHub Actions workflow and follows the run.

Related: [`docs/ios-testflight.html`](../../docs/ios-testflight.html) (how the workflow, versions and build numbers
work), `.github/workflows/ios-testflight.yml`, and Kraken's own design doc in Tatooine-Configuration at
`docs/kraken/how-it-works.html`.

Tick boxes as work lands: `- [ ]` → `- [x]`. Per `AGENTS.md`, build/test verification is left to CI; nothing below
is verified locally.

---

## Decisions (proposed; confirm before step 1)

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
- [ ] First manual run succeeds and the build appears in TestFlight. Blocked by GitHub's macOS arm64 capacity (see
      progress log). Branch `ios-testflight-runner` moves the job to `macos-15`; merge its PR once a run works.
- [ ] A tag run (`ios-v<current version>`) succeeds, which also proves the tag/version check.

Nothing below starts until these are ticked. If GitHub's Macs stay unavailable, see "Alternative: Xcode Cloud"
below, which would replace this step.

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

## Step 1 — dot-watcher: the workflow takes a version

File: `.github/workflows/ios-testflight.yml`.

- [ ] `workflow_dispatch` inputs:
  - `version` (optional). Blank means "use the project's version", which is today's behaviour, so the Actions-tab
    button keeps working unchanged.
  - `request_id` (optional). Set by Kraken so it can find the run it started (see Step 2, "Finding the run").
- [ ] `run-name:` shows the version, branch and request id, e.g. `TestFlight 1.4.4 from staging [k-42]`.
- [ ] Validate `version` as `X.Y.Z` (digits only) before anything else; fail with a clear message.
- [ ] Fail early if the tag `ios-v<version>` already exists on a different commit (that version was already uploaded
      from somewhere else). Same commit is allowed, and the run will then fail at upload only if the build number
      repeats.
- [ ] Archive with `MARKETING_VERSION=<version>` when given.
- [ ] Keep the tag-push trigger and its "tag must match the project" check for runs started by a tag.
- [ ] After a successful upload of a dispatched run, create and push `ios-v<version>` on the built commit
      (`permissions: contents: write`). Tags pushed with `GITHUB_TOKEN` don't start workflows, so this can't loop
      back into the tag trigger. Check the repository rulesets allow tag creation by Actions.
- [ ] Summary shows "version 1.4.4 (typed; project says 1.4.3)" when they differ.
- [ ] Update `docs/ios-testflight.html`: the version input, the automatic tag, and the drift note.

## Step 2 — Kraken: a Phone builds section

Repo `skelstar/tatooine-deploy`. Work on a branch there; its own README and design doc get updated too.

### Config

- [ ] `projects.yaml` gains an optional `phoneBuilds` list per project, e.g. for dot-watcher:
      `{ platform: ios, workflow: ios-testflight.yml, tagPrefix: ios-v, label: TestFlight }`. Add it to
      `config/projects.example.yaml` and to the ConfigMap in Tatooine-Configuration
      (`deployments/kraken/k8s/projects.yaml`).

### GitHub port (`src/ports.ts`, `src/real/github.ts`, `src/fake/`)

- [ ] `listBranches(repo)`: for the branch picker (`staging` first, then most recently updated).
- [ ] `listTags(repo, prefix)`: latest `ios-v*` for the suggestion and the "lower than last upload" warning.
- [ ] `projectVersion(repo, ref)`: read `MARKETING_VERSION` from the `.pbxproj` at that ref, shown beside the input.
- [ ] `dispatchWorkflow(repo, workflow, ref, inputs)`.
- [ ] `findRun(repo, workflow, requestId)` and `getRun(repo, runId)` including job steps, for progress.
- [ ] Fake versions of all of the above, with runs that move through their steps on a timer, like fake deploys.

### Finding the run

`workflow_dispatch` historically returns no run id. Kraken sends a `request_id` (its own build id) and finds the
run whose name contains it, polling the workflow's recent runs for up to about a minute. If GitHub's dispatch
response now includes the run id, use that and keep the name search as a fallback.

### Domain (`src/domain/`)

- [ ] `phoneVersion.ts`: parse/compare `X.Y.Z`; suggest the next patch after the latest tag; flag an input that is
      lower than or equal to the latest tag ("1.4.3 was already uploaded; TestFlight will refuse it once approved")
      as a warning, not a block.
- [ ] Map workflow steps to stages: Queued → Checking out → Archiving → Uploading → Done / Failed, with the failing
      step's name and a link to the run log.

### Store (`src/store.ts`)

- [ ] `phone_builds` table: id, project, platform, branch, commit, version, request_id, run_id, status, stage,
      started_at, finished_at, error. Picked up again on restart (the run carries on in GitHub regardless).

### Views (`src/views/`)

- [ ] Project page: a **Phone builds** card with the last build per platform (version, build number if known, branch,
      when, status).
- [ ] Form: branch picker, version input pre-filled with the suggestion, the project's own version shown beside it,
      any warning, then **Upload to TestFlight** leading to Kraken's usual confirm screen. The confirm screen says it
      uses real data (Release → Production) and that nothing is submitted for review.
- [ ] Progress page, same pattern as `/deployments/:id/progress`.
- [ ] Builds appear in `/log` alongside deploys.

### Safety and access

- [ ] The button is hidden unless `DEPLOYS_ENABLED=true` (or a separate `PHONE_BUILDS_ENABLED`, decide when
      building).
- [ ] Fine-grained token gains **Actions: read and write** on `skelstar/dot-watcher`. Update the token table in the
      Kraken README.
- [ ] Refuse to start a second build while one for the same platform is queued or running (the workflow queues them
      anyway; Kraken should say so rather than look stuck).

### Tests

- [ ] Domain tests for version parsing, suggestion and warnings, and for the step-to-stage mapping.
- [ ] A fake-mode scenario per state: idle, running, failed at upload (build number refused), token without Actions
      permission.

## Alternative: Xcode Cloud (not decided)

If GitHub's macOS runners keep failing for capacity, Apple's Xcode Cloud could build and upload instead. It's a
medium change, not a rewrite.

**What it gives:** Apple's own Macs (no GitHub queue), 25 compute hours a month included with the developer
membership, and signing plus TestFlight upload built in (no `ASC_*` secrets needed for the build itself).

**Setup:**

- [ ] In Xcode on the Mac: Integrate → Create Workflow. Grant Apple access to the GitHub repo; Archive action;
      post-action "TestFlight (internal testing only)"; start condition manual (and/or `ios-v*` tags).
- [ ] `ios/DotWatcher/ci_scripts/ci_post_clone.sh` (next to the `.xcodeproj`): writes `Config/Secrets.xcconfig`
      from an Xcode Cloud secret environment variable `LINZ_API_KEY`, so the map isn't blank.
- [ ] First build: check the build number. Xcode Cloud numbers builds itself, which replaces the commit-count
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

- Android: a `android-play-internal.yml` workflow (Play internal testing, `android-v*` tags) would slot into the same
  `phoneBuilds` config.
- Submitting for App Store review from Kraken. Deliberately left manual.
- Bumping `MARKETING_VERSION` in the project automatically through a PR.

## Open questions

- Kraken has no login mentioned in its README. Is it reachable from outside the home network? If so, the build
  button needs more than the `DEPLOYS_ENABLED` switch.
- Should a run with no typed version also create its `ios-v*` tag? (Proposed: yes, when that tag doesn't exist yet,
  so the latest tag is always the latest upload.)
