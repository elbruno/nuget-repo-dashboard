# Squad Decisions Archive

Archived entries (decision entries older than 2026-05-14).

---

### 1. PRD Decomposition: NuGet + GitHub Dashboard

**Author:** Mal  
**Date:** 2025-01-26  
**Status:** Approved  

The PRD defines 14 prioritized work items across three phases: Foundation (P0), Core Features (P1), and Enhancements (P2). Implementation sequence prioritizes data models and infrastructure before collectors, then workflows, then AI-assisted features. See `docs/nuget-dashboard-prd-v2.md`.

---


---

### 2. Backend Architecture — System.Text.Json + Interface-Based Services

**Author:** Kaylee (Backend Dev)  
**Date:** 2025-07-15  
**Status:** Implemented  

Decisions:
1. **System.Text.Json only** — no Newtonsoft dependency. All models use `[JsonPropertyName]` attributes.
2. **Interface-per-service** — `IConfigLoader`, `INuGetCollector`, `IGitHubCollector`, `IJsonOutputWriter`.
3. **NuGet dual-endpoint strategy** — Registration API for metadata, Search API for download counts.
4. **GitHub optional auth** — `GITHUB_TOKEN` env var optional; works for public repos.
5. **Repo root via env var** — `DASHBOARD_REPO_ROOT` enables CI/CD flexibility.

---


---

### 3. Agentic Workflow Architecture (WI-7, WI-8, WI-9)

**Owner:** Wash (DevOps)  
**Date:** 2025-01-14  
**Status:** Implemented  

Agentic workflows (WI-7, WI-8, WI-9) are defined as **markdown specifications**, not executable YAML. Non-authoritative by design:
- ✅ CAN: Analyze, detect, suggest, create informational issues
- ❌ CANNOT: Modify production data, make unilateral decisions

**Three workflows:** inventory-review (PR comments), weekly-summary (trends), health-triage (anomalies).

---


---

### 4. Inventory Workflow Uses Branch+PR Pattern

**Author:** Wash (DevOps)  
**Date:** 2025-01-27  
**Status:** Implemented  

Inventory changes go through `inventory/refresh-{date}` branch and PR with human review checklist. Every discovery requires manual merge; new packages arrive with empty `repos: []` requiring reviewer to fill mappings.

---


---

### 5. Retarget to net10.0

**Author:** Zoe (Tester)  
**Date:** 2026-04-02  
**Status:** Implemented  

Environment has .NET 8.0 and 10.0 runtimes only (no 9.0). Retargeted both `src/Collector/Collector.csproj` and `tests/Collector.Tests/Collector.Tests.csproj` from `net9.0` to `net10.0` for test execution and deployment.

**Impact:** All 51 unit tests pass on net10.0. No code changes needed beyond TFM swap.

---


---

### 6. NuGet Profile Auto-Discovery

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
**Status:** Implemented

## Context

The dashboard was tracking Microsoft packages (`Microsoft.Extensions.AI.*`) that aren't owned by the user (`elbruno`). The static `tracked-packages.json` required manual maintenance and didn't reflect the user's actual NuGet portfolio.

## Decision

Replace the static-only config with a **profile discovery flow** that dynamically discovers packages from a NuGet user profile at runtime.

### Key Design Points

1. **New config file** `config/dashboard-config.json` with `nugetProfile` and `mergeWithTrackedPackages` fields.
2. **New service** `INuGetProfileDiscoveryService` queries the NuGet Search API (`owner:{username}`) with pagination support.
3. **GitHub repo resolution** — parses `projectUrl` from NuGet metadata to extract `owner/repo` using source-generated regex.
4. **Merge strategy** — discovered packages are primary; `tracked-packages.json` supplements with manually tracked packages that aren't in the profile (e.g., Microsoft packages the user wants to watch).
5. **Pipeline expanded** from 5 steps to 6: config → discovery → merge → NuGet metrics → GitHub metrics → output.
6. **Backward compatible** — if `dashboard-config.json` is missing or `nugetProfile` is empty, falls back to `tracked-packages.json` only.

## Impact

