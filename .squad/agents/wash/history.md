# Wash — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** GitHub Actions (YAML), GitHub Pages / Azure Static Web Apps
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Workflows:** `refresh-metrics.yml` (daily+manual), `refresh-inventory.yml` (manual). AI-assisted: `inventory-review.md`, `weekly-summary.md`, `health-triage.md`.

## Learnings

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
