# Zoe — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET testing (xUnit or MSTest), GitHub Actions
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Test targets:** C# Collector logic, JSON data contracts, workflow behavior. External APIs should be mocked.

## Learnings

- **Test project location:** `tests/Collector.Tests/Collector.Tests.csproj` referencing `src/Collector/Collector.csproj` (**both net10.0**, retargeted 2026-04-02 from net9.0)
- **Packages used:** xUnit 2.9.3, FluentAssertions 8.3.0, NSubstitute 5.3.0, coverlet 6.0.4
- **HTTP mocking pattern:** Custom `MockHttpMessageHandler` in `Helpers/` — injected into `HttpClient` constructor. URL-keyed responses + custom handler delegate for stateful scenarios (rate-limit retries, call counting).
- **File I/O tests:** Use temp dirs via `Path.GetTempPath()` + unique GUID, cleaned up in `Dispose()`.
- **Framework note:** Environment has .NET 8.0 and 10.0 runtimes only — no 9.0. Both Collector and test project retargeted to `net10.0` for runtime support.
- **ConfigLoader edge cases:** Throws `FileNotFoundException` for missing files, `InvalidOperationException` for empty arrays/null/empty packageIds. Invalid JSON throws `JsonException`.
- **NuGetCollector edge cases:** Per-package error handling — one failure doesn't block others. 404 returns null (skipped). TooManyRequests triggers retry with RetryAfter header. Network errors retry up to 3 times.
- **GitHubCollector edge cases:** Invalid repo format (no `/`) returns null silently. Rate-limit 403 with X-RateLimit-Remaining=0 waits for reset. Handles null optional fields (description, language, license, lastPush).
- **JsonOutputWriter:** Writes to both `data/latest/data.json` and `data/history/YYYY/MM/DD/data.json`. Creates directory trees automatically. Uses `WriteIndented = true` + `camelCase` naming.
- **Test count:** 51 tests across 5 files — ConfigLoader (8), NuGetCollector (10), GitHubCollector (11), JsonOutputWriter (8), Models (14).
