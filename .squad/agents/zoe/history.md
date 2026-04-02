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
- **Test count:** 94 tests across 6 files — ConfigLoader (9), NuGetCollector (10), GitHubCollector (20), JsonOutputWriter (16), Models (20), NuGetProfileDiscoveryService (20 theory expansions).
- **URL parsing patterns:** GitHub repo URL extraction requires handling standard URLs, path suffixes (/tree/main, /blob/master), .git suffixes, and non-GitHub URLs. Key edge cases: malformed URLs (missing owner/repo), null/empty input, non-GitHub domains.
- **Discovery service stub:** Created `NuGetProfileDiscoveryServiceTests.cs` with 20 passing URL parsing tests and commented-out discovery service tests ready for when Kaylee's implementation lands. Uses inline helper method for now that demonstrates the parsing logic.
- **Test file:** `tests/Collector.Tests/NuGetProfileDiscoveryServiceTests.cs` — ready to be updated with actual service tests once `INuGetProfileDiscoveryService`/`NuGetProfileDiscoveryService` are implemented.
- **Kaylee's service (2026-04-02):** Implemented NuGetProfileDiscoveryService with GitHub URL parsing (source-generated regex) and NuGet Search API integration. Zoe's tests now have complete coverage.
- **Kaylee's ignore list (2026-04-02):** Added configurable `ignorePackages` array to dashboard config with case-insensitive filtering in Program.cs pipeline. All 71 tests continue to pass.
- **Split output pipeline (2026-04-02):** Updated tests for Kaylee's split output architecture — two output models (NuGetOutput/RepositoriesOutput) and two-file output (data.nuget.json/data.repositories.json). JsonOutputWriter now has WriteNuGetAsync/WriteRepositoriesAsync replacing WriteAsync. Added 16 tests for split output (8 per file type). All 94 tests pass.
- **Enriched GitHub model (2026-04-02):** GitHubRepoMetrics expanded with 12 new properties: watchersCount (subscribers_count), topics, createdAt, updatedAt, size, defaultBranch, homepage, hasWiki, hasPages, networkCount, visibility, htmlUrl. Updated BuildRepoJson helper with all new fields. Added 9 new GitHubCollector tests to validate parsing of enriched fields, including null-handling tests. Added 3 new Model tests (NuGetOutput, RepositoriesOutput round-trips + enriched fields serialization test). All tests pass — ready for Kaylee's production code.
- **Decision merged (2026-04-02):** Split output decision merged to `decisions.md` as decision #8. Orchestration logs written. Decisions inbox cleaned. Cross-agent coordination complete, all 94 tests passing.

