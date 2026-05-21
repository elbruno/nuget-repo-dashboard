# Wash — History

## Summary

Agent history summarized 2026-05-21. See history-archive.md for prior entries.

*Last 10 recent entries preserved in active history.*

---

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

## Recent Sessions

### Push to GitHub (2026-04-22)
**Objective:** Push all local changes to GitHub  
**Session:** Task completion — Bruno requested commit and push of watch list system and team documentation

**Changes pushed:**
- `config/WATCH-LIST.md` — Documentation for external reference repository tracking system
- `.squad/agents/mal/history.md` — Updated with watch list pattern learnings
- `.squad/casting-policy.json` — Agent capability registry
- `.squad/constraint-tracking.md` — Constraint documentation
- `.squad/raw-agent-output.md`, `.squad/run-output.md` — Workflow artifacts
- `.squad/scribe-charter.md` — Scribe agent charter

**Commit:** e501b32 — "docs: add watch list system and update team history" with Co-authored-by trailer  
**Status:** ✅ Pushed to `origin/main`

## Learnings

### GitHub Actions PR Creation Authentication (2026-04-23)

**Problem:** `refresh-inventory.yml` failed with "GitHub Actions is not permitted to create or approve pull requests (createPullRequest)" when attempting to create a PR via `gh pr create`.

**Root Cause:** GitHub's security policy explicitly blocks `GITHUB_TOKEN` (automatic Actions token) from creating PRs on **public repositories**. This is documented behavior, not a bug.

**Reasoning Behind GitHub's Limitation:** 
- Prevents unauthorized PR spam from compromised workflows
- Enforces explicit author accountability (PAT must be from a known user account)
- Protects against supply chain attacks via automated PR injection

**Solution Pattern:** 
1. For PR creation in public repos, use a Personal Access Token (PAT) with explicit scopes
2. Store PAT in repository secrets (never commit)
3. Use fallback pattern: `${{ secrets.REPO_PAT || secrets.GITHUB_TOKEN }}` for graceful degradation

**Key Scopes for PAT:**
- `repo` — full repository access (required for PR operations on public repos)
- `workflow` — optional but recommended to bypass rate limits in CI workflows

**Security Best Practices for Automation Secrets:**
- Use "classic" PATs (simpler for Actions) with explicit scopes, not fine-grained tokens
- Rotate every 90 days (set calendar reminders)
- Check audit trail regularly at https://github.com/settings/security-log
- Revoke immediately if compromised
- Never test with personal tokens; use dedicated bot/service account PAT if possible

**Reusability:** This pattern (fallback to optional PAT) is applicable to any workflow needing PR creation, label creation on behalf of user, or other operations blocked by GITHUB_TOKEN limitations.

**File Changed:** `.github/workflows/refresh-inventory.yml` (line 18, fallback pattern for GITHUB_TOKEN)

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