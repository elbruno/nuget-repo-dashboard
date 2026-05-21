# Squad Decisions

## Active Decisions

### 9. .NET User Secrets for GITHUB_TOKEN

**Author:** Kaylee (Backend Dev)
**Date:** 2026-07-22
**Status:** Implemented

## Context

The Collector read `GITHUB_TOKEN` exclusively from `Environment.GetEnvironmentVariable()`. Bruno wanted a more ergonomic local development experience — storing the token via .NET User Secrets so he doesn't need to set environment variables every session.

## Decision

Integrate `Microsoft.Extensions.Configuration` with User Secrets and Environment Variables providers into the Collector's `Program.cs`.

### Key Design Points

1. **Configuration builder** — `ConfigurationBuilder` chains `AddUserSecrets()` then `AddEnvironmentVariables()`, so env vars override secrets if both are set.
2. **Assembly-based secrets loading** — used `AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)` instead of the string-based overload, which didn't resolve in .NET 10 preview. The `UserSecretsId` is set in the csproj.
3. **`optional: true`** — app doesn't crash if no secrets file exists (CI/CD, fresh clones).
4. **Backward compatible** — environment variables still work exactly as before.
5. **No secrets in source** — User Secrets are stored in the OS user profile, outside the repo.

### Packages Added

- `Microsoft.Extensions.Configuration` 10.0.0-preview.5
- `Microsoft.Extensions.Configuration.UserSecrets` 10.0.0-preview.5
- `Microsoft.Extensions.Configuration.EnvironmentVariables` 10.0.0-preview.5

## Impact

- **Modified:** `src/Collector/Collector.csproj`, `src/Collector/Program.cs`, `README.md`
- **Build:** ✅ Compiles on net10.0
- **Tests:** ✅ All 112 tests pass (no regressions)

---


---

### 11. Configurable NuGet Profile Override

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

The `nugetProfile` was only configurable via the static `config/dashboard-config.json` file. Bruno requested the ability to override it at runtime for different deployment scenarios (CI/CD, local development, testing with different profiles).

## Decision

Support overriding `nugetProfile` via `NUGET_PROFILE` environment variable or .NET User Secrets, with clear precedence rules.

### Key Design Points

1. **Precedence:** Environment Variable > User Secrets > config file default (`dashboard-config.json`).
2. **Single `ConfigurationBuilder`** — moved to early in Program.cs (before Pipeline 1) so it serves both `NUGET_PROFILE` and `GITHUB_TOKEN`. No duplicate configuration building.
3. **Source reporting** — Pipeline 1 step [1/3] displays the active profile and its source (`config file`, `environment variable`, or `user secret`) for transparency.
4. **Source detection** — since `IConfiguration` merges all providers, we check `Environment.GetEnvironmentVariable("NUGET_PROFILE")` directly to distinguish env var from user secret.
5. **Single-profile design** — README documents that the dashboard targets one NuGet user at a time. Multi-profile is a future consideration.

## Impact

- **Modified:** `src/Collector/Program.cs`, `README.md`
- **No new packages** — reuses existing `Microsoft.Extensions.Configuration.*` packages
- **Build:** ✅ Compiles on net10.0
- **Tests:** ✅ All 140 tests pass (112 + 28 new ConfigurationTests)

---


---

### 13. RepoIdentity Phase 1 — CLI Scaffolding

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Phase 1 of the `repo-identity` tool establishes the foundational CLI scaffold in the existing `nuget-repo-dashboard` repository. The tool will eventually generate Oh My Posh profile configs from the tracked NuGet dashboard repo data.

## Decision 1: System.CommandLine beta4 (`2.0.0-beta4.22272.1`)

**Chosen:** `System.CommandLine` Version `2.0.0-beta4.22272.1`

**Rationale:**
- Stable enough for production CLI tooling in the .NET ecosystem
- Full support for `net8.0` and `net10.0` (the two runtimes present in this environment)
- Provides typed `Option<T>` with default-value factories — cleanly handles `--source` and `--target` with sensible defaults without manual argument parsing
- Already used widely in .NET CLI tools; aligns with project style

