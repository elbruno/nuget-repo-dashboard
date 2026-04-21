# SKILL: Diagnose Metrics Collection Stall

## Purpose

Investigate why dashboard metrics collection appears stalled or showing stale data. This skill provides a systematic diagnostic workflow for identifying whether the issue is with CI/CD pipeline execution, data collection logic, or local workspace synchronization.

## When to Use

- User reports dashboard showing "no progress" or stale data
- Data timestamps in `data/latest/*.json` are days/weeks old
- Manual workflow triggers appear to do nothing
- Scheduled workflow runs seem to have stopped

## Diagnostic Workflow

### Step 1: Verify Workflow Execution Status

**Tools:** GitHub MCP server or `gh` CLI

```bash
# List recent workflow runs
github-mcp-server-actions_list \
  --method list_workflow_runs \
  --owner <owner> \
  --repo <repo> \
  --resource_id refresh-metrics.yml \
  --per_page 10

# Check for patterns:
# - Are runs being triggered? (check `event: schedule` vs `workflow_dispatch`)
# - Are they completing? (check `status: completed`)
# - Are they succeeding? (check `conclusion: success` vs `failure`)
# - Check timestamps (created_at, updated_at)
```

**What to look for:**
- ✅ Runs present, status=completed, conclusion=success → Workflow is working
- ❌ No recent runs → Check schedule config or workflow disabled status
- ❌ Runs failing → Proceed to Step 2 (logs)

### Step 2: Analyze Failed Job Logs (if applicable)

If runs show `conclusion: failure`:

```bash
# Get failed job logs
github-mcp-server-get_job_logs \
  --run_id <run_id> \
  --failed_only true \
  --return_content true \
  --owner <owner> \
  --repo <repo>

# Search logs for:
# - Build failures
# - Collector exceptions
# - API rate limits
# - Git push failures
# - Authentication errors
```

**Common failure patterns:**
- `dotnet build` fails → Collector code issue (delegate to Kaylee)
- `Rate limit exceeded` → GitHub API throttling (wait or optimize)
- `git push rejected` → Branch protection or permissions issue
- `Exception` in Collector run → Business logic bug (delegate to Kaylee)

### Step 3: Check Workflow Configuration

If no runs are triggering:

```bash
# View workflow file
view .github/workflows/refresh-metrics.yml

# Check:
# - Schedule cron expression (e.g., "0 9 * * *")
# - Workflow not commented out or disabled
# - Branch filter (e.g., branches: [main])
```

**Common config issues:**
- Incorrect cron syntax (test at crontab.guru)
- Workflow file in wrong branch
- Workflow disabled in repo settings

### Step 4: Verify Local vs Remote Data Sync

**This is the most common root cause when workflows show success but data appears stale.**

```bash
# Check local data timestamps
Get-ChildItem data/latest/*.json | Select Name, LastWriteTime

# Check remote commits
git fetch origin main
git log HEAD..origin/main --oneline

# Look for commits like:
# "chore: refresh metrics [skip ci]"
# "chore: refresh Oh My Posh profiles [skip ci]"
```

**Pattern:** If workflow logs show successful data writes, but local files are stale, and `git log HEAD..origin/main` shows recent commits → **workspace is out of sync**.

**Resolution:**
```bash
git pull origin main
# Verify timestamps updated
Get-ChildItem data/latest/*.json | Select Name, LastWriteTime
```

### Step 5: Verify Collector Output in Logs

Even if workflow succeeds, check Collector actually wrote data:

```bash
# In workflow logs, search for:
grep "Written:" <log_file>
grep "data.nuget.json" <log_file>
grep "No data changes detected" <log_file>

# Expected output:
# Written: /path/to/data/latest/data.nuget.json
# Written: /path/to/data/history/YYYY/MM/DD/data.nuget.json
```

**Red flags:**
- "No data changes detected" on every run → Collector may not be fetching new data
- No "Written:" lines → Collector failed silently or didn't run
- "Skipping history snapshot" → Check staleness logic in Collector

### Step 6: Cross-Reference Workflow Run Time vs Data Write Time

```bash
# Workflow run timestamp (from GitHub API)
created_at: 2026-04-21T10:17:30Z

# Collector log timestamp
Generated at: 2026-04-21T10:18:18+00:00

# Local file timestamp (convert to UTC)
LastWriteTime: 4/17/2026 10:29:24 AM

# If workflow timestamp is recent but file timestamp is old → sync issue
```

## Common Root Causes (Ranked by Frequency)

1. **Workspace out of sync** (80% of cases)
   - Local repo hasn't pulled latest commits
   - Uncommitted local changes block pull
   - Resolution: `git pull origin main`

2. **Collector not writing data** (10%)
   - API rate limiting (GitHub or NuGet)
   - Collector logic bug (throws exception, exits early)
   - Resolution: Check Collector logs, delegate to Kaylee

3. **Workflow not running** (5%)
   - Schedule syntax error
   - Workflow disabled in repo settings
   - Branch mismatch
   - Resolution: Check workflow config, re-enable if needed

4. **Git commit/push failure** (3%)
   - Branch protection rules blocking bot commits
   - Permissions issue
   - Resolution: Check workflow permissions, branch protection

5. **Data unchanged (by design)** (2%)
   - NuGet/GitHub APIs returning identical data
   - Collector's `git diff --cached --quiet` detecting no changes
   - Not a bug; no action needed

## Outputs

After running diagnostics, report:

1. **Status:** WORKING | FAILING | MISCONFIGURED | OUT_OF_SYNC
2. **Root cause:** Clear, evidence-based explanation
3. **Evidence:** Workflow run IDs, log excerpts, timestamps
4. **Recommended action:** Specific fix or next step
5. **Urgency:** CRITICAL (blocking data collection) | MODERATE (delayed) | LOW (sync issue)

## Example Report

```
STATUS: OUT_OF_SYNC

ROOT CAUSE:
Workflow is executing successfully and writing fresh data to GitHub every day
at 09:00 UTC. Local workspace is 10 commits behind origin/main due to
uncommitted changes blocking git pull.

EVIDENCE:
- Workflow run #42 (2026-04-21 10:17 UTC): success
- Collector logs show data written to data/latest/*.json
- Local data timestamps: 2026-04-17 10:29 AM
- Git log HEAD..origin/main: 10 commits (4/18-4/21)

RECOMMENDED ACTION:
Run `git pull origin main` to sync workspace. If uncommitted changes exist,
stash or commit them first.

URGENCY: LOW (pipeline is healthy; user just needs to sync)
```

## Automation Opportunity

This diagnostic flow could be codified as:
1. GitHub Actions workflow for self-diagnosis (runs diagnostics, comments on issue)
2. CLI tool (`dotnet run -- diagnose-stall`)
3. Scheduled health check (daily report if staleness detected)

See `.squad/decisions/inbox/wash-metrics-stall-diagnosis-2026-04-21.md` for recommendations.

---

**Created:** 2026-04-21  
**Author:** Wash  
**Last Updated:** 2026-04-21  
**Version:** 1.0
