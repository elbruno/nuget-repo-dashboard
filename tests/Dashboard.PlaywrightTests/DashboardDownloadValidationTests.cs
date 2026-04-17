using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace Dashboard.PlaywrightTests;

/// <summary>
/// Validates that the published NuGet dashboard download numbers
/// are reasonably close to live NuGet API data.
/// Requires Playwright browsers: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
public partial class DashboardDownloadValidationTests : IAsyncLifetime
{
    private const string DashboardUrl = "https://elbruno.github.io/nuget-repo-dashboard/";
    private const string NuGetSearchUsnc = "https://azuresearch-usnc.nuget.org/query";
    private const string NuGetSearchUssc = "https://azuresearch-ussc.nuget.org/query";

    // Allow up to 25% deviation — search API shards lag behind real-time stats
    private const double TolerancePercent = 25.0;

    // Only validate packages with meaningful download counts
    private const long MinDownloadsToValidate = 100;

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Dashboard_LoadsSuccessfully()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(DashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        var title = await page.TitleAsync();
        title.Should().Contain("NuGet", "dashboard page should load with expected title");

        // Verify at least one package card renders
        var cards = page.Locator(".card");
        var count = await cards.CountAsync();
        count.Should().BeGreaterThan(0, "dashboard should render at least one package card");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Dashboard_DownloadCounts_MatchNuGetApi()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(DashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Extract package names and download counts from the dashboard
        var dashboardData = await ExtractDashboardDownloadsAsync(page);
        dashboardData.Should().NotBeEmpty("dashboard should display package data");

        // Filter to packages with meaningful download counts
        var packagesToValidate = dashboardData
            .Where(kvp => kvp.Value >= MinDownloadsToValidate)
            .ToList();

        if (packagesToValidate.Count == 0)
        {
            // All packages have low downloads — skip API comparison
            return;
        }

        using var httpClient = new HttpClient();
        var discrepancies = new List<string>();

        foreach (var (packageId, dashboardDownloads) in packagesToValidate)
        {
            var liveDownloads = await GetLiveDownloadCountAsync(httpClient, packageId);
            if (liveDownloads <= 0) continue;

            var diff = Math.Abs(dashboardDownloads - liveDownloads);
            var pctDiff = (double)diff / liveDownloads * 100;

            if (pctDiff > TolerancePercent)
            {
                discrepancies.Add(
                    $"{packageId}: dashboard={dashboardDownloads}, NuGet API={liveDownloads} (diff={pctDiff:F1}%)");
            }
        }

        discrepancies.Should().BeEmpty(
            $"all packages with >{MinDownloadsToValidate} downloads should be within {TolerancePercent}% of live NuGet data");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Dashboard_TotalDownloads_IsReasonable()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(DashboardUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // Find the summary card with "Total Downloads" label
        var summaryCards = page.Locator(".summary-card");
        var count = await summaryCards.CountAsync();

        long? totalDownloads = null;
        for (int i = 0; i < count; i++)
        {
            var label = await summaryCards.Nth(i).Locator(".label").TextContentAsync();
            if (label?.Contains("Downloads", StringComparison.OrdinalIgnoreCase) == true)
            {
                var numberText = await summaryCards.Nth(i).Locator(".number").TextContentAsync();
                totalDownloads = ParseFormattedNumber(numberText ?? "");
                break;
            }
        }

        totalDownloads.Should().NotBeNull("dashboard should display a Total Downloads summary");
        totalDownloads.Should().BeGreaterThan(0, "total downloads should be positive");
    }

    private static async Task<Dictionary<string, long>> ExtractDashboardDownloadsAsync(IPage page)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var cards = page.Locator(".card");
        var count = await cards.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var card = cards.Nth(i);

            // Get package name from the card title link
            var titleLink = card.Locator(".card-title a");
            if (await titleLink.CountAsync() == 0) continue;

            var packageName = await titleLink.TextContentAsync();
            if (string.IsNullOrWhiteSpace(packageName)) continue;

            // Get download count from the stat value
            var statValues = card.Locator(".stat .value");
            if (await statValues.CountAsync() == 0) continue;

            var downloadText = await statValues.First.TextContentAsync();
            var downloads = ParseFormattedNumber(downloadText ?? "");

            result[packageName.Trim()] = downloads;
        }

        return result;
    }

    /// <summary>
    /// Gets the live download count from NuGet API (max of both shards).
    /// </summary>
    private static async Task<long> GetLiveDownloadCountAsync(HttpClient httpClient, string packageId)
    {
        var usncTask = GetShardDownloadsAsync(httpClient, NuGetSearchUsnc, packageId);
        var usscTask = GetShardDownloadsAsync(httpClient, NuGetSearchUssc, packageId);

        await Task.WhenAll(usncTask, usscTask);

        return Math.Max(
            Math.Max(await usncTask, 0),
            Math.Max(await usscTask, 0));
    }

    private static async Task<long> GetShardDownloadsAsync(HttpClient httpClient, string shardUrl, string packageId)
    {
        try
        {
            var url = $"{shardUrl}?q=packageid:{packageId}&take=1";
            var json = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("totalDownloads", out var dl))
                        return dl.GetInt64();
                }
            }
        }
        catch
        {
            return -1;
        }

        return 0;
    }

    /// <summary>
    /// Parses dashboard-formatted numbers like "3.6K", "1.2M", "42", "0".
    /// </summary>
    internal static long ParseFormattedNumber(string text)
    {
        text = text.Trim().Replace(",", "");

        if (long.TryParse(text, out var exact))
            return exact;

        var match = FormattedNumberRegex().Match(text);
        if (!match.Success) return 0;

        var number = double.Parse(match.Groups[1].Value);
        var suffix = match.Groups[2].Value.ToUpperInvariant();

        return suffix switch
        {
            "K" => (long)(number * 1_000),
            "M" => (long)(number * 1_000_000),
            "B" => (long)(number * 1_000_000_000),
            _ => (long)number
        };
    }

    [GeneratedRegex(@"^([\d.]+)\s*([KMBkmb])?$")]
    private static partial Regex FormattedNumberRegex();
}