- **New files:** `config/dashboard-config.json`, `src/Collector/Models/DashboardConfig.cs`, `src/Collector/Models/DiscoveredPackage.cs`, `src/Collector/Services/NuGetProfileDiscoveryService.cs`
- **Modified:** `src/Collector/Program.cs` (refactored from 5-step to 6-step pipeline)
- All 71 existing tests continue to pass.

---


---

### 7. Package Ignore List in Dashboard Config

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
**Status:** Implemented

## Context

The dashboard was collecting packages the user doesn't own — `LocalEmbeddings` from NuGet profile discovery and `Microsoft.Extensions.AI.*` from `tracked-packages.json`. Bruno needed a way to exclude unwanted packages without modifying code.

## Decision

Add an `ignorePackages` array to `config/dashboard-config.json` and `DashboardConfig` model. After discovery and merge (step 3), Program.cs filters out any package whose `packageId` matches the ignore list using case-insensitive comparison.

### Key Design Points

1. **Config-driven** — ignore list lives in `dashboard-config.json`, not hardcoded.
2. **Case-insensitive** — uses `StringComparer.OrdinalIgnoreCase` HashSet for matching.
3. **Post-merge filtering** — applied after both discovery and tracked-package merge, so it catches packages from any source.
4. **Defaults to empty** — `IgnorePackages` defaults to `[]` so existing configs work without change.
5. **Console logging** — reports filtered count for transparency.

## Impact

- **Modified:** `config/dashboard-config.json`, `src/Collector/Models/DashboardConfig.cs`, `src/Collector/Program.cs`, `config/tracked-packages.json`
- **Result:** 37 discovered → 1 filtered → 36 collected, all ElBruno-owned packages.

---


---

### 8. Split Output Pipeline + Enriched GitHub Metrics

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
**Status:** Implemented

## Context

The Collector previously wrote a single `data.json` file containing both NuGet package metrics and GitHub repository metrics in one combined `DashboardOutput` model. Bruno requested:

1. **Split the output** into two separate files for cleaner data consumption by the frontend
2. **Enrich GitHub metrics** with additional public repository stats from the GitHub API

## Decision

### Output Architecture

Created two new output models and corresponding JSON files:

- **`NuGetOutput`** → `data.nuget.json` — contains `generatedAt` + `packages` array
- **`RepositoriesOutput`** → `data.repositories.json` — contains `generatedAt` + `repositories` array

Both files written to `data/latest/` and `data/history/YYYY/MM/DD/`.

### Updated JsonOutputWriter Interface

Changed from single `WriteAsync(DashboardOutput)` to:
```csharp
Task WriteNuGetAsync(NuGetOutput output, string repoRoot);
Task WriteRepositoriesAsync(RepositoriesOutput output, string repoRoot);
```

Each method writes to both latest and history paths.

### Enriched GitHubRepoMetrics

Added 12 new properties from `/repos/{owner}/{repo}` endpoint:

- `watchersCount`, `topics`, `createdAt`, `updatedAt`, `size`, `defaultBranch`, `homepage`, `hasWiki`, `hasPages`, `networkCount`, `visibility`, `htmlUrl`

All parsed in `GitHubCollector.CollectRepoAsync()` — no additional API calls.

### Program.cs Update

Step [6/6] now builds both `NuGetOutput` and `RepositoriesOutput` from a shared `generatedAt` timestamp, then calls both writer methods.

## Impact

- **Modified:** `Models/GitHubRepoMetrics.cs`, `Models/DashboardOutput.cs`, `Services/GitHubCollector.cs`, `Services/JsonOutputWriter.cs`, `Program.cs`
- **Output:** `data/latest/data.nuget.json` (36 packages) + `data.repositories.json` (3 repos)
- **Tests:** 71 → 94 (net +23 for split output + enriched fields)
- **Build:** ✅ Compiles, all 94 tests pass on net10.0

---


---

### 10. NuGet Profile Configurability Analysis

**Author:** Coordinator
**Date:** 2026-04-02
**Status:** Analysis Only (Not Implemented)

## Context

Current implementation stores `nugetProfile` in static `config/dashboard-config.json`. User requested analysis of making this configurable via environment variable for different deployment scenarios.

## Analysis Summary

### Pros of Environment Variable Configuration

