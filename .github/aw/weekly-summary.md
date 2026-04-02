# Weekly Summary Workflow

## Description

This GitHub Agentic Workflow generates a comprehensive weekly summary of NuGet packages and GitHub repository metrics. It highlights trends, notable changes, and ecosystem activity across all tracked packages and repositories.

⚠️ **This workflow is non-authoritative and advisory only.** Summaries are intended for informational purposes and should be verified against primary data sources.

## Triggers

- **Schedule:** Every Monday at 09:00 UTC
- **Manual:** Can be triggered on demand via `workflow_dispatch`

## Data Sources

The workflow reads from:

- `data/latest/data.json` — Current metrics snapshot
- `data/history/` — Historical data for trend comparison (Week-over-week, Month-over-month)
- `config/tracked-packages.json` — List of tracked packages and their mapped repos

## Behavior

### 1. Aggregate Latest Metrics

Load the latest data snapshot containing:
- NuGet package metrics (downloads, version count, last updated)
- GitHub repository metrics (stars, forks, open issues, open PRs, watchers)
- Relationships between packages and repos

### 2. Calculate Trends

Compare latest data against the previous week to identify:
- **Download Changes:** Packages with significant download increases/decreases (>10% week-over-week)
- **Version Activity:** Packages with new major/minor/patch releases this week
- **Repository Activity:** Repos with new issues, PRs, or stars trending upward
- **Ecosystem Growth:** Net change in tracked packages and repos

### 3. Generate Summary Sections

The workflow creates a markdown summary with:

#### Top 5 Packages by Downloads
```markdown
| Rank | Package | Downloads (This Week) | Change (WoW) |
|------|---------|----------------------|--------------|
| 1 | Newtonsoft.Json | 50M | +8% |
| 2 | Serilog | 25M | -2% |
| 3 | ... | ... | ... |
```

#### Notable Changes
- 📈 Spike (>20% increase): `Package X` downloads jumped from 1M to 1.3M
- 🎉 Major Release: `Package Y` released v8.0.0 with breaking changes
- 📉 Decline (>15% decrease): `Package Z` downloads decreased to historical low
- 🔴 Repository Alert: `owner/repo` now has 200+ open issues (up from 150)

#### Repository Activity
```markdown
## Most Active Repos (This Week)
- `aspnet/aspnetcore`: 12 new issues, 8 new PRs, 250 new stars
- `dotnet/runtime`: 5 new issues, 3 new PRs, 180 new stars
- ...
```

#### Ecosystem Snapshot
```markdown
## Dashboard Summary
- **Total Tracked Packages:** 42
- **Total Mapped Repos:** 38
- **New Packages Added:** 2
- **New Repos Added:** 1
- **Packages with Updates:** 8
```

### 4. Create GitHub Issue

The workflow creates a **GitHub issue** with:
- Title: `📊 Weekly Summary — {YYYY-MM-DD}`
- Labels: `weekly-summary`, `dashboard`, `automated`
- Content: Formatted markdown summary as described above
- Assignee: Optional (if configured)

The issue is posted as a publicly visible record of ecosystem metrics.

## Example Output

```markdown
# 📊 Weekly Summary — 2025-01-13

## Top 5 Packages by Downloads

| Rank | Package | Downloads (WoW) | Change |
|------|---------|-----------------|--------|
| 1 | Newtonsoft.Json | 50,234,567 | +8.2% |
| 2 | Serilog | 25,123,456 | -1.5% |
| 3 | AutoMapper | 18,567,890 | +3.1% |
| 4 | Dapper | 15,234,567 | +0.8% |
| 5 | log4net | 12,456,789 | -4.2% |

## Notable Changes

### 📈 Notable Increases
- **Entity Framework Core** jumped 18% (8.2M → 9.7M downloads)
- **MediatR** released v12.3.0 with performance improvements

### 🎉 Major Releases
- **ASP.NET Core** v9.0 now fully supported
- **Polly** v8.5 adds new resilience patterns

### 📉 Notable Decreases
- **log4net** declined 4.2% as teams migrate to Serilog

### 🔴 Repo Alerts
- `dotnet/aspnetcore`: 287 open issues (+15 this week)
- `JamesNK/Newtonsoft.Json`: 95 open PRs awaiting review

## Most Active Repos (This Week)

1. `aspnet/aspnetcore` — 8 issues, 12 PRs merged, 310 stars
2. `dotnet/runtime` — 5 issues, 6 PRs merged, 185 stars
3. `serilog/serilog` — 2 issues, 3 PRs merged, 42 stars

## Ecosystem Snapshot

- **Tracked Packages:** 42
- **Tracked Repos:** 38
- **New Releases:** 8 packages
- **New Stars:** 2,847 total
- **Active Issues:** 412 across all repos
```

## Non-Authoritative Advisory

This workflow:
- ✅ CAN: Summarize, analyze trends, highlight notable changes
- ✅ CAN: Post informational issues for visibility and discussion
- ❌ CANNOT: Make decisions on package deprecation or repo removal
- ❌ CANNOT: Modify tracked-packages.json or production data
- ❌ CANNOT: Take automated action based on detected anomalies (see `health-triage.md` for that)

## Configuration

This workflow requires:
- GitHub CLI (`gh`) with write access to create issues
- Read access to `data/latest/` and `data/history/` directories
- NuGet API access (public endpoint)
- Proper authentication for GitHub API calls

## Error Handling

If data is incomplete or unavailable:
- Post issue with note: "_Note: Some metrics unavailable this week. Analysis based on available data._"
- Flag missing data sources in the issue
- Do not fail silently; surface all limitations to the reader

## Workflow Integration

This workflow is part of the larger dashboard ecosystem:
- **Depends on:** `refresh-metrics.yml` (provides latest/history data)
- **Related:** `inventory-review.md`, `health-triage.md`
- **Consumed by:** Team discussion, external users monitoring the dashboard