**Alternatives considered:**
- `Cocona` — heavier DI-based framework; overkill for a single-purpose CLI tool
- `CliFx` — less ecosystem adoption; would be unfamiliar to the team
- Manual `args[]` parsing — fragile, no `--help` generation

## Decision 2: Multi-target `net8.0;net10.0`

**Chosen:** `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`

**Rationale:**
- The environment has .NET 8 (LTS) and .NET 10 runtimes only (no 9.0)
- net8.0 is the active LTS release — ensures broadest developer compat
- net10.0 is the latest; used by the Collector and ensures feature parity
- Consistent with the project convention documented in Kaylee's history
- Tests run against both TFMs to catch TFM-specific regressions early

**Alternatives considered:**
- `net10.0` only — risks excluding devs on net8.0 LTS machines
- `net9.0;net10.0` — net9.0 is not present in this environment (confirmed)

## Decision 3: CLI command structure — `generate` / `preview` / `apply` / `install`

**Chosen four top-level subcommands:**

| Command | Purpose | Phase |
|---------|---------|-------|
| `generate` | Parse repo data, produce `oh-my-posh.generated.json` | 1 → 4 (full impl) |
| `preview` | Print table of what would be generated | 1 → 5 (full impl) |
| `apply` | Copy generated profiles to theme directory | 1 → 5 (full impl) |
| `install` | One-shot device bootstrap with profile patching | 7 (new) |

**Rationale:**
- Separating `generate` and `apply` follows the "dry run vs execute" pattern — users can inspect output before writing to their system config
- `preview` provides a read-only UX entry point for CI/CD pipelines and curious devs
- `--source` defaults to the Collector's standard output path so zero-config use is possible
- `--target` defaults to `~/.poshthemes` (common Oh My Posh convention for custom themes)
- `install` command consolidates multi-step device setup into a single idempotent command

---


---

### 14. RepoIdentity Phase 2 — DashboardDataReader

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Phase 2 of the RepoIdentity CLI requires reading `data/latest/data.repositories.json` (produced daily by the Collector) into a strongly-typed C# model list. Two key design decisions were made during implementation.

## Decision 1: Init-only property records for JSON deserialization

**Chosen approach:** Use init-only property records rather than positional record syntax for `RepositoryInfo` and `DashboardData`.

```csharp
// ✅ Chosen — works with System.Text.Json out of the box
public record RepositoryInfo
{
    public string Owner { get; init; } = string.Empty;
    // ...
}

// ❌ Rejected — requires [JsonConstructor] on every record
public record RepositoryInfo(string Owner, ...);
```

**Rationale:** Positional (primary constructor) records require `[JsonConstructor]` to tell `System.Text.Json` which constructor to use when deserializing. Init-only property records work without any attribute because the deserializer can use the parameterless constructor and then set properties via their setters. This keeps models clean and free of serialization concerns.

**Trade-off:** Init-only records are slightly more verbose but eliminate a common source of deserialization bugs when constructors change.

## Decision 2: System.Text.Json (no extra NuGet dependency)

**Chosen approach:** Use `System.Text.Json` from the .NET SDK with `PropertyNameCaseInsensitive = true`.

**Rationale:** `System.Text.Json` is bundled with every .NET 8+ runtime — zero additional dependencies. The JSON produced by the Collector uses camelCase property names (standard JS convention); `PropertyNameCaseInsensitive = true` handles the camelCase → PascalCase mapping automatically without requiring `[JsonPropertyName]` attributes on every model property.

**Alternative considered:** `Newtonsoft.Json` — rejected because it adds a NuGet dependency for no benefit here. The Collector already uses `System.Text.Json` for its own models.

## Files Affected

- `src/RepoIdentity/Models/RepositoryInfo.cs` (new)
- `src/RepoIdentity/Models/DashboardData.cs` (new)
- `src/RepoIdentity/Services/IDashboardDataReader.cs` (new)
- `src/RepoIdentity/Services/DashboardDataReader.cs` (new)
- `tests/RepoIdentity.Tests/DashboardDataReaderTests.cs` (new)

---


---

### 15. RepoIdentity Phase 3 — ColorGenerator + MetadataEnricher

**Date:** 2026-07-22  
**Author:** Kaylee (Backend Dev)

## Decision 1: Use SHA256 for deterministic color generation (not MD5)