1. **Deployment flexibility** — different profiles per environment (dev, staging, prod)
2. **CI/CD-friendly** — no config file needed; set via secrets/parameters
3. **Runtime override** — env var can override config file without code/config changes
4. **12-factor app compliance** — configs stored in environment, not files

### Cons of Environment Variable Configuration

1. **Configuration discovery** — users accustomed to file-based config; env vars less discoverable
2. **Documentation burden** — requires explicit documentation of env var name and defaults
3. **Validation** — file-based JSON schema provides better type safety than string env vars
4. **Precedence complexity** — multiple config sources (file + env var) can be confusing

## Recommendation

**Current design (file-based) is appropriate for now:**
- Dashboard is meant to be configured once per repository
- Profile doesn't change frequently during execution
- JSON schema provides validation and documentation
- Future: If multi-tenant scenarios emerge, revisit environment variable support

**No implementation performed** — user explicitly requested analysis only.

---


---

### 12. GitHub Pages Deployment via refresh-metrics.yml

**Author:** Wash (DevOps)  
**Date:** 2026-04-02  
**Status:** Implemented

## Context

The dashboard needs a public-facing site. The Collector already produces `data/latest/data.nuget.json` and `data/latest/data.repositories.json`. A frontend (`site/index.html`) will consume these files. We need automated deployment so the dashboard stays current after every metrics refresh.

## Decision

Deploy to GitHub Pages directly from the `refresh-metrics.yml` workflow using a second job (`deploy-dashboard`) that runs after `collect`.

### Key Design Points

1. **Single workflow** — no separate deploy workflow. The `deploy-dashboard` job chains after `collect` via `needs: collect`, keeping the pipeline atomic.
2. **Site assembly** — copies `site/index.html` + `data/latest/*.json` into `_site/` with flattened paths (`_site/data/data.nuget.json`). Frontend fetches from relative `data/` path.
3. **GitHub Pages via Actions** — uses `actions/upload-pages-artifact@v3` + `actions/deploy-pages@v4` (OIDC token-based, no deploy keys needed).
4. **Permissions** — added `pages: write` and `id-token: write` at workflow level alongside existing `contents: write`.
5. **.NET version fix** — corrected from `9.0.x` to `10.0.x` to match the project's net10.0 target framework.
6. **Manual setup required** — repo owner must enable GitHub Pages with "GitHub Actions" source in Settings → Pages.

### Alternatives Considered

- **Separate deploy workflow** — rejected; adds complexity and timing issues (needs to wait for data commit).
- **Azure Static Web Apps** — viable future option, but GitHub Pages is simpler for a public dashboard with no server-side logic.
- **gh-pages branch** — legacy pattern; Actions-based deploy is the modern approach and avoids branch pollution.

## Impact

- **Modified:** `.github/workflows/refresh-metrics.yml`, `README.md`
- **New permissions:** `pages: write`, `id-token: write`
- **Prerequisite:** Repo Settings → Pages → Source = "GitHub Actions"
- **URL:** `https://elbruno.github.io/nuget-repo-dashboard/`

---


---

### 18. Download Velocity, Staleness Detection, Issue Activity, Version Tracking

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-13  
**Status:** Implemented

## Context

The NuGet Search API has been returning stale download counts since April 7 (frozen at 18,439 total across 50 packages). We needed to detect and surface this data quality issue, plus add issue opened/closed tracking and version/package publishing visibility.

## Decisions


---

### 19. Multi-Shard NuGet Download Query Resilience

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-13  
**Status:** Implemented

## Context

The NuGet Search API runs two independent search shards for geographic/performance redundancy:
- **USNC** (`api-v2v3search-0.nuget.org`) — froze on April 7, 2026; returns stale download counts
- **USSC** (`api-v2v3search-1.nuget.org`) — live, up-to-date data

The Collector was hardcoded to query only USNC, causing stale data propagation to the dashboard.

## Decision

Modified `NuGetCollector.GetTotalDownloadsAsync()` to query **both shards in parallel** and use the **maximum** download count from either response.

### Key Design Points

