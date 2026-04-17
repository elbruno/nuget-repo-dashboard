# Kaylee — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET, NuGet API, GitHub API, JSON
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Collector** lives in `src/Collector/`. Fetches NuGet + GitHub data, outputs to `data/latest/*.json` and `data/history/YYYY/MM/DD/*.json`.

## Core Context

### Foundation (Learnings)

- **Project structure**: `src/Collector/` is a .NET console app (`Collector.csproj`, **retargeted to net10.0**). Models in `Models/`, services in `Services/`.
- **Environment:** .NET 8.0 and 10.0 runtimes only (no 9.0). Retargeted from net9.0 for deployment compatibility.
- **Serialization**: `System.Text.Json` exclusively with `[JsonPropertyName]` attributes.
- **Architecture:** Interfaces for all services (`IConfigLoader`, `INuGetCollector`, `IGitHubCollector`, `IJsonOutputWriter`) for DI and testability.
- **NuGet API**: Registration endpoint + search endpoint for download counts.
- **GitHub API**: REST with optional `GITHUB_TOKEN` env var; rate-limiting with retry logic (3 attempts, exponential backoff).
- **Output paths**: `data/latest/data.json` + `data/history/{yyyy}/{MM}/{dd}/data.json` (daily history).
- **Repo root resolution**: `AppContext.BaseDirectory` or `DASHBOARD_REPO_ROOT` env var.

### Collector Pipeline (Phase 1–2)

- **Pipeline 1 (Discovery)**: Load config → Discover packages from NuGet profile (`INuGetProfileDiscoveryService`) → Merge tracked packages → Build deduplicated sorted lists
- **Pipeline 2 (Collection)**: Collect NuGet metrics → Collect GitHub metrics → Trend aggregation (Phase 2)
- **NuGet Profile Discovery**: Queries Search API (`owner:{username}`) with pagination, parses GitHub URLs from `projectUrl` using source-generated regex.
- **Dashboard Config** (`config/dashboard-config.json`): Contains `nugetProfile`, `mergeWithTrackedPackages`, `ignorePackages`. Supports environment variable overrides via `NUGET_PROFILE` env var or .NET User Secrets.
- **Split Output** (Phase 1): Two files — `data.nuget.json` (package metrics) and `data.repositories.json` (GitHub repo metrics with 12 enriched fields).
- **User Secrets** (Phase 1): `GITHUB_TOKEN` readable from User Secrets or env var (env var overrides).
- **Trend Aggregation** (Phase 2): `TrendAggregationService` scans history for 90-day rolling windows, outputs `data.trends.json` with package/repo trends, version events.
- **CLI Modes** (Phase 1): `--mode inventory` (Pipeline 1 only, writes `tracked-packages.json`) vs `--mode metrics` (both pipelines, default).

### Dashboard Frontend (Phase 1–2)

- **HTML Dashboard**: Single-file `site/index.html` (~1100 lines, no build tools, no external deps). Fetches `data.nuget.json` + `data.repositories.json` + optional `data.trends.json`.
- **Features**: Package cards (sorted by downloads), repo cards (sorted by stars), dark/light mode, responsive grid, filters (search/sort/language), view toggle (card/list), collapsible sections, sticky nav, top 3 highlights (medals 🥇🥈🥉), trend sparklines (SVG inline).
- **Storage**: localStorage for view mode and section collapse state.
- **XSS safety**: `esc()` helper for all user-derived content.

### RepoIdentity CLI (Phase 1–8)

