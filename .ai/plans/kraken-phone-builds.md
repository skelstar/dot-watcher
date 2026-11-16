# Phone builds from Kraken — Plan

*DotWatcher + Kraken — plan, November 2026. Status: drafted, not started. Waiting on the TestFlight workflow (step 0).*

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

Branch `ios-testflight-workflow` (pushed; PR to `staging`).

- [ ] Merge the `ios-testflight-workflow` PR into `staging`.
- [ ] Create the App Store Connect API key (Admin role) and the repository secrets `ASC_KEY_ID`, `ASC_ISSUER_ID`,
      `ASC_KEY_P8`, `LINZ_API_KEY`.
- [ ] First manual run on `staging` succeeds and the build appears in TestFlight. Fix `runs-on: macos-26` if the
      image or Xcode 26 isn't available.
- [ ] A tag run (`ios-v<current version>`) succeeds, which also proves the tag/version check.

Nothing below starts until these are ticked.

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