**Choice:** SHA256 via `SHA256.HashData()` (BCL, no external dependency)

**Rationale:**
- SHA256 is available in the BCL on all .NET 8+ targets — no NuGet package needed
- Provides excellent distribution across the color space (256-bit hash, first 3 bytes used for R/G/B)
- Deterministic: same seed always produces the same hash and therefore the same color
- More collision-resistant than MD5, though for color generation this is minor; SHA256 is preferred as the modern standard
- `SHA256.HashData()` static API avoids `IDisposable` lifecycle management

**Rejected alternatives:**
- MD5: deprecated, not FIPS-compliant, no advantage for this non-security use case
- FNV/custom hash: requires additional implementation complexity with no benefit

## Decision 2: Return null (not throw) when repo.identity.json is missing or malformed

**Choice:** `MetadataEnricher.TryReadAsync()` returns `null` on missing file or JSON parse failure

**Rationale:**
- `repo.identity.json` is explicitly optional — its absence is the normal case for most repos
- Callers use a `Try*` pattern (method name `TryReadAsync`) which signals nullable return semantics
- Throwing exceptions on missing files would require try/catch at every call site
- Malformed JSON (e.g. incomplete file, encoding errors) should not crash the enrichment pipeline — graceful degradation is correct
- Consistent with the `TryParse` / `TryGet` patterns throughout .NET BCL

**Implementation:** The `catch (Exception)` block swallows all exceptions and returns null. File existence is checked with `File.Exists()` before attempting to open — this handles the missing file case without an exception at all.

## Decision 3: EnrichedRepoInfo combines base repo data with identity overrides

**Choice:** `EnrichedRepoInfo` is a flat record that merges fields from `RepositoryInfo` (GitHub data) and `RepoIdentityMetadata` (optional identity override)

**Rationale:**
- A single flat record is simpler for downstream consumers (dashboard rendering, JSON serialization) than a nested object
- `AccentColor` is always populated: if `RepoIdentityMetadata.AccentColor` is set, use it; otherwise use the `ColorGenerator` output. This ensures no null checks needed at render time
- `Icon` and `Type` are nullable — they come from identity metadata only, with no fallback
- The enrichment step (combining base + metadata + generated color) happens in a single place, keeping callers clean

**Fields:**
- From base repo: `Owner`, `Name`, `FullName`, `Description`, `Language`, `Stars`, `HtmlUrl`
- Computed/enriched: `AccentColor` (required, never null), `Icon` (optional), `Type` (optional)

---


---

### 16. RepoIdentity Phase 4 — ConfigGenerator + generate command

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Phase 4 completes the `generate` command of the `repo-identity` CLI. The generator must turn `data.repositories.json` into per-repo Oh My Posh JSON theme files so terminal prompts can reflect which repo is active.

## Decisions


---

### 17. RepoIdentity Phase 5 — preview + apply commands

**Date:** 2026-07-22  
**Author:** Kaylee (Backend Dev)  
**Status:** Implemented

## Decisions


---

### 19. RepoIdentity Phase 6 — Enhanced Profile Generation

**Author:** Kaylee
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Phase 5 delivered working `generate`, `preview`, and `apply` commands for Oh My Posh profile generation. Phase 6 addresses 7 gaps identified in analysis to make the generated profiles more visually useful and complete.

## Changes Implemented

### Change 1 — `console_title_template`

Added `ConsoleTitleTemplate` to the anonymous serialization object in `ConfigGenerator.GenerateAsync`. Because `System.Text.Json` with `JsonNamingPolicy.CamelCase` emits `consoleTitleTemplate`, a string replace is applied post-serialization to produce the snake_case key Oh My Posh requires: `console_title_template`.

### Change 2 — Solid background + contrasting foreground