- **Stack**: System.CommandLine beta4 (`2.0.0-beta4.22272.1`), multi-target net8.0;net10.0
- **Four commands**: `generate` (repo JSON profiles), `preview` (table view), `apply` (copy to theme dir), `install` (device bootstrap)
- **Phase 1–4 foundation**: CLI scaffold → DashboardDataReader → ColorGenerator + MetadataEnricher → ConfigGenerator (per-repo JSON profiles, index.json)
- **Phase 5 implementation**: `preview` command (formatted table), `apply` command (copy profiles, selective `--repo` filtering)
- **Phase 6 enhancements** (now complete): `console_title_template`, solid backgrounds with contrast foreground, purpose-based icons (keyword scan before language fallback), perceptual color spacing (RGB distance ≥ 60), emoji handling fix (surrogate pair unescape)
- **Phase 7 install command** (now complete): Device bootstrap with 4 steps (prereq check, copy profiles, copy script, idempotent $PROFILE patch). Platform-aware paths. `--dry-run` support.
- **Phase 8 script generator** (now complete): `generate` now emits `Set-RepoTheme.ps1` PowerShell activation script (auto-detection + oh-my-posh init)

### Key Technical Decisions

- **ColorGenerator**: SHA256 hash (BCL, no deps) → first 3 bytes → 80-210 brightness range → hex color
- **MetadataEnricher**: Returns null (not throws) when `repo.identity.json` missing/malformed
- **Init-only records**: No `[JsonConstructor]` needed for deserialization
- **String replaces**: `"schema"` → `"$schema"` (System.Text.Json camelCase workaround), `console_title_template` (snake_case key)
- **Emoji in JSON**: Post-serialize regex replaces surrogate pair escapes with literal UTF-8
- **User Secrets**: `optional: true` for CI/CD compat; `Environment.GetEnvironmentVariable()` for env var detection
- **$PROFILE patching**: Idempotent via `# repo-identity:` marker guard
- **Color spacing**: Iterative `EnsureMinDistance` (max 20 iterations, +30R/+15G shift on collision)

### Test Coverage

- **Collector**: 179 total tests (112 unit + 67 integration/configuration/trend)
- **RepoIdentity**: 38 tests (25 unit + 6 integration) on both net8.0 and net10.0 (50 runs total)
- **Test patterns**: MockHttpMessageHandler for HTTP, temp directories for file I/O, InternalsVisibleTo for internal methods, Theory for parametrized tests

---

## Recent Work

### RepoIdentity Phase 6 — Enhanced Profile Generation (2026-07-22)

Phase 6 adds 5 features to `ConfigGenerator.cs`:
1. **`console_title_template`** snake_case key at JSON root
2. **Solid background** using repo accent color, deterministic contrast foreground (`#FFFFFF` or `#1C1C1C` based on NTSC luminance)
3. **Purpose-based icons** via `PurposeIcons` keyword array + `SelectIcon` internal method scanning repo name before language fallback
4. **Perceptually-spaced colors** via `EnsureMinDistance` enforcing minimum RGB distance of 60 between repos (max 20 iterations, +30R/+15G shift on collision)
5. **Emoji handling** fixed — `UnescapeSurrogatePairs` regex replaces surrogate pair escapes with literal UTF-8

Updated `docs/repo-identity.md` with full architecture. All public interfaces unchanged; `SelectIcon` is internal for testing via `InternalsVisibleTo`. All 38 tests pass on both net8.0 and net10.0. Zoe's 13 pre-written tests now passing.

### RepoIdentity Phase 7 — install Command + Install Guide (2026-07-22)

New `InstallCommand.cs` — device bootstrap with 4 steps:
1. Prereq check (`oh-my-posh --version`, 5s timeout, platform-specific install tips on failure)
2. Copy profiles (all `*.json` from `--profiles` dir to `--target`, default `~/.poshthemes`)
3. Copy `Set-RepoTheme.ps1` if present
4. Idempotent patch of PowerShell `$PROFILE` with 3-line snippet guarded by `# repo-identity:` marker

Platform-aware profile paths:
- Windows: `MyDocuments/PowerShell/Microsoft.PowerShell_profile.ps1`
- Unix: `~/.config/powershell/Microsoft.PowerShell_profile.ps1`

Options: `--profiles`, `--target`, `--skip-prereqs`, `--dry-run`

Created `docs/repo-identity-install.md` with cross-device install guide (dependencies, bootstrap, machine modifications, idempotency, dry-run examples, customization, uninstall).

### RepoIdentity Phase 8 — Set-RepoTheme.ps1 Generator (2026-07-22)

