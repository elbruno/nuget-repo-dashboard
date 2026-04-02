# Dashboard Filters & View Modes

**Author:** Kaylee (Backend Dev)
**Date:** 2026-07-22
**Status:** Implemented

## Context

Bruno requested filters and view modes for the dashboard to help navigate 36+ packages and 16+ repos. The existing dashboard was a static card grid with no interactivity beyond scroll.

## Decision

Added per-section filter toolbars and card/list view toggle to `site/index.html`, keeping the single-file architecture.

### Key Design Points

1. **Client-side filtering** — data is fetched once and stored in `allPackages`/`allRepos` JS arrays. Filters re-render from these arrays without re-fetching.
2. **NuGet filters:** live search (by name), sort dropdown (Downloads/Name A-Z/Name Z-A/Newest), min-downloads pill buttons (All/1K+/10K+/100K+).
3. **Repo filters:** live search (by name), sort dropdown (Stars/Forks/Issues/PRs/Name/Updated), language dropdown (auto-populated from data), toggle buttons for "has open issues" and "has open PRs".
4. **View modes:** Card (grid) and List (table) per section. Persisted in `localStorage` keys `nuget-view` and `repo-view`.
5. **Immediate application** — no "Apply" button; all filters trigger instant re-render via input/change/click events.
6. **Result count** — "Showing X of Y packages/repositories" displayed in each section header.
7. **Empty state** — friendly "No items match your filters" message when filters produce zero results.
8. **Design consistency** — new `--toolbar-bg` CSS variable for filter bar background, matching existing dark/light mode scheme.
9. **XSS safety** — added `esc()` helper for HTML-escaping user-derived content in templates.

## Impact

- **Modified:** `site/index.html` (345 → 808 lines)
- **No backend changes** — purely frontend
- **Deployment:** Same single-file deploy via GitHub Pages