1. **Parallel queries** — `Task.WhenAll` queries both shards simultaneously to minimize latency (2x requests but concurrent)
2. **Max aggregation** — returns `max(USNC, USSC)` ensuring users get the most current data from whichever shard is live
3. **Fallback resilience** — if one shard fails (HTTP error or parse exception), uses the other's value; if both fail, returns 0 (existing behavior)
4. **Transparency** — logs mismatches to console when USNC ≠ USSC for diagnostic visibility
5. **Zero breaking changes** — method signature `GetTotalDownloadsAsync()` unchanged; tests pass without modification

### Implementation Details

- **New constants** (lines 16–17): `SearchShardUsnc` and `SearchShardUssc` URLs
- **Helper method** (lines 144–165): `GetDownloadsFromShardAsync(string url)` for shard-specific JSON parsing + error handling
- **Main method** (lines 166–188): Calls both shards via `Task.WhenAll`, aggregates results
- **Log format**: `[NuGet] Download shard mismatch for {packageId}: USNC={x} USSC={y}, using max={max}`

### Rationale

**Why not switch to USSC-only?**
- If USSC becomes stale in future, we'd have the same problem again. Dual-shard ensures resilience to future staleness in either shard.

**Why parallel instead of sequential?**
- Reduces latency. Sequential would add ~200ms per package (multiply by 50 packages = significant delay).

**Why max aggregation instead of primary+fallback?**
- Simpler logic. Query both, take max. No ordering assumptions between shards.
- Both shards are equally authoritative — no reason to prefer one's staleness over the other's liveness.

## Testing

- **11 new tests** in `NuGetCollectorMultiShardTests.cs` cover:
  - Both shards return data → max aggregation
  - Individual shard failures → fallback to healthy shard
  - Both shards fail → returns 0
  - Same value on both shards → no mismatch log
  - Empty data arrays → treated as 0
  - HTTP error codes (404, 500, 503)
- **Build:** ✅ 0 warnings, 0 errors
- **Test count:** 198 → 209 (net +11)
- **All tests passing** ✅

## Impact

- **Modified:** `src/Collector/Services/NuGetCollector.cs`
- **New tests:** `tests/Collector.Tests/NuGetCollectorMultiShardTests.cs`
- **Output:** No change to data format; improved data quality (stale → live) for frozen shards
- **Backward compatible:** Existing tests pass without modification
- Trends section expanded from 2 to 4 cards: Total Downloads, Top Movers + Velocity, Issue Activity table, Recent Releases
- All new data rendered via existing vanilla JS patterns (no new dependencies)

## Impact

- **Modified models:** `TrendData.cs`, `GitHubIssue.cs`, `GitHubRepoMetrics.cs`
- **Modified services:** `TrendAggregationService.cs`, `GitHubCollector.cs`
- **Modified frontend:** `site/index.html`
- **Build:** 0 warnings, 0 errors
- **Tests:** 19 new tests added, all 198 existing tests pass (no regressions)

---


---

### 21. 2025-01-20: Responsive Design Verification & Testing

**Author:** Zoe (QA Tester)  
**Date:** 2025-01-20  
**Status:** Approved

Comprehensive testing of responsive design across 6 breakpoints: 320px (iPhone SE), 375px (iPhone 6/7/8), 480px (Android), 768px (iPad), 1024px (iPad Pro), 1200px+ (Desktop).

**Results:** ✅ **APPROVED** — All tests pass.
- Mobile breakpoint: Header, cards, toolbar text doesn't overflow; readable at 320px
- Tablet breakpoint: 2-column grid, balanced spacing
- Desktop breakpoint: Full-width auto-fill layout, generous padding
- Dark mode works correctly across all viewports
- No horizontal overflow or awkward wrapping
- Typography scales appropriately
- Touch targets ≥ 30px at all breakpoints

Dashboard ready for production deployment.

---


---

### 22. 2026-04-22: Responsive Design Commit — Push to Main

**Author:** Wash (DevOps)  
**Date:** 2026-04-22  
**Status:** Deployed

Pushed responsive design fixes to `origin/main` (commit SHA 7456497).

**Commit Details:**
- Files: `site/index.html` (147 insertions, 52 deletions)
- Changes: CSS-only responsive fixes; no logic changes
- Pre-verified by Zoe: All breakpoints tested and approved
- Risk Level: Low (styling only)