Extended `GenerateCommand.cs` to emit PowerShell activation script after JSON generation. Script template stored as C# 11 raw string literal (`"""..."""`) in `GenerateCommand.cs`.

Output: `{outputDirectory}/Set-RepoTheme.ps1` with LF-only line endings

Script flow:
1. `git rev-parse --show-toplevel` finds repo root (silent no-op if not in git repo)
2. `git remote get-url origin` extracts remote
3. Regex match on `github.com[:/](owner/repo)`
4. Reads `~/.poshthemes/index.json`, looks up matching profile entry
5. `oh-my-posh init pwsh --config <file> | Invoke-Expression`

`$ErrorActionPreference = 'SilentlyContinue'` makes it a silent no-op in environments missing git/oh-my-posh/index.json.

Updated `docs/repo-identity.md` with `## Set-RepoTheme.ps1` section. File location: `terminal/ohmyposh/Set-RepoTheme.ps1` (generated, not edited manually).
- **RepoIdentity Phase 5a — preview + apply commands (2026-07-22):** Replaced both stubs with real implementations. `preview` command: reads `data.repositories.json` via `DashboardDataReader`, filters archived repos, prints a formatted table (Repo/Language/Color/Stars columns, 75-char separator) using `ColorGenerator` for deterministic hex colors, reports total count. Uses same `--source` option as `generate` for consistency. `apply` command: `--profiles` defaults to `terminal/ohmyposh/`, `--target` defaults to `~/.poshthemes`, `--repo` for selective single-repo copy (slash→dash sanitization). Copies all non-index `.json` profiles when `--repo` omitted. Guards for missing profiles directory and empty match. Both commands build and smoke test confirmed: 16 profiles listed, build succeeded on net8.0+net10.0. README.md updated with new `## 🎨 repo-identity` section covering all four commands with examples, output description, and optional `repo.identity.json` customization.
- **RepoIdentity Phase 6 — Enhanced Profile Generation (2025-07-22):** Five enhancements to `ConfigGenerator`: (1) `console_title_template` added to root JSON via post-serialize string replace (camelCase→snake_case); (2) solid `Background = color` replaces `"transparent"`, contrasting `Foreground` (`#FFFFFF`/`#1C1C1C`) derived from NTSC luminance; (3) `PurposeIcons` array + `SelectIcon()` method scans repo name for 12 keyword groups before falling back to `LanguageIcons`; (4) `EnsureMinDistance` iterative color-shift algorithm (min Euclidean RGB distance 60, max 20 iterations) pre-generates all colors before writing files; (5) `docs/repo-identity.md` created. Critical lesson: `System.Text.Json` always escapes supplementary Unicode characters (emoji U+10000+) as `\uXXXX\uXXXX` surrogate pairs even with `UnsafeRelaxedJsonEscaping` — added `UnescapeSurrogatePairs()` post-processing with compiled `Regex` to restore literal UTF-8. All 38 tests pass (net8.0 + net10.0). Decision written to `.squad/decisions/inbox/kaylee-phase6-enhancements.md`.
- **RepoIdentity Phase 7 — install command + install guide (2025-07-22):** Created `src/RepoIdentity/Commands/InstallCommand.cs` — the "one-shot device bootstrap" command. Four options: `--profiles` (default: `terminal/ohmyposh`), `--target` (default: `~/.poshthemes`), `--skip-prereqs`, `--dry-run`. Four-step flow: (1) prereq check via `RunProcess("oh-my-posh", "--version")` with platform-specific install hints; (2) copy all `*.json` profile files to target directory; (3) copy `Set-RepoTheme.ps1` if present; (4) idempotent `$PROFILE` patch — appends 3-line snippet if `# repo-identity:` marker not already present, resolves PowerShell profile path per-platform (`MyDocuments/PowerShell/` on Windows, `~/.config/powershell/` on Unix). `Program.cs` updated with `rootCommand.AddCommand(InstallCommand.Create())`. `docs/repo-identity-install.md` created as cross-device install guide (external deps table, bootstrap commands, what changes on machine, idempotency notes, dry-run example, customization, uninstall steps, CLI reference). Build: 0 warnings, 0 errors. `--help` and `--dry-run` both verified working.
- **RepoIdentity Phase 8 — Auto-Detection Script Generation (2025-07-22):** Updated `GenerateCommand.cs` to emit `Set-RepoTheme.ps1` alongside Oh My Posh profile JSON files. Script template stored as a `private const string ScriptTemplate` using a C# 11 raw string literal (`"""..."""`). Written to the output directory with LF-only line endings via `.Replace("\r\n", "\n")`. The script is a cross-platform PowerShell 7+ auto-detection script: uses `git rev-parse --show-toplevel` + `git remote get-url origin` to identify the current repo, looks up `owner/repo` in `~/.poshthemes/index.json`, then calls `oh-my-posh init pwsh --config <file> | Invoke-Expression`. `$ErrorActionPreference = 'SilentlyContinue'` ensures silent no-ops when git/oh-my-posh/index.json are absent. Verified: `generate` command produces `Set-RepoTheme.ps1` with 35 lines and 8 blank lines in `terminal/ohmyposh/`. Updated `docs/repo-identity.md` with a `## Set-RepoTheme.ps1` section covering flow, design decisions, and troubleshooting steps.
- **Dashboard Improvements — Velocity, Issues, Versions (2026-07-23):** Three features added to Collector + frontend. (1) **Download Velocity + Staleness Detection**: `PackageVelocity` class in `TrendData.cs` (avgDailyDownloads over last 7 deltas, staleDays = consecutive trailing zero-delta days, isStale = staleDays >= 3). `ComputeVelocities()` in `TrendAggregationService` runs after download trends are built. Frontend shows amber staleness banner when `stalePackageCount > 0` and velocity per-day rates under top movers. (2) **Issue Activity**: New `GetRecentClosedIssuesAsync` in `GitHubCollector` fetches closed issues (last 30 days) with same retry pattern. `GitHubIssue` gained `State` and `ClosedAt` fields; `GitHubRepoMetrics` gained `RecentClosedIssues` and `ClosedIssuesCount`. `TrendAggregationService.ProcessIssueActivityAsync` counts opened/closed per date from repo snapshots. Frontend renders last-7-days opened/closed table. (3) **Version/Package Publishing**: `ProcessNuGetSnapshotAsync` now tracks first-appearance (newPackages) and version-change-by-date (versionActivity, skipping initial version). Frontend shows recent releases and new package events. Build: 0 warnings, 0 errors. All 198 tests pass.

