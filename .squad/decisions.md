# Squad Decisions

## Active Decisions

### 1. PRD Decomposition: NuGet + GitHub Dashboard

**Author:** Mal  
**Date:** 2025-01-26  
**Status:** Approved  

The PRD defines 14 prioritized work items across three phases: Foundation (P0), Core Features (P1), and Enhancements (P2). Implementation sequence prioritizes data models and infrastructure before collectors, then workflows, then AI-assisted features. See `docs/nuget-dashboard-prd-v2.md`.

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

### 3. Agentic Workflow Architecture (WI-7, WI-8, WI-9)

**Owner:** Wash (DevOps)  
**Date:** 2025-01-14  
**Status:** Implemented  

Agentic workflows (WI-7, WI-8, WI-9) are defined as **markdown specifications**, not executable YAML. Non-authoritative by design:
- ✅ CAN: Analyze, detect, suggest, create informational issues
- ❌ CANNOT: Modify production data, make unilateral decisions

**Three workflows:** inventory-review (PR comments), weekly-summary (trends), health-triage (anomalies).

---

### 4. Inventory Workflow Uses Branch+PR Pattern

**Author:** Wash (DevOps)  
**Date:** 2025-01-27  
**Status:** Implemented  

Inventory changes go through `inventory/refresh-{date}` branch and PR with human review checklist. Every discovery requires manual merge; new packages arrive with empty `repos: []` requiring reviewer to fill mappings.

---

### 5. Retarget to net10.0

**Author:** Zoe (Tester)  
**Date:** 2026-04-02  
**Status:** Implemented  

Environment has .NET 8.0 and 10.0 runtimes only (no 9.0). Retargeted both `src/Collector/Collector.csproj` and `tests/Collector.Tests/Collector.Tests.csproj` from `net9.0` to `net10.0` for test execution and deployment.

**Impact:** All 51 unit tests pass on net10.0. No code changes needed beyond TFM swap.

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

### 16. RepoIdentity Phase 4 — ConfigGenerator + generate command

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-07-22  
**Status:** Implemented

## Context

Phase 4 completes the `generate` command of the `repo-identity` CLI. The generator must turn `data.repositories.json` into per-repo Oh My Posh JSON theme files so terminal prompts can reflect which repo is active.

## Decisions

### 1. One file per active repo (not a single combined config)

Each non-archived repository gets its own `.json` file (e.g. `elbruno-MyRepo.json`) rather than a single monolithic config.  
**Rationale:** Makes the future `apply` command selective — a user can apply only the profile for the repo they're currently in without touching other repos. A combined file would require parsing and re-splitting at apply time.

### 2. `index.json` as summary/manifest for tooling

An `index.json` is written to the same output directory summarising all generated profiles (repo name, config file path, accent color, icon, generated timestamp).  
**Rationale:** Enables downstream tooling (shell scripts, the future `apply` command, CI checks) to enumerate available profiles without scanning for `.json` files. Acts as a typed manifest.

### 3. Skip archived repos in generation

Repos with `Archived = true` are excluded from generation entirely.  
**Rationale:** Archived repos are not actively developed; generating a prompt theme for them adds noise and wastes disk space. Keeping the output set clean makes `apply` logic simpler.

### 4. Oh My Posh config structure

Each generated config uses:
- `$schema`: `https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/schema.json`
- `version`: 2
- Single `prompt` block, `left` aligned, with one `text` segment
- Segment `style`: `plain`, `background`: `transparent`
- `foreground`: deterministic hex color from `ColorGenerator` (seeded on `fullName:language`)
- `template`: ` {icon} {repoName} ` (space-padded for readability)

**Rationale:** Minimal structure that Oh My Posh validates. Single segment is sufficient for repo identification in a prompt. Deterministic color means re-running generation produces identical files (idempotent).

### 5. `$schema` key workaround

`System.Text.Json` with `JsonNamingPolicy.CamelCase` serializes `Schema` → `"schema"`. Oh My Posh requires the key `"$schema"`. Fixed with a post-serialize string replace: `json.Replace("\"schema\":", "\"$schema\":")`.  
**Rationale:** Simpler than a custom `JsonConverter` or naming policy override for a single key. The replace is precise (only matches the key pattern, not values).

### 6. `SanitizeFileName` replaces `/` only (not spaces)

`SanitizeFileName("owner/Repo Name")` → `"owner-Repo Name"`. Only slashes are replaced; spaces preserved.  
**Rationale:** Matches test expectations. Real repo names on GitHub cannot contain `/` but can contain hyphens. Spaces in repo names are rare; if needed, a future pass can normalize further.

## Files Added / Modified

