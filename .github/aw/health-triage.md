# Health Triage Workflow

## Description

This GitHub Agentic Workflow automatically detects anomalies and health issues across tracked packages and repositories. It identifies stale packages, repositories with high issue counts, download anomalies, and other ecosystem health concerns.

⚠️ **This workflow is non-authoritative and advisory only.** All detected issues are flags for human review and decision-making. The workflow does not take corrective action automatically.

## Triggers

- **Schedule:** Daily at 02:00 UTC
- **Manual:** Can be triggered on demand via `workflow_dispatch`

## Data Sources

The workflow reads from:

- `data/latest/data.json` — Current metrics
- `data/history/` — Historical data for anomaly detection
- `config/tracked-packages.json` — List of tracked packages and repos
- GitHub API — Repository information and status

## Detection Rules

### 1. Stale Packages

**Condition:** A package has not released a new version in 6+ months.

**Detection Logic:**
- Compare last release date in NuGet against current date
- Flag if: `(today - lastReleaseDate) > 6 months`

**Data Points Included:**
- Package name and last release date
- Days since last release
- Version count (total versions available)
- Download trend (is this package still being downloaded?)

**Example Issue:**
```markdown
## 🟡 Stale Package Alert: log4net

**Status:** No new releases in 7+ months
**Last Release:** 2024-06-15 (225 days ago)
**Last Version:** 2.0.14
**Weekly Downloads:** 2.1M (↓ 3% from last month)

**Recommendation:** 
- Check if maintainer is still active
- Suggest users migrate to modern alternatives (Serilog, NLog)
- Consider removing from dashboard if abandoned

**Link:** [NuGet Package](https://www.nuget.org/packages/log4net)
```

### 2. High Issue Count Repositories

**Condition:** A repository has an above-threshold number of open issues or high issue-to-resolution ratio.

**Detection Logic:**
- Calculate issue density: `openIssues / totalIssues` (recent window: last 90 days)
- Flag if: `openIssues > 200` OR `densityRatio > 0.6`
- Rank by severity (highest first)

**Data Points Included:**
- Current open issues count
- Open PRs count
- Issue closure rate (resolved issues in last 30 days)
- Repository activity level
- Age of oldest open issue

**Example Issue:**
```markdown
## 🔴 High Issue Load: dotnet/aspnetcore

**Open Issues:** 287 (↑ 15 from last week)
**Open PRs:** 92
**Issue Resolution Rate:** 45% (resolved 23 of 51 new issues last week)
**Oldest Unresolved Issue:** 127 days old
**Repository Activity:** High (recent commits, active maintainers)

**Recommendation:**
- No action required if community is engaged
- If unresponsive, consider escalating to team
- Monitor for burnout indicators

**Health:** 🟡 Elevated but manageable
**Link:** [Issues](https://github.com/dotnet/aspnetcore/issues)
```

### 3. Download Anomalies

**Condition:** Significant week-over-week or month-over-month download changes.

**Detection Logic:**

**Sharp Increases (>50% WoW):**
- Flag as potential viral growth or external event
- Check if legitimate (new release, community event)
- Ensure data is not corrupted

**Sharp Decreases (>25% WoW):**
- Flag as potential deprecation or user migration
- Cross-reference with related packages (users migrating?)
- Check release notes for breaking changes

**Data Points Included:**
- Current weekly downloads
- Week-over-week change (%)
- Month-over-month trend
- Historical average
- Comparison to related packages

**Example Issue (Spike):**
```markdown
## 📈 Download Spike: NewtonSoft.Json

**Current Downloads (WoW):** 50.2M → 56.8M (+13.2%)
**Trend:** Consistent growth over past month
**Related Events:** v13.0.4 released 3 days ago

**Recommendation:**
- Likely due to new release or external promotion
- Monitor for sustainability
- Check GitHub for discussions or announcements

**Status:** ✅ Normal growth detected
```