### Dashboard Improvements — Velocity, Issues, Versions (2026-04-13)

Completed three dashboard metric features in parallel with Zoe's tests:

1. **Download Velocity + Staleness Detection** 
   - `PackageVelocity` class in `TrendData.cs`: `avgDailyDownloads` (last 7 deltas), `staleDays` (consecutive zero deltas), `isStale` (staleDays >= 3)
   - `ComputeVelocities()` runs after download trends built
   - Metrics exposed in `TrendData.packageVelocities`
   - Frontend amber banner when `stalePackageCount > 0`

2. **Issue Activity Tracking**
   - New `GetRecentClosedIssuesAsync()` in `GitHubCollector` (30-day window, same retry pattern)
   - `GitHubIssue` extended: `State` (string?), `ClosedAt` (DateTimeOffset?)
   - `GitHubRepoMetrics` extended: `RecentClosedIssues` (List<GitHubIssue>), `ClosedIssuesCount` (int)
   - `ProcessIssueActivityAsync()` counts opened/closed per date from repo snapshots
   - Frontend 7-day table showing opened/closed counts

3. **Version/Package Publishing Metrics**
   - `ProcessNuGetSnapshotAsync()` tracks first-appearance → `newPackages` events
   - Version-change-by-date → `versionActivity` (excludes initial versions)
   - Frontend shows recent releases timeline

Build: 0 warnings, 0 errors. 198 tests pass (19 new tests from Zoe).