- `src/RepoIdentity/Models/GenerationResult.cs` — new record
- `src/RepoIdentity/Services/IConfigGenerator.cs` — new interface
- `src/RepoIdentity/Services/ConfigGenerator.cs` — new sealed implementation
- `src/RepoIdentity/Commands/GenerateCommand.cs` — replaced stub with full wiring
- `tests/RepoIdentity.Tests/ConfigGeneratorTests.cs` — 5 new tests

## Test Results

19 tests passing on both net8.0 and net10.0. Smoke test on real `data.repositories.json`: 16 profiles generated to `terminal/ohmyposh/`.

---

### 17. RepoIdentity Phase 5 — preview + apply commands

**Date:** 2026-07-22  
**Author:** Kaylee (Backend Dev)  
**Status:** Implemented

## Decisions

### 1. preview uses same --source as generate for consistency
Both `preview` and `generate` accept `--source` defaulting to `data/latest/data.repositories.json`. This ensures users can compare what *would* be generated against what *was* generated using identical input — no surprises.

### 2. apply --repo uses slash→dash sanitization to find file
Repo names use `/` (e.g. `elbruno/MyRepo`) but file names use `-` (e.g. `elbruno-MyRepo.json`). The `apply` command applies `repo.Replace("/", "-")` to resolve the filename, matching the sanitization used by `ConfigGenerator.SanitizeFileName()`.

### 3. apply copies only non-index profiles when --repo is omitted
`index.json` is a manifest file, not a usable Oh My Posh theme. When copying all profiles, `index.json` is excluded from the copy set so users don't accidentally load it as a theme.

### 4. README updated with new repo-identity section
Added `## 🎨 repo-identity` section to `README.md` documenting all four CLI commands (`preview`, `generate`, `apply` with and without `--repo`), the output file structure, and the optional `repo.identity.json` customization format. Placed after the existing Dashboard section.

---

### 18. Download Velocity, Staleness Detection, Issue Activity, Version Tracking

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-13  
**Status:** Implemented

## Context

The NuGet Search API has been returning stale download counts since April 7 (frozen at 18,439 total across 50 packages). We needed to detect and surface this data quality issue, plus add issue opened/closed tracking and version/package publishing visibility.

## Decisions

### 1. Staleness Detection via Download Velocity

- Compute daily download deltas from consecutive history snapshots
- `avgDailyDownloads` = average of last 7 deltas
- `staleDays` = count of consecutive trailing zero-delta days
- `isStale` = staleDays >= 3 (avoids weekend false positives)
- `stalePackageCount` aggregated at trend data root level
- Frontend shows amber warning banner when any packages are stale

### 2. Issue Activity from History Snapshots

- New `GetRecentClosedIssuesAsync` in GitHubCollector (same retry/rate-limit pattern)
- `GitHubIssue` extended with `State` and `ClosedAt` fields
- `GitHubRepoMetrics` extended with `RecentClosedIssues` list and `ClosedIssuesCount`
- TrendAggregationService counts opened (by `createdAt`) and closed (by `closedAt`) per date from repo snapshots
- Output as `issueActivity` array in trend data

### 3. Version/Package Publishing Tracking

- Track first-appearance of packages chronologically across history → `newPackages` events
- Aggregate version changes per date → `versionActivity` (excludes initial version appearances to avoid noise)
- Both output in trend data for frontend consumption

### 4. Frontend Integration

- Staleness banner at top of content area (amber, dark-mode aware)

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

# Decision: PR Test Endpoint Migration

**Author:** Zoe (Tester)
**Date:** 2026-04-13

## Context

Kaylee refactored `GitHubCollector` to replace the old `GetOpenPrCountAsync` (which used `/pulls?state=open&per_page=1` and `/pulls?state=open&per_page=100`) with two new methods:
- `GetRecentPullRequestsAsync` → `/pulls?state=open&sort=created&direction=desc&per_page=40`
- `GetRecentMergedPullRequestsAsync` → `/pulls?state=closed&sort=updated&direction=desc&per_page=40`

## Decision

Updated all 14 existing tests in `GitHubCollectorTests.cs` to mock the new endpoint URLs instead of the old ones. This was required because the old URLs no longer match what the collector calls, causing `CollectAsync_KnownRepo_ReturnsCorrectMetrics` to fail (expected 3 open PRs, got 0).

## Impact

- All existing tests now correctly mock the new PR endpoints
- No behavioral changes to test assertions beyond the URL updates
- The `BuildPullsJson` helper still works since `ParseSinglePullRequest` uses `TryGetProperty` with defaults
- `additions`/`deletions`/`changedFiles` are always 0 from the list endpoint — tested explicitly in new tests

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

