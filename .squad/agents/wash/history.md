# Wash — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** GitHub Actions (YAML), GitHub Pages / Azure Static Web Apps
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Workflows:** `refresh-metrics.yml` (daily+manual), `refresh-inventory.yml` (manual). AI-assisted: `inventory-review.md`, `weekly-summary.md`, `health-triage.md`.

## Core Context

### Workflow Architecture
- **refresh-metrics.yml:** Daily CI data collection (net10.0 Collector), commits to history, deploys to GitHub Pages
- **refresh-inventory.yml:** Manual package discovery via NuGet API, creates PR with branch pattern for human review
- **Agentic Workflows (.github/aw/*.md):** Non-authoritative markdown specs (inventory-review, weekly-summary, health-triage); analyze & suggest only, no production modifications
- **Profile Regeneration:** CI now auto-regenerates repo-identity profiles after metrics refresh using net10.0 RepoIdentity tool
- **Key Design Patterns:** `[skip ci]` tags prevent loops, diff checks keep history clean, DASHBOARD_REPO_ROOT env var ensures CI path resolution

### Diagnostic Patterns
When investigating "data staleness" reports:
1. Check workflow execution status in GitHub Actions UI
2. If workflows passing, verify local workspace sync (`git fetch`, `git log HEAD..origin`)
3. Compare local file timestamps vs workflow run timestamps
4. Issue is usually workspace sync, not pipeline failure

## Recent Updates

2026-04-21: Metrics Stall Investigation (merged to decisions #33)
- Root cause: Local workspace out of sync with origin/main (April 17 local vs April 18-21 remote)
- CI was succeeding; user needed `git pull origin main`
- Recommended workspace health check to prevent false alarms (decision deferred to Mal)

## Learnings

### Metrics Stall Investigation (2026-04-21)

**Issue:** Bruno reported dashboard showing no progress for several days (data frozen at April 17, 2026).

**Root Cause:** Local workspace was out of sync with GitHub remote (`origin/main`). The `refresh-metrics.yml` workflow was running successfully every day at 09:00 UTC and committing fresh data to GitHub, but the local workspace had uncommitted changes that prevented a clean `git pull`.

**Evidence Found:**
1. GitHub Actions workflow runs: All recent runs (#42, #41, #40, #39, #38, #37, #36, #35) showed `status: completed, conclusion: success`
2. Workflow logs confirmed Collector was executing correctly:
   - Writing to `data/latest/*.json` and `data/history/YYYY/MM/DD/*.json`
   - Detecting data changes via `git diff --cached --quiet`
   - Committing with message `"chore: refresh metrics [skip ci]"`
3. Local `data/latest/*.json` timestamps: April 17, 2026 10:29 AM
4. Remote commits: Fresh data commits on April 18, 19, 20, 21 (visible via `git log HEAD..origin/main`)
5. Git status: `HEAD` was behind `origin/main` by 10+ commits

**Resolution:**
- Executed `git pull origin main` to sync local workspace with remote
- Local data files updated to April 21, 2026 (current)
- Verified history snapshots present for all missing days (04/18, 04/19, 04/20, 04/21)

**Key Diagnostic Commands:**
```bash
# Check workflow run history
github-mcp-server-actions_list --resource_id refresh-metrics.yml

# Check recent workflow logs
github-mcp-server-get_job_logs --job_id <id>

# Check local vs remote divergence
git fetch origin main
git log HEAD..origin/main

# Verify data timestamps
Get-ChildItem data/latest/*.json | Select Name, LastWriteTime
Get-ChildItem data/history/YYYY/MM/DD -Recurse | Sort LastWriteTime -Desc
```

**Pattern Identified:** When investigating "data staleness" complaints:
1. First check workflow execution status (GitHub Actions UI or MCP tools)
2. If workflows are succeeding, check local workspace sync (`git status`, `git fetch`, `git log HEAD..origin`)
3. Verify Collector logs show data writes (look for "Written: /path/to/data")
4. Compare local file timestamps vs workflow run timestamps
5. The issue is often workspace sync, not the CI/CD pipeline itself

**Recommendation:** Consider adding a pre-commit hook or workspace health check to remind users to pull before assuming pipeline failure.

### Phase 9: CI Auto-Regenerate Profiles (2026-XX-XX)

**Workflow Status:** Completed  
**Session:** phase-9-ci-autogen (Bruno Capuano)

Added automatic profile regeneration to `.github/workflows/refresh-metrics.yml`. After the Collector updates `data/latest/data.repositories.json`, the CI workflow now runs `repo-identity generate` against fresh data. If profiles changed, they are committed with `[skip ci]` to prevent loops.

**Changes Made:**
1. **Workflow edit** (`.github/workflows/refresh-metrics.yml`): 
   - Added step 7 "Regenerate repo-identity profiles": Runs `dotnet run --project src/RepoIdentity/RepoIdentity.csproj --framework net10.0 --configuration Release -- generate --source data/latest/data.repositories.json`
   - Added step 8 "Commit updated profiles": Checks for staged changes before committing; only commits if profiles differ
   - Both steps inserted between "Commit and push" (old step 6) and "Assemble site" (now step 9)
   - Framework: `net10.0` (uses already-installed SDK from step 2)
   - Idempotent: git config set again (harmless), diff check prevents empty commits

2. **Documentation update** (`docs/repo-identity-install.md`):
   - Replaced placeholder section with full "CI Auto-Regeneration" coverage
   - Explains workflow sequence: data refresh → regenerate → commit if changed
   - Includes sync instructions for devices (git pull + install), manual trigger examples
   - Documented `[skip ci]` tag rationale (loop prevention)

**Design Rationale:**
- **Framework choice:** net10.0 instead of net8.0 — CI already has .NET 10 installed in step 2, no need for multi-version fallback
- **Diff check:** `git diff --staged --quiet` keeps git history clean; no commits when profiles are unchanged
- **[skip ci] tag:** Prevents infinite loop: commit → metrics refresh → commit → ...
- **Placement:** After metrics commit but before site assembly ensures profiles use freshest data

**Key File Paths:**
- `.github/workflows/refresh-metrics.yml` (steps 7–8 added)
- `docs/repo-identity-install.md` (section 161+ fully populated)

### Agentic Workflow Architecture (WI-7, WI-8, WI-9)

**Core Pattern:** All GitHub Agentic Workflows (`.github/aw/*.md`) are documented as markdown specification files, not executable YAML. They define behavior, triggers, and output expectations for AI-powered assistants.

**Key Design Principle:** Non-Authoritative Advisory
- Workflows analyze, detect, and suggest—but never modify production data
- All recommendations require explicit human review and approval
- Clear separation: deterministic workflows (refresh-metrics, refresh-inventory) own production data; agentic workflows own insight generation

**Workflow Breakdown:**

1. **inventory-review.md** — PR comment-based feedback on package-repo mappings
   - Reads discovered mappings from `refresh-inventory` PR
   - Quality checks: repo existence, connection, maintenance status, archived flag
   - Output: Structured PR review comment with ✅/⚠️/❓ categorization
   - No merge blocking; purely advisory

2. **weekly-summary.md** — Trend analysis and ecosystem metrics
   - Reads: `data/latest/data.json`, `data/history/` (historical context)
   - Aggregates: Top 5 packages, notable changes, repo activity
   - Output: GitHub issue with weekly summary
   - Runs weekly (Monday 09:00 UTC) or on-demand
   - Risk: None (no modifications, read-only)

3. **health-triage.md** — Anomaly detection and health monitoring
   - Detects: stale packages (6+ months), high-issue repos (200+), download anomalies, archived repos
   - Output: Consolidated GitHub issues per anomaly type
   - Runs daily (02:00 UTC) or on-demand
   - Each issue includes data-driven rationale, links, and recommendations
   - Critical: No automatic corrective action; all findings surfaced for human decision

**Key File Paths:**
- `.github/aw/inventory-review.md` (4.2 KB)
- `.github/aw/weekly-summary.md` (5.6 KB)
- `.github/aw/health-triage.md` (8.3 KB)
- `LICENSE` (MIT, 1.1 KB)

**Integration Notes:**
- All workflows operate on public data; no secrets required
- Error handling: Surface limitations rather than fail silently
- GitHub API rate limits: Implement graceful degradation
- Data freshness: Workflows reference specific data paths; ensure refresh jobs complete first

### Deterministic Workflows (WI-5, WI-6)

**refresh-metrics.yml** — Daily automated data refresh
- Triggers: cron `0 6 * * *` (daily 06:00 UTC) + manual `workflow_dispatch`
- Builds & runs Collector at `src/Collector/Collector.csproj` (**net10.0**, retargeted 2026-04-02)
- Sets `DASHBOARD_REPO_ROOT=${{ github.workspace }}` so Collector writes to correct paths
- Collector writes to `data/latest/data.json` and `data/history/YYYY/MM/DD/data.json`
- Uses `git diff --cached --quiet` on `data/` to detect changes; commits with `[skip ci]` tag
- Concurrency group prevents overlapping runs; idempotent by design
- **Phase 2 integration (2026-04-03):** Added step to deploy `data/latest/data.trends.json` to `_site/data/data.trends.json` for GitHub Pages accessibility. Enables dashboard sparkline visualization via Kaylee's TrendAggregationService output.

**refresh-inventory.yml** — Manual package discovery
- Trigger: `workflow_dispatch` only (human-initiated)
- Queries NuGet search API (`owner:` filter) for package discovery
- Compares discovered packages against `config/tracked-packages.json`
- Creates `inventory/refresh-{date}` branch with merged config
- Opens PR via `gh pr create` with checklist body and `inventory` label
- New packages get empty `repos: []` — human must fill in repo mappings during review
- Exits cleanly with log message if no new packages found

**Key File Paths:**
- `.github/workflows/refresh-metrics.yml` (WI-5)
- `.github/workflows/refresh-inventory.yml` (WI-6)
- `config/tracked-packages.json` — source of truth for tracked packages
- `data/latest/data.json` — latest collector output
- `data/history/YYYY/MM/DD/data.json` — historical snapshots

**Design Decisions:**
- `DASHBOARD_REPO_ROOT` env var is critical — Collector resolves paths relative to `AppContext.BaseDirectory` by default, which doesn't work in CI
- Concurrency on refresh-metrics prevents duplicate commits from overlapping schedule+manual runs
- Inventory workflow uses branch+PR pattern (not direct commit) to enforce human review of package additions
- `NUGET_OWNER` is configurable via job-level env var; defaults to "microsoft"

### GitHub Pages Deployment (2026-07-22)

**Workflow Update:** Added `deploy-dashboard` job to `refresh-metrics.yml` that runs after `collect`.

**Key Design Points:**
- .NET version fixed from `9.0.x` to `10.0.x` to match project's net10.0 target
- Two-job workflow: `collect` (data) → `deploy-dashboard` (site)
- Site assembly: `site/index.html` + `data/latest/*.json` → flat `_site/` with `data/` subfolder
- Uses `actions/upload-pages-artifact@v3` + `actions/deploy-pages@v4`
- Permissions: `contents: write`, `pages: write`, `id-token: write` (OIDC for Pages deploy)
- `deploy-dashboard` checks out with `ref: ${{ github.ref }}` to get freshly committed data
- Environment `github-pages` configured with deployment URL output
- Requires repo Settings → Pages → Source set to "GitHub Actions"

**Data path flattening:** `data/latest/data.nuget.json` → `_site/data/data.nuget.json` so the frontend fetches from `data/data.nuget.json` relative to site root.

### GitHub Pages Dashboard Integration (2026-04-02)

**Workflow Status:** Completed  
**Session:** github-pages-dashboard (Bruno Capuano, background mode)

GitHub Pages deployment job added to `refresh-metrics.yml`. After data collection, `deploy-dashboard` job assembles site from `site/index.html` + flattened `data/latest/*.json` → `_site/`, uploads artifact, and deploys via OIDC.

**Key Updates:**
- .NET version: `9.0.x` → `10.0.x` (matches Collector net10.0 target)
- Added `deploy-dashboard` job with Pages write + OIDC permissions
- `checkout@v3` with `ref: ${{ github.ref }}` ensures fresh data commit is available
- Site assembly: `site/index.html` + `data/latest/data.nuget.json` → `_site/data/data.nuget.json` (flattened for frontend)
- README updated with Pages documentation and setup requirement (Settings → Pages → Source = "GitHub Actions")

### Collector Inventory Mode Integration (2026-04-02)

**Workflow Status:** Completed  
**Session:** collector-inventory-mode (Bruno Capuano)

Refactored `refresh-inventory.yml` to delegate all business logic to the C# Collector running in `--mode inventory`.

**Key Changes:**
- Removed 3 bash steps: "Read dashboard config", "Discover NuGet packages", "Merge candidates into tracked packages"
- Replaced with single step: `dotnet run --project src/Collector/Collector.csproj --configuration Release -- --mode inventory`
- Fixed .NET version from `9.0.x` → `10.0.x` (matches Collector net10.0 target)
- Added `DASHBOARD_REPO_ROOT: ${{ github.workspace }}` env var (critical for CI path resolution)
- Simplified PR body to read `nugetProfile` with jq for display purposes only (Collector handles all discovery logic)
- Kept git/PR workflow operations (branch creation, commit, push, label, PR creation) in workflow — these are CI/CD concerns, not business logic

**Architecture Pattern:**
- Workflow handles orchestration (checkout, .NET setup, git operations, PR creation)
- Collector handles domain logic (NuGet discovery, ignore filtering, merge with tracked packages)
- Clean separation: bash-free data processing, all JSON parsing/merging moved to C#
- Env vars: `DASHBOARD_REPO_ROOT` and `GITHUB_TOKEN` passed to Collector for path resolution and optional GitHub API calls

**Verification:** `refresh-metrics.yml` confirmed correct — uses .NET 10, runs Collector with proper env vars, site assembly copies correct files (index.html + data/latest/*.json → _site/).

**File Paths:**
- `.github/workflows/refresh-inventory.yml` (rewritten, ~70 lines → ~60 lines, 90% less bash)
- `.github/workflows/refresh-metrics.yml` (verified, no changes needed)

### TrendAggregationService Data Integration (2026-01-XX)

**Workflow Status:** Completed  
**Objective:** Support new TrendAggregationService output in deployment pipeline

**Changes Made:**
1. **Assemble site step** (line 65-71): Added copy operation for `data/latest/data.trends.json` → `_site/data/data.trends.json`
   - Follows existing pattern: `cp data/latest/{filename} _site/data/{filename}`
   - Deployed alongside `data.nuget.json` and `data.repositories.json`

2. **Git change detection** (line 48): Verified existing pattern `git add --force data/latest/*.json data/history/` already covers new file
   - Glob pattern `*.json` captures `data.trends.json` automatically; no change needed
   - File is committed when Collector detects changes

**Why:** TrendAggregationService will output to `data/latest/data.trends.json` during Collector runs. Dashboard needs the file in `_site/data/` for frontend consumption (via `data/data.trends.json` relative path).

**File Modified:**
- `.github/workflows/refresh-metrics.yml` (added 1 line in Assemble site step)