## Learnings

- **TrendAggregationService is the central aggregation point**: All new trend features (velocity, issue activity, version activity, new packages) plug into its main loop and post-processing. Keep the pattern: collect during snapshot iteration, aggregate after the loop.
- **ProcessIssueActivityAsync reads repos snapshot a second time**: This is intentional — the file is small and the repos snapshot contains issue data. Avoids coupling ProcessReposSnapshotAsync to issue-specific dictionaries.
- **Version activity skips initial appearances**: When a package first appears in history, its version is recorded in VersionHistory but NOT in versionActivity (which only tracks upgrades). This avoids flooding versionActivity with 50 "new version" events on the first snapshot date.
- **Staleness threshold is 3 days**: `isStale = staleDays >= 3`. This was chosen to avoid false positives from weekend data gaps while still catching the NuGet API stale-data issue (which froze counts starting April 7).
- **GitHubCollector closed issues use `since` parameter**: The `since` query param is set to 30 days ago in ISO8601 format, keeping the API response bounded.
- **Frontend staleness banner**: Uses inline styles matching the existing pattern. Dark mode override added in the `@media (prefers-color-scheme: dark)` block alongside `.mover-change` styles.
- **NuGet search shards (USNC vs USSC)**: NuGet runs two search shards (`azuresearch-usnc.nuget.org` and `azuresearch-ussc.nuget.org`) that can return different download counts when one becomes stale. The USNC shard froze on April 7, 2026. Querying both shards in parallel and taking the maximum ensures the collector gets the most current data.
- **Parallel shard queries with Task.WhenAll**: `GetTotalDownloadsAsync` now queries both shards simultaneously using `Task.WhenAll`. Each shard uses the existing `GetWithRetryAsync` for resilience. If one shard fails (returns 0), the other's value is used. If both fail, returns 0 (existing behavior).
- **Shard mismatch logging**: When both shards return non-zero but different values, a console log helps diagnose staleness: `[NuGet] Download shard mismatch for {packageId}: USNC={x} USSC={y}, using max={max}`. This is informational only — the collector already picked the correct max value.
- **GetDownloadsFromShardAsync helper**: Extracted the JSON parsing logic into a separate method to avoid duplication. Takes a URL, calls `GetWithRetryAsync`, parses the `data[0].totalDownloads` field, returns 0 on any failure.
- **PR data follows the Issues pattern exactly**: `GetRecentPullRequestsAsync` mirrors `GetRecentIssuesAsync`, `GetRecentMergedPullRequestsAsync` mirrors `GetRecentClosedIssuesAsync`. Both use the same retry logic, JSON parsing style, and 30-day window for merged/closed data.
- **GitHub Pulls API does NOT return additions/deletions/changed_files**: The list endpoint (`GET /repos/{owner}/{repo}/pulls`) omits line-count fields. These are only available from the individual PR endpoint (`GET /repos/{owner}/{repo}/pulls/{number}`). Set to 0 from list endpoint for efficiency — individual calls would be N+1 per repo.
- **PR parsing extracted to `ParseSinglePullRequest` helper**: Reused by both open and merged PR fetching to avoid code duplication. Parses all available fields from the list endpoint.
- **ProcessPullRequestActivityAsync follows ProcessIssueActivityAsync**: Same pattern — reads repos snapshot a second time, counts opened/merged/closed per date from PR data in the snapshot.
- **Frontend PR section mirrors Issues section exactly**: Same HTML structure, same filter/sort logic, same card/list toggle pattern, same localStorage persistence. Added `prsViewMode`, `getAllPrEntries`, `getFilteredPrs`, `renderPrsSection`, `populatePrsRepoDropdown`.
- **PR Activity trend card uses opened/merged/closed**: Unlike issues (opened/closed), PRs track three states. Merged is displayed in accent purple (#6c47ff) to distinguish from closed (green).
- **Draft and review badges**: `.draft-badge` (gray, italic) and `.review-badge` (approved=green, changes-requested=orange, review-required=gray) with dark mode overrides.

### Multi-Shard NuGet Download Query Implementation (2026-04-13)

Modified `NuGetCollector.cs` to resolve April 7 stale USNC shard issue by querying both search shards in parallel:

1. **Dual-shard architecture**
   - Constants: `SearchShardUsnc` (`api-v2v3search-0.nuget.org`), `SearchShardUssc` (`api-v2v3search-1.nuget.org`)
   - `GetTotalDownloadsAsync()` queries both via `Task.WhenAll` for concurrent execution
   - Aggregates result: `max(USNC, USSC)` ensures live data when either shard is fresh

2. **Resilience & fallback**
   - New helper `GetDownloadsFromShardAsync(string url)` handles per-shard JSON parsing + error handling
   - Single shard failure → use other's value
   - Both shards fail → return 0 (preserves existing behavior)

3. **Diagnostic logging**
   - When both shards return different non-zero values: `[NuGet] Download shard mismatch for {packageId}: USNC={x} USSC={y}, using max={max}`
   - Helps identify future staleness issues

4. **Build & test results**
   - Build: 0 warnings, 0 errors
   - Existing tests pass unchanged (no signature changes)
   - Zoe created 11 new integration tests for multi-shard scenarios
   - Test count: 198 → 209
   - All 209 tests passing ✅
- **PR data collection feature (2026-04-13):** Built full PR collection pipeline: new GitHubPullRequest model (17 fields), refactored GitHubCollector (replaced GetOpenPrCountAsync with GetRecentPullRequestsAsync + GetRecentMergedPullRequestsAsync), added PR trend aggregation (PullRequestActivityPoint), updated GitHubRepoMetrics (3 new fields), built Open PRs dashboard section with filters/sort/toggle. Frontend mirrors Issues section UX. Build: 0 warnings, 0 errors. Decision #17 (kaylee-pr-dashboard) merged to decisions.md.

### Monotonicity Guard + Staleness Alert (2026-07-24)

Added two defensive layers to the collector pipeline via new `IMetricsGuardService` / `MetricsGuardService` in `src/Collector/Services/MetricsGuardService.cs`:

1. **Monotonicity Guard (Layer 1)**: `ApplyMonotonicityGuard()` reads previous `data/latest/data.nuget.json` and applies `Math.Max(fresh, previous)` per-package on `totalDownloads`. Ensures download counts never regress even if both NuGet search shards are stale. Logs `[Guard]` when activated.

2. **Staleness Alert (Layer 2)**: `CheckStaleness()` inspects trend data for packages with >100 downloads showing 0% growth over 5+ consecutive data points. Logs `[Staleness]` warnings — advisory only, doesn't block writes.

Both guards are integrated in `Program.cs` after collection + trend aggregation but before writing output files. Interface-based design for testability (InternalsVisibleTo already covers Collector.Tests). Build: 0 warnings, 0 errors. All 227 tests pass. Decision: `.squad/decisions/inbox/kaylee-monotonicity-guard.md`.

**Key patterns:**
- `LoadPreviousNuGetOutputAsync()` returns null when file doesn't exist (first run) — guard becomes a no-op
- `CountTrailingZeroGrowth()` is `internal static` for direct unit testing
- Constants `StalenessThreshold` (5) and `MinDownloadsForStalenessCheck` (100) are `internal const` for test access

- **MetricsGuardService implementation (2026-04-17):** Completed monotonicity guard + staleness alert for download counts. Created `src/Collector/Services/MetricsGuardService.cs` with `IMetricsGuardService` interface. Two layers: (1) `ApplyMonotonicityGuard()` reads previous `data.nuget.json` snapshot, applies `Math.Max(fresh, previous)` per-package, logs `[Guard]` on activation; (2) `CheckStaleness()` reads trend data, detects packages >100 downloads with 5+ consecutive zero-growth days, logs `[Staleness]` warnings (advisory). Both guards in `Program.cs` post-collection/trend, pre-output. Zero regression risk. Test count: 227 → 257 (all pass). Decision #19 merged to decisions.md.
