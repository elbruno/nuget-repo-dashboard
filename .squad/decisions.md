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

### 13. HTML Dashboard — Single-File Architecture

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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
