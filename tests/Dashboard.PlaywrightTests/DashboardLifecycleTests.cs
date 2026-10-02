using FluentAssertions;
using Microsoft.Playwright;

namespace Dashboard.PlaywrightTests;

public partial class DashboardDownloadValidationTests
{
    private const string LocalDashboardUrl = "http://dashboard.test/";
    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private async Task<IPage> OpenLifecycleDashboardAsync(string query = "", bool missingConfig = false)
    {
        var page = await _browser.NewPageAsync();
        var html = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "site", "index.html"));
        var config = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "config", "dashboard-config.json"));
        await page.RouteAsync("**/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var body = path switch
            {
                "/" => html,
                "/data/dashboard-config.json" => config,
                "/data/data.nuget.json" => """
                    {"packages":[
                      {"packageId":"Active.Package","totalDownloads":100,"tags":[]},
                      {"packageId":"ElBruno.CopilotCLIMonitor","totalDownloads":5000,"tags":[]},
                      {"packageId":"ElBruno.CopilotCLIMonitor.Core","totalDownloads":2000,"tags":[]},
                      {"packageId":"ArchivedRepo.Package","totalDownloads":25,"tags":[],"projectUrl":"https://github.com/owner/archived"}
                    ]}
                    """,
                "/data/data.repositories.json" => """
                    {"repositories":[
                      {"fullName":"owner/active","stars":10,"topics":[],"recentIssues":[],"recentPullRequests":[]},
                      {"fullName":"elbruno/ElBruno.CopilotCLIMonitor","stars":999,"topics":[],"recentIssues":[{"title":"Retired issue"}],"recentPullRequests":[{"title":"Retired PR"}],"maintainability":{"totalScore":99}},
                      {"fullName":"owner/archived","archived":true,"stars":888,"topics":[],"recentIssues":[],"recentPullRequests":[],"maintainability":{"totalScore":98}}
                    ],"watchList":[{"fullName":"owner/archived","owner":"owner","repo":"archived"}]}
                    """,
                "/data/data.trends.json" => """
                    {"packages":{
                      "Active.Package":{"downloads":[{"date":"2026-09-01","value":90},{"date":"2026-09-09","value":100}]},
                      "ElBruno.CopilotCLIMonitor":{"downloads":[{"date":"2026-09-01","value":1000},{"date":"2026-09-09","value":5000}]}
                    },"velocities":[{"packageId":"ElBruno.CopilotCLIMonitor","isStale":true,"avgDailyDownloads":0}],"stalePackageCount":1}
                    """,
                _ => "{}"
            };
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = missingConfig && path == "/data/dashboard-config.json" ? 404 : 200,
                ContentType = path == "/" ? "text/html" : path.EndsWith(".json") ? "application/json" : "text/javascript",
                Body = body
            });
        });
        await page.GotoAsync(LocalDashboardUrl + query, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        return page;
    }

    [Fact]
    public async Task Lifecycle_DefaultViewHidesRetiredButPreservesTotalsAndActivePackagesOnArchivedRepos()
    {
        var page = await OpenLifecycleDashboardAsync();
        (await page.Locator("#content").IsVisibleAsync()).Should().BeTrue();
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("active");
        (await page.Locator("#nuget-grid .card").CountAsync()).Should().Be(2);
        (await page.Locator("#repo-grid .card").CountAsync()).Should().Be(1);
        (await page.Locator("#nuget-grid").TextContentAsync()).Should().Contain("ArchivedRepo.Package").And.NotContain("CopilotCLIMonitor");
        (await page.Locator("#lifetime-downloads .number").TextContentAsync()).Should().Be("7,125");
        (await page.Locator("#active-downloads .number").TextContentAsync()).Should().Be("125");
        (await page.Locator("#top-leaderboard-grid").TextContentAsync()).Should().NotContain("CopilotCLIMonitor").And.NotContain("owner/archived");
        (await page.Locator("#staleness-banner").IsVisibleAsync()).Should().BeFalse();
        (await page.Locator("#trends-grid .trend-card").First.TextContentAsync()).Should().Contain("All Packages").And.Contain("5,100");
        (await page.Locator("#trends-grid .trend-card").Nth(1).TextContentAsync()).Should().NotContain("CopilotCLIMonitor");
        (await page.Locator("#issues-grid").TextContentAsync()).Should().NotContain("Retired issue");
        (await page.Locator("#prs-grid").TextContentAsync()).Should().NotContain("Retired PR");
        (await page.Locator("#watch-list-grid .card").CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Lifecycle_RetiredAndAllViewsShowBadgesAndHistoryInCardsAndLists()
    {
        var page = await OpenLifecycleDashboardAsync();
        await page.SelectOptionAsync("#dashboard-lifecycle", "retired");
        (await page.Locator("#nuget-grid .card").CountAsync()).Should().Be(2);
        (await page.Locator("#repo-grid .card").CountAsync()).Should().Be(2);
        (await page.Locator("#nuget-grid").TextContentAsync()).Should().Contain("Retired - unsupported");
        (await page.Locator("#repo-grid").TextContentAsync()).Should().Contain("Archived - read-only");
        (await page.Locator("#watch-list-grid").TextContentAsync()).Should().Contain("Archived - read-only");
        (await page.Locator("#repo-grid .health-badge").CountAsync()).Should().Be(0);
        (await page.Locator("#nuget-grid canvas").CountAsync()).Should().Be(1);
        (await page.Locator("#issues-grid").TextContentAsync()).Should().Contain("Retired issue");
        (await page.Locator("#prs-grid").TextContentAsync()).Should().Contain("Retired PR");
        page.Url.Should().Contain("lifecycle=retired");

        await page.SelectOptionAsync("#dashboard-lifecycle", "all");
        await page.ClickAsync("#nuget-view-toggle button[data-view='list']");
        await page.ClickAsync("#repo-view-toggle button[data-view='list']");
        (await page.Locator("#nuget-list tbody tr").CountAsync()).Should().Be(4);
        (await page.Locator("#repo-list tbody tr").CountAsync()).Should().Be(3);
        (await page.Locator("#nuget-list").TextContentAsync()).Should().Contain("Retired - unsupported");
        (await page.Locator("#repo-list").TextContentAsync()).Should().Contain("Retired - unsupported").And.Contain("Archived - read-only");
        (await page.Locator("#lifetime-downloads .number").TextContentAsync()).Should().Be("7,125");
        (await page.Locator("#active-downloads .number").TextContentAsync()).Should().Be("125");
        await page.SelectOptionAsync("#repo-sort", "health");
        (await page.Locator("#repo-list tbody tr").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Lifecycle_UrlPersistenceSearchAndClearAreConsistent()
    {
        var page = await OpenLifecycleDashboardAsync("?lifecycle=retired");
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("retired");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("retired");
        await page.FillAsync("#dashboard-search", ".Core");
        (await page.Locator("#nuget-grid .card").CountAsync()).Should().Be(1);
        await page.ClickAsync("#clear-filters");
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("active");
        (await page.Locator("#nuget-grid .card").CountAsync()).Should().Be(2);
        page.Url.Should().NotContain("lifecycle=");

        await page.SelectOptionAsync("#dashboard-lifecycle", "all");
        await page.GotoAsync(LocalDashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("all");
        await page.GotoAsync(LocalDashboardUrl + "?lifecycle=invalid", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        (await page.Locator("#dashboard-lifecycle").InputValueAsync()).Should().Be("active");
    }

    [Fact]
    public async Task Lifecycle_ExportsRespectFilterAndIncludeLifecycleFields()
    {
        var page = await OpenLifecycleDashboardAsync("?lifecycle=retired");
        var exported = await page.EvaluateAsync<string>("""
        () => {
            let result;
            downloadFile = content => { result = content; };
            exportPackagesAsJSON();
            return result;
        }
        """);
        using var packages = System.Text.Json.JsonDocument.Parse(exported);
        packages.RootElement.GetProperty("count").GetInt32().Should().Be(2);
        foreach (var package in packages.RootElement.GetProperty("packages").EnumerateArray())
        {
            package.GetProperty("retired").GetBoolean().Should().BeTrue();
            package.GetProperty("id").GetString().Should().Contain("CopilotCLIMonitor");
        }
        var repoExport = await page.EvaluateAsync<string>("""
        () => {
            let result;
            downloadFile = content => { result = content; };
            exportRepositoriesAsJSON();
            return result;
        }
        """);
        repoExport.Should().Contain("\"retired\": true").And.Contain("\"archived\": true");
        using var repositories = System.Text.Json.JsonDocument.Parse(repoExport);
        repositories.RootElement.GetProperty("repositories")[0].GetProperty("stars").GetInt32().Should().Be(999);
        var csv = await page.EvaluateAsync<string>("""
        () => {
            let result;
            downloadFile = content => { result = content; };
            exportPackagesAsCSV();
            return result;
        }
        """);
        csv.Should().Contain("ElBruno.CopilotCLIMonitor,").And.Contain("Retired").And.Contain("true");
    }

    [Fact]
    public async Task Lifecycle_MissingConfigurationShowsErrorInsteadOfActiveSuccess()
    {
        var page = await OpenLifecycleDashboardAsync(missingConfig: true);
        (await page.Locator("#error").TextContentAsync()).Should().Contain("Failed to load lifecycle configuration");
        (await page.Locator("#content").IsVisibleAsync()).Should().BeFalse();
    }
}