**Push Status:** ✅ Successfully pushed to origin/main. Branch up to date.

---


---

### 23. 2025-04-02: Introduce Watch List Configuration

**Author:** Mal (Lead Architect)  
**Date:** 2025-04-02  
**Status:** Implemented

Created `config/watch-list.json` to track external GitHub repositories the project monitors for reference, architectural insights, and integration opportunities.

**Schema:**
- `owner`: GitHub user/org
- `repo`: Repository name
- `url`: Full GitHub URL
- `description`: Brief purpose/description
- `dateAdded`: ISO 8601 date of addition
- `purpose`: Why this repo is being watched

**Initial Entry:** `elbruno/openclawnet` — Reference architecture and AI-assisted workflow patterns.

**Design:** Non-authoritative watch list is informational; may seed agentic workflows (inventory-review, weekly-summary) but does not drive data collection.

---


---

### 24. 2025-04-02: Watch List System for External Reference Repos

**Author:** Mal (Lead Architect)  
**Date:** 2025-04-02  
**Status:** Accepted

Designed lightweight JSON-based watch list system (`config/watch-list.json`) with human-readable documentation (`config/WATCH-LIST.md`).

**Design Rationale:**
- **Format:** JSON — machine-readable, version-controllable, extensible
- **Location:** `config/watch-list.json` — colocated with project config
- **Documentation:** Markdown guide explaining schema, how to add repos, examples
- **Manual entry process** — low friction, no validation scripts required
- **Future automation possible:** Validation jobs, changelog workflows, freshness checks

**Requirements Met:**
- Easy to add repos (4-step manual process)
- Self-documenting (clear schema + examples)
- Scalable (JSON array, works from 1 to 50+ repos)
- Zero ceremony (no scripts or CI gates)

**Trade-offs:** No schema validation initially; can add when manual process becomes a burden.

---


---

### 25. 2026-04-22: Watch List System Deployment & Squad Infrastructure

**Author:** Wash (DevOps)  
**Date:** 2026-04-22  
**Status:** Implemented & Pushed

Deployed watch list system documentation and Squad infrastructure files to `origin/main` (commit e501b32).

**Files Committed:**
1. `config/WATCH-LIST.md` — Watch list system documentation
2. Squad infrastructure files:
   - `.squad/casting-policy.json` — Agent role/capability registry
   - `.squad/constraint-tracking.md` — Constraints tracking
   - `.squad/raw-agent-output.md` — Agentic workflow artifacts
   - `.squad/run-output.md` — Execution logs and diagnostics
   - `.squad/scribe-charter.md` — Scribe agent responsibilities
3. `.squad/agents/mal/history.md` — Watch list pattern documented

**Design:** Watch list system provides minimal-friction configuration for monitoring external GitHub repositories as reference implementations and pattern sources. Extensible for future automation (validation, changelog, freshness checks).

---


---

### 26. 2026-05-04: Investigation — Metrics Collection Status

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-05-04  
**Status:** Verified — No Action Required

Investigation into apparent data collection stall (no commits since April 24, 2026) concluded: **FALSE ALARM**.

**Finding:** Data collection working correctly and committing daily. Root cause of confusion: PowerShell's `ConvertFrom-Json` formatted ISO 8601 date `"2026-05-04T11:04:31..."` as `04/24/2026` instead of May 4, 2026.

**Actual Status:** ✅ All systems operational
- Latest commit: 2026-05-04 11:04:31 UTC
- History files present for: 05/04, 05/03, 05/02, 05/01, 04/30, 04/29, 04/28, etc.
- Workflow runs daily at 09:00 UTC (04:00 AM EST)
- All recent runs show "success" status
- 81 packages tracked, 23 repos monitored
- Metrics Guard Service prevents download count regressions

