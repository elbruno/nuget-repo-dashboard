# Kaylee — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET, NuGet API, GitHub API, JSON
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Collector** lives in `src/Collector/`. Fetches NuGet + GitHub data, outputs to `data/latest/*.json` and `data/history/YYYY/MM/DD/*.json`.

## Learnings

- **Project structure**: `src/Collector/` is a .NET console app (`Collector.csproj`, **retargeted to net10.0**). Models in `Models/`, services in `Services/`.
- **Framework note (2026-04-02):** Environment has .NET 8.0 and 10.0 runtimes only (no 9.0). Collector retargeted from net9.0 to net10.0 for deployment compatibility. No code changes required beyond TFM swap.
- **Serialization**: Using `System.Text.Json` exclusively with `[JsonPropertyName]` attributes on all model properties.
- **Interfaces for testability**: All services expose interfaces (`IConfigLoader`, `INuGetCollector`, `IGitHubCollector`, `IJsonOutputWriter`) for DI and mocking.
- **NuGet API**: Registration endpoint (`registration5-gz-semver2`) for version/metadata + search endpoint for aggregate download counts. Pages may need separate fetching if not inlined.
- **GitHub API**: REST API with optional Bearer token via `GITHUB_TOKEN` env var. Open PR count requires a separate `/pulls?state=open` call.
- **Rate limiting**: Both collectors implement retry logic (3 attempts, exponential backoff). GitHub collector respects `X-RateLimit-Reset` header.
- **Output paths**: `data/latest/data.json` (latest snapshot) and `data/history/{yyyy}/{MM}/{dd}/data.json` (daily history).
- **Repo root resolution**: Program.cs resolves from `AppContext.BaseDirectory` (5 levels up from bin output) or `DASHBOARD_REPO_ROOT` env var.
- **Config**: `config/tracked-packages.json` — validated on load (non-empty packageId required).
- **Key files**: `src/Collector/Program.cs`, `src/Collector/Models/*.cs`, `src/Collector/Services/*.cs`, `config/tracked-packages.json`, `config/dashboard-config.json`, `README.md`.
- **NuGet profile discovery (2026-04-02):** New service `INuGetProfileDiscoveryService` / `NuGetProfileDiscoveryService` in `Services/`. Queries NuGet Search API (`azuresearch-usnc.nuget.org/query?q=owner:{username}`) with pagination to discover all packages for a user profile. Parses GitHub repo URLs from `projectUrl` using source-generated regex.
- **Dashboard config:** `config/dashboard-config.json` contains `nugetProfile` (NuGet username for discovery) and `mergeWithTrackedPackages` (bool to merge static `tracked-packages.json`). Program.cs loads this first; if no profile is set, falls back to tracked-packages.json only.
- **Pipeline flow (6 steps):** 1) Load dashboard config, 2) Discover packages from NuGet profile, 3) Load & merge tracked packages, 4) Collect NuGet metrics, 5) Collect GitHub metrics, 6) Build & write output.
- **GitHub URL parsing:** `NuGetProfileDiscoveryService.ParseGitHubRepo()` handles `github.com/{owner}/{repo}` with optional path/query/`.git` suffix. Uses `[GeneratedRegex]` for perf.
- **Models added:** `DashboardConfig` (config model), `DiscoveredPackage` (intermediate discovery result with `GitHubRepo` field).
- **elbruno profile stats:** 37 packages discovered, 33 with valid GitHub repo URLs, 4 without projectUrl.
- **Zoe testing (2026-04-02):** 20 tests written for GitHub URL parsing (NuGetProfileDiscoveryServiceTests.cs); discovery service tests stubbed pending service completion. 71 total tests passing.
- **Ignore list feature (2026-04-02):** Added `ignorePackages` to `DashboardConfig` and `dashboard-config.json`. Program.cs filters packages after discovery+merge using a case-insensitive `HashSet`. Cleared `tracked-packages.json` of Microsoft packages. Filters `LocalEmbeddings` (bare, non-ElBruno package) from discovery results. Pipeline output: 37 discovered → 1 filtered → 36 collected, all ElBruno-owned.
