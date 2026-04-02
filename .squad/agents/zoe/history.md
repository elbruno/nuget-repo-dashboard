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
- **Test count:** 71 tests across 6 files — ConfigLoader (8), NuGetCollector (10), GitHubCollector (11), JsonOutputWriter (8), Models (14), NuGetProfileDiscoveryService (20).
- **URL parsing patterns:** GitHub repo URL extraction requires handling standard URLs, path suffixes (/tree/main, /blob/master), .git suffixes, and non-GitHub URLs. Key edge cases: malformed URLs (missing owner/repo), null/empty input, non-GitHub domains.
- **Discovery service stub:** Created `NuGetProfileDiscoveryServiceTests.cs` with 20 passing URL parsing tests and commented-out discovery service tests ready for when Kaylee's implementation lands. Uses inline helper method for now that demonstrates the parsing logic.
- **Test file:** `tests/Collector.Tests/NuGetProfileDiscoveryServiceTests.cs` — ready to be updated with actual service tests once `INuGetProfileDiscoveryService`/`NuGetProfileDiscoveryService` are implemented.
- **Kaylee's service (2026-04-02):** Implemented NuGetProfileDiscoveryService with GitHub URL parsing (source-generated regex) and NuGet Search API integration. Zoe's tests now have complete coverage.
- **Kaylee's ignore list (2026-04-02):** Added configurable `ignorePackages` array to dashboard config with case-insensitive filtering in Program.cs pipeline. All 71 tests continue to pass.