**Technical Notes:** `.gitignore` tracked files are committed via `git add --force` in workflow. Monotonicity Guard ensures data integrity by taking max of fresh vs. previous values.
1. **Prereq check** — `RunProcess("oh-my-posh", "--version")` with 5-second timeout. On failure, prints platform-specific install instructions (`winget` on Windows, `brew` on macOS). Skipped with `--skip-prereqs`.
2. **Copy profiles** — all `*.json` files (including `index.json`) from `--profiles` to `--target`. Uses `File.Copy(overwrite: true)`.
3. **Copy Set-RepoTheme.ps1** — if present in profiles dir; warns to run `generate` if absent.
4. **Patch `$PROFILE`** — idempotent append of 3-line snippet, guarded by `# repo-identity:` marker. Profile path is platform-aware: `MyDocuments/PowerShell/Microsoft.PowerShell_profile.ps1` on Windows, `~/.config/powershell/Microsoft.PowerShell_profile.ps1` on Unix.

**Idempotency:** Re-running `install` overwrites profile JSON files (safe — deterministic generation) but never duplicates the `$PROFILE` snippet.

### `docs/repo-identity-install.md` (new)

Cross-device install guide covering:
- External dependencies table (.NET, Oh My Posh, PowerShell 7, Windows Terminal, Git)
- Oh My Posh install instructions for Windows / macOS / Linux
- Two-command bootstrap (`git clone` + `dotnet run -- install`)
- Exact list of what changes on the machine (4 rows: JSON profiles, index.json, Set-RepoTheme.ps1, $PROFILE)
- Idempotency guarantees
- `--dry-run` usage with example output
- `repo.identity.json` customization (accentColor, icon, type)
- Uninstall instructions
- Full CLI reference

## Rationale

The `install` command consolidates what would otherwise be a manual 4-step process (copy files, edit $PROFILE, create directories) into a single idempotent command. `--dry-run` makes it safe to inspect on new devices before committing. The `$PROFILE` marker guard prevents duplicate snippet injection on re-runs, which is the main footgun in auto-patching shell profiles.

## Alternatives Considered

- **Shell script instead of C# command** — rejected; keeping bootstrap entirely within the CLI avoids bash/PowerShell cross-platform fragility and keeps the tool self-contained.
- **Separate `patch-profile` subcommand** — rejected; folding it into `install` gives users a single command to remember for new device setup.

---


---

### 29. HTML Dashboard — Single-File Architecture

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
**Status:** Implemented

## Context

Bruno requested a frontend dashboard to visualize the Collector's JSON output. The dashboard needs to display NuGet package metrics and GitHub repository stats from the split output files (`data.nuget.json`, `data.repositories.json`).

## Decision

Created `site/index.html` as a **single-file HTML dashboard** with all CSS and JS inline.

### Key Design Points

1. **Zero dependencies** — no build tools, no CDN imports, no npm. Just one HTML file.
2. **Relative data URLs** — fetches `data/data.nuget.json` and `data/data.repositories.json` from sibling `data/` directory. Deployment just needs the `site/` folder with the `data/` folder alongside or inside it.
3. **Dark/light mode** — uses `prefers-color-scheme` media query with CSS variables. No toggle button needed; respects OS preference.
4. **Responsive** — CSS grid with `auto-fill` and `minmax(340px, 1fr)` for card layout. Single column on mobile.
5. **NuGet-inspired palette** — blues and purples via CSS variables for easy customization.
6. **Sort order** — packages by total downloads desc, repos by stars desc.
7. **Error UX** — loading spinner during fetch, friendly error message if JSON fails to load.

## Features

- NuGet package cards sorted by downloads (descending)
- GitHub repository cards sorted by stars (descending)
- Summary cards (package count, repo count, total downloads)
- Package tags and repository topics as badges
- Comma-formatted numbers and human-readable timestamps
- Links to NuGet.org and GitHub profiles
- System font stack (no external dependencies)

## Impact

- **New file:** `site/index.html` (~13KB)
- **No backend changes** — consumes existing JSON contract from Collector
- **Deployment:** Copy `site/index.html` + `data/` folder to any static host (GitHub Pages, Azure Static Web Apps, etc.)

---


---

### 30. Dashboard Filters & View Modes

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
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

---


---

### 19. Monotonicity Guard + Staleness Alert for Download Counts

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-17  
**Status:** Implemented

## Context

The NuGet Search API uses geo-replicated shards (USNC + USSC) that reindex independently. Even with the Math.Max dual-shard fix, both shards can occasionally be stale simultaneously, causing the collector to write download counts **lower** than previously stored values. Since download counts are monotonically increasing, this is always wrong.

