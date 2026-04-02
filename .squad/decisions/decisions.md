# Decisions

## #9 — CLI Mode Argument for Inventory Management

**Author:** Kaylee (Backend Dev)  
**Date:** 2026-04-02  
**Status:** Implemented  

### Context

The `refresh-inventory.yml` GitHub Action previously duplicated discovery logic in bash scripts, calling the Collector twice and parsing JSON with `jq` to extract package lists. This created maintenance burden and risked divergence between the discovery logic in C# and the inventory management logic in bash.

### Decision

Move ALL inventory management logic into the C# Collector by adding a `--mode` CLI argument.

### Implementation

**Two modes:**
1. **`inventory`** — Run Pipeline 1 (Discovery) only, write to `config/tracked-packages.json`, exit
2. **`metrics`** (default) — Run Pipeline 1 + Pipeline 2, write to `data/` as normal

**Argument parsing:**
- Supports both `--mode inventory` and `--mode=inventory` syntax
- Case-insensitive mode values
- Invalid modes return exit code 1 with clear error message
- Default mode is `metrics` when no argument provided

**Inventory mode behavior:**
1. Loads `dashboard-config.json` and discovers packages from NuGet profile
2. Merges with `tracked-packages.json` if enabled
3. Filters ignored packages
4. Writes sorted, deduplicated package list to `config/tracked-packages.json`
5. Skips Pipeline 2 (Collection) entirely — no NuGet/GitHub API calls
6. Prints summary showing package count and output path

**Output format:**
- JSON array of `PackageConfig` objects: `[{"packageId": "...", "repos": [...]}]`
- Sorted alphabetically by `packageId` (case-insensitive)
- Indented JSON with `WriteIndented = true`
- Creates `config/` directory if it doesn't exist

**Mode display:**
- Startup banner shows active mode: `Mode: inventory` or `Mode: metrics`

### Impact

- **Modified:** `src/Collector/Program.cs` — added argument parsing (lines 8-22), inventory mode logic (lines 194-237)
- **Testing:** All 112 existing tests pass, no behavior changes for default mode
- **Workflows:** Future `refresh-inventory.yml` can simplify to single `dotnet run -- --mode inventory` command
- **Maintainability:** Discovery logic centralized in C#, no bash/jq duplication

### Alternatives Considered

1. **Separate executable** — Rejected: would duplicate models, services, and HTTP client setup
2. **Environment variable** — Rejected: CLI arguments are more explicit and self-documenting
3. **Subcommands (verb-style)** — Rejected: overkill for two modes, top-level flag is simpler

### Notes

- The inventory writer handles empty/missing `tracked-packages.json` gracefully
- In inventory mode, packages with no repos (e.g., `ElBruno.QRCodeGenerator.Tool`) are written with `"repos": []`
- The ConfigLoader's requirement for non-empty arrays doesn't affect the writer (write path is independent)
- Mode validation happens early, before any expensive operations

---

## #10 — Inventory Workflow Uses C# Collector

**Author:** Wash (DevOps)  
**Date:** 2026-04-02  
**Status:** Implemented

### Context

Previously, `refresh-inventory.yml` contained ~80 lines of bash logic to:
- Query NuGet Search API with pagination
- Filter ignored packages from `dashboard-config.json`
- Compare discovered packages against `tracked-packages.json`
- Merge new candidates into tracked packages JSON

This duplicated domain logic that should live in the Collector, not in workflow bash scripts.

### Decision

Refactored `refresh-inventory.yml` to delegate all business logic to the C# Collector running with `--mode inventory`.

### Key Changes

1. **Removed bash steps:**
   - "Read dashboard config" 
   - "Discover NuGet packages"
   - "Merge candidates into tracked packages"

2. **Replaced with single Collector invocation:**
   ```yaml
   - name: Run Collector in inventory mode
     run: dotnet run --project src/Collector/Collector.csproj --configuration Release -- --mode inventory
   ```

3. **Env vars passed to Collector:**
   - `DASHBOARD_REPO_ROOT: ${{ github.workspace }}` — critical for CI path resolution
   - `GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}` — for optional GitHub API calls

4. **Workflow retains orchestration responsibilities:**
   - Checkout, .NET setup
   - Git branch creation, commit, push
   - PR creation with label and checklist body

### Architecture Pattern

**Separation of Concerns:**
- **Workflow (YAML):** Orchestration, CI/CD operations (checkout, setup, git, PR)
- **Collector (C#):** Business logic (NuGet discovery, filtering, merging, JSON manipulation)

**Benefits:**
- 90% less bash code in workflow (~80 lines → ~10 lines for Collector invocation)
- Domain logic tested in C# unit tests (not bash integration tests)
- Single source of truth for package discovery rules
- Easier to extend (e.g., add GitHub repo resolution to inventory mode)

### Impact

- **Modified:** `.github/workflows/refresh-inventory.yml` (rewritten, ~70 lines → ~60 lines)
- **Fixed:** .NET version from `9.0.x` → `10.0.x` (matches Collector net10.0 target)
- **Verified:** `refresh-metrics.yml` is correct (no changes needed)

### Related

- **Collector Implementation:** Kaylee's #9 — CLI Mode Argument
- **Team Decision #6:** NuGet Profile Auto-Discovery (`.squad/decisions.md`)
