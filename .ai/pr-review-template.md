# AI PR Review Template

**Purpose:** Standardized template for AI-assisted static code reviews for Dot Watcher.

**Scope:** This is a static code review only. Do not run build, publish, test, package, simulator, server, Docker, or CI-equivalent commands unless the user explicitly asks in the current turn. GitHub Actions CI is responsible for runtime verification.

---

## Review Process

### 1. Initial Analysis

Use source-only inspection:

```bash
git status --short
git diff <target-branch>...<feature-branch> --stat
git diff <target-branch>...<feature-branch>
git log <target-branch>..<feature-branch> --oneline
```

Use `rg` and file reads for targeted follow-up. Do not run application code, builds, tests, formatters, package installs, servers, Docker commands, mobile simulators, or CI-equivalent commands.

### 2. Review Output Structure

#### A. Header

- Source branch -> target branch
- Review date and time, including timezone
- Reviewer
- Overall status: Approve / Comment / Request Changes

#### B. Findings

Lead with findings before summary. Order by severity.

For each finding include:

- Severity: Critical / High / Medium / Low
- File and line reference
- What is wrong
- Why it matters
- Suggested fix

If there are no findings, say that clearly and list residual risks or CI/test gaps.

#### C. Summary

- One-paragraph overview of the change
- Key behavior changed
- Deployment, configuration, or compatibility notes

#### D. Technical Assessment

Cover:

- Correctness and edge cases
- API compatibility and existing URI stability
- ASP.NET Core controller/startup/service boundaries
- SQLite persistence behavior and in-memory live state behavior
- Concurrency and thread-safety around live session state
- Error handling and logging
- Backwards compatibility for iOS, web client, debug dashboard, and deployment routing

#### E. Security and Privacy

Check:

- Bearer token enforcement on protected write/delete endpoints
- Read access via session code and whether any endpoint exposes more than intended
- GPS location data leakage in logs, recordings, responses, debug dashboard, or static files
- Input validation for session codes, runner names, timestamps, coordinates, headings, and uploaded NDJSON
- CORS and public deployment exposure
- Secret handling in config, appsettings, environment variables, Docker, and k8s notes

#### F. Performance and Reliability

Check:

- SQLite access patterns and transaction safety
- Unbounded memory growth in live session state or log buffers
- Large recording upload/download behavior
- Polling endpoint efficiency for web viewers
- File/database persistence across container restarts and redeployments
- Failure modes for malformed JSON/NDJSON and missing configuration

#### G. Test Coverage Analysis

Static review only. Evaluate whether tests exist or should exist, but do not run them.

Suggest focused tests for:

- `POST /location` auth, validation, storage, and response behavior
- `GET /locations/{sessionCode}` unknown sessions and latest-position behavior
- Recording upload/download/delete/list/merge behavior
- Log feed and clear behavior
- Startup configuration failures such as missing `BearerToken`
- Client-visible API compatibility for unchanged route paths

For each recommended test include:

- What scenario it covers
- Why it matters
- Suggested file/location
- Important assertions
- Whether CI should verify it

#### H. Dot Watcher Checklist

- [ ] Existing API URI paths remain unchanged for iOS and web clients.
- [ ] Protected endpoints require `Authorization: Bearer <token>`.
- [ ] Public read endpoints do not require bearer auth but expose only intended session data.
- [ ] Session codes are normalized consistently where persistence depends on them.
- [ ] Live in-memory state and SQLite recording state remain intentionally separate.
- [ ] Uploaded recordings cannot crash the app with malformed NDJSON.
- [ ] Debug dashboard routes do not expose secrets.
- [ ] GPS timestamps represent capture time, not request receipt time.
- [ ] CORS and deployment routing assumptions are still documented.
- [ ] README/API docs and GitHub Actions workflow stay aligned with code changes.
- [ ] No local build, publish, or test commands were run during review unless explicitly requested.

#### I. Recommendations

Use this format:

```markdown
### 1. Title (Priority: High)

**Why it matters:**
Explain impact.

**Suggested approach:**
Concrete implementation guidance.

**Test coverage:**
Scenarios to add or verify in CI.

**Estimated effort:** X minutes/hours
```

#### J. Questions

List concrete questions for the developer or product owner. Avoid vague questions.

#### K. Final Verdict

State one of:

- Approve
- Comment
- Request Changes

Include a short rationale and next steps.

---

## Project Context

### Stack

- .NET 9 ASP.NET Core server
- ASP.NET Core controllers under `server/Controllers/`
- SQLite persistence via `Microsoft.Data.Sqlite`
- xUnit integration tests under `server/DotWatcher.Server.Tests/`
- GitHub Actions CI for restore, build, publish, and tests
- React web client with Mapbox
- Swift iOS tracker app
- k3s home-lab deployment behind Traefik/Cloudflare routing

### Main App Areas

- `server/Program.cs` - startup, DI, static files, CORS, controller routing, and legacy NDJSON migration.
- `server/Controllers/` - HTTP API endpoints.
- `server/SessionStore.cs` - in-memory live positions and SQLite-backed recordings.
- `server/Models.cs` - API payload records.
- `server/LogBuffer.cs` - debug dashboard log buffer.
- `server/wwwroot/index.html` - debug dashboard.
- `server/DotWatcher.Server.Tests/` - API integration tests.
- `client/` - web viewer.
- `ios/` - iOS GPS tracker.

### Review Tone

- Be direct and specific.
- Prioritize real correctness bugs, security/privacy issues, API compatibility breaks, deployment risks, and missing tests.
- Avoid nitpicks unless they affect maintainability, reliability, or user experience.
- Provide clear fixes, not just observations.