Additionally, prolonged staleness across both shards can go unnoticed for days.

## Decision

### Layer 1: Monotonicity Guard (critical)

- New `IMetricsGuardService` / `MetricsGuardService` in `src/Collector/Services/`
- `ApplyMonotonicityGuard()`: before writing output, reads previous `data/latest/data.nuget.json` and applies `Math.Max(fresh, previous)` per-package on `totalDownloads`
- Logs `[Guard]` when the guard activates (collected < stored)
- Designed as interface-based service for testability (InternalsVisibleTo already covers test project)

### Layer 2: Staleness Alert (advisory)

- `CheckStaleness()`: reads trend data, checks packages with >100 total downloads for 5+ consecutive zero-growth data points
- Logs `[Staleness]` warnings to console — visible in CI logs
- Advisory only: does not block writes

### Integration

Both guards run in `Program.cs` after collection and trend aggregation but **before** writing output files. The monotonicity guard mutates metrics in-place; the staleness check is read-only.

## Impact

- Zero regression risk: guard only ever increases values, never decreases
- CI logs surface API staleness issues automatically
- Both methods are testable via the interface
- Test suite: 30 new tests (MonotonicityGuardTests: 13, StalenessAlertTests: 17)
- Total tests: 227 → 257 (all passing)

---


---

### 33. Workspace Sync Health Check

**Author:** Wash (DevOps)  
**Date:** 2026-04-21  
**Status:** Recommendation  

## Context

Bruno reported dashboard showing no progress for several days. Investigation revealed:
- GitHub Actions `refresh-metrics.yml` workflow was running successfully (daily at 09:00 UTC)
- Fresh data was being committed to GitHub every day
- **Root cause:** Local workspace was out of sync with remote (`origin/main`)
- User's local `data/latest/*.json` files were stale (April 17) while remote had April 18-21 commits

## Problem

When users have uncommitted changes or haven't pulled in a while, they see stale data locally and assume the CI pipeline is broken. This wastes diagnostic time and creates false alarms.

## Recommendation

Add a workspace health check that surfaces sync status. Three options evaluated:

### Option A: Pre-commit hook (via Husky or similar)
```bash
# .husky/pre-commit
git fetch origin main --quiet
BEHIND=$(git rev-list --count HEAD..origin/main)
if [ "$BEHIND" -gt 0 ]; then
  echo "⚠️  Local workspace is $BEHIND commits behind origin/main"
  echo "   Run 'git pull origin main' to sync latest data"
fi
```

### Option B: CLI health command ⭐ Preferred
Add to `src/Collector/Collector.csproj` or a separate `WorkspaceHealth.csproj` tool:
```bash
dotnet run --project tools/WorkspaceHealth -- check
```
Outputs:
- Local data timestamps vs latest workflow run timestamp
- Git sync status (ahead/behind/diverged)
- Recommendation: pull, commit, or push

### Option C: Dashboard footer
Add timestamp metadata to `site/index.html` footer with comparison of local copy vs server data.

## Trade-offs

| Option | Pros | Cons |
|--------|------|------|
| A: Pre-commit hook | Automatic reminder | Requires Husky or manual setup; git-centric |
| B: CLI health command | On-demand, clear output, CI-friendly | User must remember to run it |
| C: Dashboard footer | Visible in UI, no CLI needed | Requires frontend changes, doesn't fix root cause |

## Decision

**Deferred to Mal (team lead)** — this is an architectural/UX decision. Preference: **Option B (CLI health command)** seems most pragmatic (non-invasive, can be run on-demand or in CI, useful for debugging without requiring git expertise).

## Next Steps

1. Review with Mal and Kaylee
2. If approved, assign implementation to appropriate squad member
3. Document usage in `docs/troubleshooting.md`

---

**Evidence:**
- Workflow logs: https://github.com/elbruno/nuget-repo-dashboard/actions/runs/24716973089
- Git log: 10+ commits behind (2026-04-18 to 2026-04-21)
- See `wash/history.md` § "Metrics Stall Investigation (2026-04-21)" for full diagnostic trail



---



