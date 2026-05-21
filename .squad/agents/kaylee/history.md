# Kaylee — History

## Summary

Agent history summarized 2026-05-21. See history-archive.md for prior entries.

*Last 10 recent entries preserved in active history.*

---

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