**Example Issue (Drop):**
```markdown
## 📉 Download Decline: old-package

**Current Downloads (WoW):** 1.2M → 0.8M (-33%)
**Trend:** Declining over past 3 months
**Related Context:** No releases in 8 months

**Recommendation:**
- Assess if package is being deprecated
- Look for user migration to alternatives
- Consider marking as legacy/archived

**Status:** 🟡 Investigate deprecation status
```

### 4. Archived Repositories

**Condition:** A repository in the tracked list is marked as archived on GitHub.

**Detection Logic:**
- Query GitHub API for `archived` status
- Flag any repos where `archived == true`
- Check when it was archived

**Data Points Included:**
- Repository name and URL
- Archive date
- Last activity before archive
- Number of packages mapped to this repo

**Example Issue:**
```markdown
## 🔴 Archived Repository: JamesNK/Newtonsoft.Json

**Status:** Repository archived 2024-11-01
**Reason:** Project moved; use System.Text.Json instead
**Last Activity:** 2024-10-28
**Packages Mapping to This Repo:** 2 (Newtonsoft.Json, ...)

**Recommendation:**
- Update package documentation to note archived status
- Suggest migration to active forks or alternatives
- Consider removing from dashboard if users have migrated

**Action Required:** Yes — review mapped packages and update guidance
**Link:** [Repository](https://github.com/JamesNK/Newtonsoft.Json)
```

### 5. Repository Metadata Changes

**Condition:** Significant changes in repository metadata (description, topics, visibility).

**Detection Logic:**
- Track changes in: stars count, description, topics, language, license
- Flag if: stars drop sharply, language changes, license changes

**Example Issue:**
```markdown
## ℹ️ Repository Metadata Change: some/repo

**Change:** License changed from MIT to Apache-2.0
**Date:** 2024-12-10
**Impact:** Potential compliance implications for users

**Recommendation:**
- Review licensing impact
- Update documentation if needed

**Status:** 📋 Informational (no action required unless license conflict)
```

## Issue Creation

For each anomaly detected:

1. **One Issue Per Anomaly Type** (consolidated, not spam)
   - Stale Packages: Single issue listing all stale packages
   - High Issue Repos: Single issue listing all problem repos
   - Download Anomalies: Single issue listing spikes and drops
   - Archived Repos: Single issue listing all archived repos

2. **Issue Format:**
   - Title: `🏥 Health Triage — {Category}` (e.g., `🏥 Health Triage — Stale Packages`)
   - Labels: `health-triage`, `dashboard`, `automated`
   - Assignee: Optional (if configured)
   - Severity indicator in title or body

3. **Data-Driven Rationale**
   - Every issue includes specific metrics and comparisons
   - Links to relevant GitHub issues, PRs, and NuGet pages
   - Clear recommendations for next steps
   - Timestamp of detection

## Non-Authoritative Advisory

This workflow:
- ✅ CAN: Detect anomalies, flag health concerns, post informational issues
- ✅ CAN: Provide data-driven analysis and recommendations
- ❌ CANNOT: Remove packages or repos from tracking
- ❌ CANNOT: Close or archive repositories
- ❌ CANNOT: Modify tracked-packages.json
- ❌ CANNOT: Block merges or enforce policy

All detected issues require human review before taking corrective action.

## Configuration

This workflow requires:
- GitHub CLI (`gh`) with write access to create issues
- GitHub API token with repo read access
- Read access to `data/latest/` and `data/history/` directories
- NuGet API access (public endpoint)

## Error Handling

If data sources are unavailable:
- Skip that detection rule for this run
- Post informational issue: "_Note: Some checks unavailable today (data source: X). Full report next run._"
- Do not create false positives due to missing data

## Workflow Integration

This workflow is part of the larger dashboard ecosystem:
- **Depends on:** `refresh-metrics.yml` (provides metrics data)
- **Related:** `weekly-summary.md`, `inventory-review.md`
- **Workflow Output:** GitHub issues for team discussion and action
