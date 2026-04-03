# Decision: Historical Trend Aggregation Architecture

**Author:** Kaylee (Backend Dev)
**Date:** 2026-07-22
**Status:** Implemented

## Context

Phase 2 requires time-series trend data for sparkline visualizations on the dashboard. Daily snapshots already exist in `data/history/YYYY/MM/DD/` but no aggregation existed.

## Decision

1. **Trends are derived data** — `data.trends.json` is written to `data/latest/` only, NOT to history. It's regenerated each run from the raw snapshots.

2. **90-day rolling window** — Keeps output size manageable. Configurable via `windowDays` parameter.

3. **No interpolation** — Missing days are simply skipped (gaps in the sparkline). This avoids introducing fake data points.

4. **Version history tracking** — Detects version changes between consecutive snapshots to mark release events.

5. **Dashboard fetches trends optionally** — If `data.trends.json` doesn't exist (first run, or no history), sparklines are hidden gracefully. The dashboard never fails on missing trends.

6. **Pure SVG sparklines** — No charting library. Inline SVG polylines drawn from data points, styled with CSS variables for theme support.

## Alternatives Considered

- **Chart.js / external library**: Rejected — violates the no-build-tools, no-npm constraint.
- **Store trends in history too**: Rejected — derived data shouldn't duplicate in history; wastes storage and creates consistency issues.
- **Interpolate missing days**: Rejected — introduces synthetic data; gaps are more honest.

## Impact

- New files: `Models/TrendData.cs`, `Services/TrendAggregationService.cs`
- Modified: `JsonOutputWriter.cs` (new `WriteTrendsAsync`), `Program.cs` (step 3/3), `site/index.html` (+77 lines CSS, +120 lines JS)
- New output: `data/latest/data.trends.json`
- Pipeline 2 now 3 steps instead of 2
