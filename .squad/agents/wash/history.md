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
- Builds & runs Collector at `src/Collector/Collector.csproj` (.NET 9)
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