Replaced `Background = "transparent"` with `Background = color` (the repo's accent color). Added `Foreground = contrastColor` where contrast is determined by NTSC luminance formula (`0.299r + 0.587g + 0.114b`) / 255. Colors with luminance < 0.5 get white (`#FFFFFF`) foreground; ≥ 0.5 get dark (`#1C1C1C`) foreground.

### Change 3 — Purpose-based icons

Added `PurposeIcons` static readonly array of `(string[] Keywords, string Icon)` tuples with 12 entries covering common repo name patterns (mcp, whisper, tts, embedding, qr, realtime, vision, llm, agent, nuget, dashboard, api). Added `SelectIcon(string repoName, string? language)` internal method that checks keywords against `repo.Name.ToLowerInvariant()` before falling back to `LanguageIcons` dictionary.

### Change 4 — Perceptually-spaced colors

Added `EnsureMinDistance` iterative method (max 20 iterations) that enforces minimum Euclidean RGB distance of 60 between any two assigned colors. Colors that collide are shifted by +30 R / +15 G (mod 130 in the 80-210 range). Colors are pre-generated before profile writing so the distance check covers all repos in one pass. Added `ParseRgb` helper to parse `#RRGGBB` hex strings to `(int r, int g, int b)`.

### Change 5 — Documentation

Created `docs/repo-identity.md` documenting the profile structure, icon mapping table, color generation algorithm, and CLI commands.

### Incidental fix — Emoji in JSON

Discovered that `System.Text.Json` always escapes supplementary Unicode characters (emoji, U+10000+) as `\uXXXX\uXXXX` surrogate pairs, even with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Added `UnescapeSurrogatePairs` method using a compiled `Regex` to replace surrogate pair escape sequences with literal UTF-8 characters in the serialized JSON output. This makes profiles human-readable and allows test string-contains assertions to work correctly.

## Interfaces

All public interfaces (`IConfigGenerator`) are **unchanged**. `SelectIcon` is `internal` to support Zoe's tests directly via `InternalsVisibleTo`. `SanitizeFileName` was already internal.

## Tests

13 tests were already written by Zoe (in `ConfigGeneratorTests.cs`) and were failing before this implementation:
- `GenerateAsync_ProfileHasConsoleTitleTemplate`
- `GenerateAsync_SegmentHasSolidBackground`
- `GenerateAsync_SegmentHasContrastForeground`
- `GenerateAsync_IconMatchesPurposeOrLanguage` (9 theory cases)
- `GenerateAsync_MultipleReposHaveDistinctColors`

All 38 tests now pass on both net8.0 and net10.0.

## Tests Zoe may need to update

The `PipelineIntegrationTests.FullPipeline_ColorsAreDeterministic_OnMultipleRuns` test reads `segment.foreground` and checks it's the same between two runs. Previously `foreground` was the accent color; now it's the contrast color (`#FFFFFF` or `#1C1C1C`). The test still passes because contrast is deterministically derived from the accent color — but the *semantic meaning* of the assertion has changed. Zoe may wish to update the test to check `background` for accent color determinism and `foreground` for the contrast value check.

## Alternatives Considered

- **`[JsonPropertyName]` on strongly-typed record**: Would avoid string replaces but requires defining a full `ProfileDocument` class hierarchy — more code for less benefit at this scale.
- **Custom `JsonConverter`**: Overly complex for this use case.
- **Keep `"transparent"` background**: Rejected — all 16 repos are C# so they all got 🔷 with no visual differentiation.

---


---

### 19. RepoIdentity Phase 7 — install Command + Install Guide

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## What Was Built

### `src/RepoIdentity/Commands/InstallCommand.cs` (new)

New `install` command — the "one-shot device bootstrap". Registered in `Program.cs` via `rootCommand.AddCommand(InstallCommand.Create())`.

**Options:**
- `--profiles <dir>` — source directory with generated profiles (default: `terminal/ohmyposh` in CWD)
- `--target <dir>` — destination directory (default: `~/.poshthemes`)
- `--skip-prereqs` — skip oh-my-posh availability check
- `--dry-run` — print every action without executing

**Four-step flow:**

---


---

### 20. RepoIdentity Phase 8 — Set-RepoTheme.ps1 Generator

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

`repo-identity generate` already writes Oh My Posh profile `.json` files and `index.json` to `terminal/ohmyposh/`. To complete the terminal theme auto-detection flow, the `generate` command also needs to emit `Set-RepoTheme.ps1` — the PowerShell activation script that reads `index.json` at shell start and applies the matching theme for the current git repo.

## Decision

Extend `GenerateCommand.cs` to write `Set-RepoTheme.ps1` to the output directory immediately after `ConfigGenerator.GenerateAsync()` completes.

## Implementation Details

- **Script template location:** `private const string ScriptTemplate` in `GenerateCommand.cs` as a C# 11 raw string literal (`"""..."""`). Keeps the script version-controlled alongside the C# code that generates it.
- **Line endings:** Written with LF-only (`\n`) via `.Replace("\r\n", "\n")`. PowerShell 7+ handles LF on all platforms.
- **Output path:** `{outputDirectory}/Set-RepoTheme.ps1` — same directory as the JSON profiles (default: `terminal/ohmyposh/`).
- **Console output:** Appends `   Set-RepoTheme.ps1` to the existing file listing in the success summary.

## Script behaviour

1. `git rev-parse --show-toplevel` — finds the repo root (exits silently if not in a git repo)
2. `git -C $repoRoot remote get-url origin` — extracts the remote URL
3. Regex match on `github.com[:/](owner/repo)` to get `owner/repo`
4. Reads `~/.poshthemes/index.json` and looks up the matching profile entry
5. Calls `oh-my-posh init pwsh --config <configFile> | Invoke-Expression`

`$ErrorActionPreference = 'SilentlyContinue'` ensures the script is a silent no-op in any environment where git, oh-my-posh, or `index.json` are absent.

## Alternatives considered

- **Separate template file in the repo:** Would require embedding or reading from disk at generate-time. Raw string literal in C# is simpler and keeps the script co-located with the code that owns it.
- **Generating the script from `install` command instead:** The `install` command copies from `terminal/ohmyposh/` to `~/.poshthemes/`. By generating it at `generate` time, the script is committed to the repo and always reflects the current generation logic.

## Files changed

- `src/RepoIdentity/Commands/GenerateCommand.cs` — added `ScriptTemplate` const and write logic
- `docs/repo-identity.md` — added `## Set-RepoTheme.ps1` section
- `terminal/ohmyposh/Set-RepoTheme.ps1` — generated output (do not edit manually)

---


---

### 31. Dashboard Navigation, Highlights & Collapsible Sections

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Bruno requested three UX improvements to `site/index.html`: a "Top 3" highlights section, collapsible content sections, and a sticky navigation bar for quick section access.

## Decision

Implemented all three features in the single-file `site/index.html` architecture (no new files, no external dependencies).

### Key Design Points

1. **Top 3 Highlights** — New `<section id="top-highlights">` renders top 3 packages by downloads and top 3 repos by stars. Uses `fmtCompact()` for stat display, medal emoji for ranking. Two-column CSS grid collapses to single column on mobile. No filters — curated view only.

2. **Collapsible Sections** — CSS `max-height` transition (0.4s ease) with opacity fade. Chevron indicator rotates via `transform: rotate(-90deg)`. State persisted in `localStorage` with keys `collapse-{sectionId}`. Click handler uses `e.target.closest()` to exclude view toggle and section-meta controls from triggering collapse.

3. **Sticky Nav Bar** — `<nav class="top-nav">` with `position: sticky; top: 0; z-index: 100`. Three anchor links with pill-style hover effects. `html { scroll-behavior: smooth }` + `scroll-margin-top: 60px` on sections prevents content from hiding behind the nav.

### Alternatives Considered

- **`<details>`/`<summary>` for collapsible** — rejected; limited animation control, inconsistent browser styling.
- **JavaScript scroll for nav** — rejected; CSS `scroll-behavior: smooth` is simpler and more performant.
- **Separate highlights page** — rejected; single-page architecture is a project constraint.

## Impact

- **Modified:** `site/index.html` (808 → 952 lines, +227 net)
- **No new files or dependencies**
- **Backward compatible** — all existing filters, view toggles, and data fetching unchanged
- **localStorage keys added:** `collapse-top-highlights`, `collapse-nuget-packages`, `collapse-github-repos`

---


---

### 32. Historical Trend Aggregation Architecture

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
- Tests: 140 → 179 (39 new trend aggregation tests, all passing)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

---

# Decision: Full PR Data Collection Architecture

**Author:** Kaylee (Backend Dev)
**Date:** 2026-07-23
**Status:** Implemented

## Context

The dashboard previously only tracked a count of open PRs (`OpenPullRequests` int) via a `GetOpenPrCountAsync` method that fetched up to 100 PRs just to count them. Bruno requested a full "Open Pull Requests" section matching the existing Issues section.

## Decision

1. **New model `GitHubPullRequest`** — 17 fields covering PR-specific data (draft status, review decision, branch info, merge timestamps). Follows same `[JsonPropertyName]` camelCase pattern as `GitHubIssue`.

2. **Replaced `GetOpenPrCountAsync` with two methods:**
   - `GetRecentPullRequestsAsync` — fetches up to 40 open PRs with full detail
   - `GetRecentMergedPullRequestsAsync` — fetches recently merged PRs (30-day window), filters by `merged_at`

3. **`additions`/`deletions`/`changed_files` set to 0** — The GitHub Pulls list endpoint does not return these. Individual PR endpoint would require N+1 calls per repo. Decided to skip for efficiency; dashboard works without line counts.

4. **`reviewDecision` set to null from REST** — Only available via GraphQL API. Left as nullable field for future enhancement.

5. **PR Activity in Trends** — `PullRequestActivityPoint` tracks opened/merged/closed per date (three states vs issues' two).

6. **Frontend mirrors Issues section** — Same HTML structure, filter/sort logic, card/list toggle, localStorage persistence.

## Impact

- `GitHubRepoMetrics` gains 3 new fields: `recentPullRequests`, `recentMergedPullRequests`, `mergedPullRequestsCount`
- `TrendData` gains `pullRequestActivity` list
- `OpenPullRequests` int preserved for backward compat (now derived from `RecentPullRequests.Count`)
- Two additional API calls per repo during collection (open PRs + closed PRs)


---



---

# Decision: Identifying & Triggering the Publish Workflow

**Date:** 2026-05-21T14:29:07.671-04:00  
**Agent:** Wash (DevOps)  
**Status:** ✅ Completed

## Problem
User requested: "run the workflow to publish the page"

Need to identify which GitHub Actions workflow is responsible for publishing the dashboard to GitHub Pages.

## Analysis
Reviewed `.github/workflows/` and found:
- `refresh-metrics.yml` — Daily NuGet + GitHub Collector data refresh
  - Trigger: Schedule (04:00 AM EST daily) + `workflow_dispatch` (manual)
  - Pipeline:
    1. Builds & runs .NET Collector (net10.0)
    2. Commits data changes to `data/latest/` and `data/history/` (if any)
    3. Regenerates repo-identity profiles via RepoIdentity tool
    4. **Assembles site:** Copies `site/index.html` and data JSON files to `_site/` directory
    5. **Uploads Pages artifact:** `actions/upload-pages-artifact@v3`
    6. **Deploys to GitHub Pages:** `actions/deploy-pages@v4`

This is **the only workflow that publishes the dashboard page.**

## Decision
**Workflow:** `refresh-metrics.yml`  
**Trigger method:** `gh workflow run "refresh-metrics.yml" --ref main`  
**Run ID:** 26245401993  
**Status URL:** https://github.com/elbruno/nuget-repo-dashboard/actions/runs/26245401993

## Implementation
✅ Successfully triggered via `gh workflow run` at 2026-05-21T14:29:07.671-04:00

```bash
gh workflow run "refresh-metrics.yml" --ref main
# Output:
# ✓ Created workflow_dispatch event for refresh-metrics.yml at main
# https://github.com/elbruno/nuget-repo-dashboard/actions/runs/26245401993
```

Status verification shows run created and waiting to execute:
```json
{
  "status": "waiting",
  "conclusion": "",
  "url": "https://github.com/elbruno/nuget-repo-dashboard/actions/runs/26245401993"
}
```

## Notes
- No repository modifications were required.
- The workflow is fully automated: no secrets or manual steps needed.
- The run will progress through: queued → in_progress → completed (success/failure).
- Dashboard will be live at https://elbruno.github.io/nuget-repo-dashboard/ upon successful completion.

## Team Notes
For future reference: When user requests "run the workflow to publish the page," this refers to triggering `refresh-metrics.yml` manually. The normal daily schedule will execute automatically at 04:00 AM EST.


---

