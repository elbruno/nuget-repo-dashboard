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

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
