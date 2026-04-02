# Kaylee — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET, NuGet API, GitHub API, JSON
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Collector** lives in `src/Collector/`. Fetches NuGet + GitHub data, outputs to `data/latest/*.json` and `data/history/YYYY/MM/DD/*.json`.

## Learnings

- **Project structure**: `src/Collector/` is a .NET 9 console app (`Collector.csproj`). Models in `Models/`, services in `Services/`.
- **Serialization**: Using `System.Text.Json` exclusively with `[JsonPropertyName]` attributes on all model properties.
- **Interfaces for testability**: All services expose interfaces (`IConfigLoader`, `INuGetCollector`, `IGitHubCollector`, `IJsonOutputWriter`) for DI and mocking.
- **NuGet API**: Registration endpoint (`registration5-gz-semver2`) for version/metadata + search endpoint for aggregate download counts. Pages may need separate fetching if not inlined.
- **GitHub API**: REST API with optional Bearer token via `GITHUB_TOKEN` env var. Open PR count requires a separate `/pulls?state=open` call.
- **Rate limiting**: Both collectors implement retry logic (3 attempts, exponential backoff). GitHub collector respects `X-RateLimit-Reset` header.
- **Output paths**: `data/latest/data.json` (latest snapshot) and `data/history/{yyyy}/{MM}/{dd}/data.json` (daily history).
- **Repo root resolution**: Program.cs resolves from `AppContext.BaseDirectory` (5 levels up from bin output) or `DASHBOARD_REPO_ROOT` env var.
- **Config**: `config/tracked-packages.json` — validated on load (non-empty packageId required).
- **Key files**: `src/Collector/Program.cs`, `src/Collector/Models/*.cs`, `src/Collector/Services/*.cs`, `config/tracked-packages.json`, `README.md`.
