using System.Text.Json;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class TrendAggregationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TrendAggregationService _service;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public TrendAggregationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"trend-agg-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _service = new TrendAggregationService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers — create fabricated history data on disk
    // ──────────────────────────────────────────────────────────────────

    private string CreateHistoryDay(DateOnly date)
    {
        var dir = Path.Combine(
            _tempRoot, "data", "history",
            date.Year.ToString(),
            date.Month.ToString("D2"),
            date.Day.ToString("D2"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task WriteNuGetSnapshot(string dayDir, params NuGetPackageMetrics[] packages)
    {
        var output = new NuGetOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = packages.ToList()
        };
        var path = Path.Combine(dayDir, "data.nuget.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, WriteOptions);
    }

    private async Task WriteReposSnapshot(string dayDir, params GitHubRepoMetrics[] repos)
    {
        var output = new RepositoriesOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Repositories = repos.ToList()
        };
        var path = Path.Combine(dayDir, "data.repositories.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, WriteOptions);
    }

    private static NuGetPackageMetrics BuildPackage(
        string id, long downloads, string version = "1.0.0") =>
        new()
        {
            PackageId = id,
            TotalDownloads = downloads,
            LatestVersion = version,
            Description = $"Test package {id}",
            Authors = "TestAuthor"
        };

    private static GitHubRepoMetrics BuildRepo(
        string owner, string name,
        int stars = 0, int forks = 0, int issues = 0, int prs = 0) =>
        new()
        {
            Owner = owner,
            Name = name,
            FullName = $"{owner}/{name}",
            Stars = stars,
            Forks = forks,
            OpenIssues = issues,
            OpenPullRequests = prs
        };

    // Large windowDays ensures none of our test data is filtered out by the cutoff
    private const int LargeWindow = 365 * 5;

    // ──────────────────────────────────────────────────────────────────
    // 1. Happy path — 3+ days of valid data
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ThreeDays_ProducesCorrectTrendPoints()
    {
        var day1 = new DateOnly(2025, 6, 1);
        var day2 = new DateOnly(2025, 6, 2);
        var day3 = new DateOnly(2025, 6, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg.A", 100));
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.A", 150));
        await WriteNuGetSnapshot(dir3, BuildPackage("Pkg.A", 200));

        await WriteReposSnapshot(dir1, BuildRepo("elbruno", "repo1", stars: 10, forks: 2));
        await WriteReposSnapshot(dir2, BuildRepo("elbruno", "repo1", stars: 12, forks: 3));
        await WriteReposSnapshot(dir3, BuildRepo("elbruno", "repo1", stars: 15, forks: 4));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        // NuGet trends
        result.Packages.Should().ContainKey("Pkg.A");
        var pkgTrend = result.Packages["Pkg.A"];
        pkgTrend.Downloads.Should().HaveCount(3);
        pkgTrend.Downloads[0].Date.Should().Be("2025-06-01");
        pkgTrend.Downloads[0].Value.Should().Be(100);
        pkgTrend.Downloads[1].Value.Should().Be(150);
        pkgTrend.Downloads[2].Value.Should().Be(200);

        // Repo trends
        result.Repositories.Should().ContainKey("elbruno/repo1");
        var repoTrend = result.Repositories["elbruno/repo1"];
        repoTrend.Stars.Should().HaveCount(3);
        repoTrend.Stars.Select(s => s.Value).Should().ContainInOrder(10, 12, 15);
        repoTrend.Forks.Select(f => f.Value).Should().ContainInOrder(2, 3, 4);
    }

    [Fact]
    public async Task HappyPath_MultiplePackagesAndRepos()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        await WriteNuGetSnapshot(dir,
            BuildPackage("Pkg.A", 100),
            BuildPackage("Pkg.B", 200));

        await WriteReposSnapshot(dir,
            BuildRepo("user", "repo1", stars: 5),
            BuildRepo("user", "repo2", stars: 10));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().HaveCount(2);
        result.Packages.Should().ContainKey("Pkg.A").And.ContainKey("Pkg.B");

        result.Repositories.Should().HaveCount(2);
        result.Repositories.Should().ContainKey("user/repo1").And.ContainKey("user/repo2");
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Empty history — no history directory
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyHistory_NoHistoryDir_ReturnsEmptyTrends()
    {
        // _tempRoot exists but has no data/history/ subdirectory
        var result = await _service.AggregateAsync(_tempRoot);

        result.Should().NotBeNull();
        result.Packages.Should().BeEmpty();
        result.Repositories.Should().BeEmpty();
        result.WindowDays.Should().Be(90);
    }

    [Fact]
    public async Task EmptyHistory_HistoryDirExistsButEmpty_ReturnsEmptyTrends()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "data", "history"));

        var result = await _service.AggregateAsync(_tempRoot);

        result.Packages.Should().BeEmpty();
        result.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyHistory_NonExistentRoot_ReturnsEmptyTrends()
    {
        var missingRoot = Path.Combine(_tempRoot, "does-not-exist");

        var result = await _service.AggregateAsync(missingRoot);

        result.Should().NotBeNull();
        result.Packages.Should().BeEmpty();
        result.Repositories.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. Single day of data
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleDay_ProducesTrendsWithOneDataPoint()
    {
        var day = new DateOnly(2025, 8, 15);
        var dir = CreateHistoryDay(day);

        await WriteNuGetSnapshot(dir, BuildPackage("Solo.Pkg", 42));
        await WriteReposSnapshot(dir, BuildRepo("org", "mono", stars: 7, forks: 1, issues: 3, prs: 0));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages["Solo.Pkg"].Downloads.Should().ContainSingle()
            .Which.Value.Should().Be(42);

        var repo = result.Repositories["org/mono"];
        repo.Stars.Should().ContainSingle().Which.Value.Should().Be(7);
        repo.Forks.Should().ContainSingle().Which.Value.Should().Be(1);
        repo.OpenIssues.Should().ContainSingle().Which.Value.Should().Be(3);
        repo.OpenPullRequests.Should().ContainSingle().Which.Value.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. Missing days (gaps) — only includes available days
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MissingDays_GapsInHistory_OnlyIncludesAvailableDays()
    {
        var day1 = new DateOnly(2025, 3, 1);
        var day3 = new DateOnly(2025, 3, 3);
        var day5 = new DateOnly(2025, 3, 5);

        var dir1 = CreateHistoryDay(day1);
        var dir3 = CreateHistoryDay(day3);
        var dir5 = CreateHistoryDay(day5);

        await WriteNuGetSnapshot(dir1, BuildPackage("Gap.Pkg", 10));
        await WriteNuGetSnapshot(dir3, BuildPackage("Gap.Pkg", 30));
        await WriteNuGetSnapshot(dir5, BuildPackage("Gap.Pkg", 50));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        var downloads = result.Packages["Gap.Pkg"].Downloads;
        downloads.Should().HaveCount(3);
        downloads.Select(d => d.Date).Should().ContainInOrder("2025-03-01", "2025-03-03", "2025-03-05");
        downloads.Select(d => d.Value).Should().ContainInOrder(10, 30, 50);
    }

    [Fact]
    public async Task MissingDays_NoInterpolation()
    {
        var day1 = new DateOnly(2025, 5, 1);
        var day10 = new DateOnly(2025, 5, 10);

        var dir1 = CreateHistoryDay(day1);
        var dir10 = CreateHistoryDay(day10);

        await WriteReposSnapshot(dir1, BuildRepo("o", "r", stars: 5));
        await WriteReposSnapshot(dir10, BuildRepo("o", "r", stars: 50));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        // Only 2 data points — no interpolated values between them
        result.Repositories["o/r"].Stars.Should().HaveCount(2);
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. Malformed JSON — skips that day gracefully
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MalformedJson_NuGetFile_SkippedGracefully()
    {
        var goodDay = new DateOnly(2025, 4, 1);
        var badDay = new DateOnly(2025, 4, 2);

        var goodDir = CreateHistoryDay(goodDay);
        var badDir = CreateHistoryDay(badDay);

        await WriteNuGetSnapshot(goodDir, BuildPackage("Good.Pkg", 100));
        await File.WriteAllTextAsync(Path.Combine(badDir, "data.nuget.json"), "{{ not valid json!!");

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().ContainKey("Good.Pkg");
        result.Packages["Good.Pkg"].Downloads.Should().ContainSingle();
    }

    [Fact]
    public async Task MalformedJson_ReposFile_SkippedGracefully()
    {
        var goodDay = new DateOnly(2025, 4, 1);
        var badDay = new DateOnly(2025, 4, 2);

        var goodDir = CreateHistoryDay(goodDay);
        var badDir = CreateHistoryDay(badDay);

        await WriteReposSnapshot(goodDir, BuildRepo("o", "r", stars: 10));
        await File.WriteAllTextAsync(Path.Combine(badDir, "data.repositories.json"), "NOT JSON");

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Repositories["o/r"].Stars.Should().ContainSingle().Which.Value.Should().Be(10);
    }

    [Fact]
    public async Task MalformedJson_DoesNotPreventOtherDays()
    {
        var day1 = new DateOnly(2025, 4, 1);
        var day2 = new DateOnly(2025, 4, 2);
        var day3 = new DateOnly(2025, 4, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg", 10));
        await File.WriteAllTextAsync(Path.Combine(dir2, "data.nuget.json"), "CORRUPT");
        await WriteNuGetSnapshot(dir3, BuildPackage("Pkg", 30));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages["Pkg"].Downloads.Should().HaveCount(2);
        result.Packages["Pkg"].Downloads.Select(d => d.Value).Should().ContainInOrder(10, 30);
    }

    [Fact]
    public async Task MalformedJson_EmptyFile_SkippedGracefully()
    {
        var day = new DateOnly(2025, 4, 5);
        var dir = CreateHistoryDay(day);

        await File.WriteAllTextAsync(Path.Combine(dir, "data.nuget.json"), "");

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. 90-day cap
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NinetyDayCap_DefaultWindow_OnlyIncludesLast90Days()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var within = today.AddDays(-30);
        var outside = today.AddDays(-100);

        var withinDir = CreateHistoryDay(within);
        var outsideDir = CreateHistoryDay(outside);

        await WriteNuGetSnapshot(withinDir, BuildPackage("Recent", 200));
        await WriteNuGetSnapshot(outsideDir, BuildPackage("Old", 500));

        // Default windowDays = 90
        var result = await _service.AggregateAsync(_tempRoot);

        result.Packages.Should().ContainKey("Recent");
        result.Packages.Should().NotContainKey("Old");
        result.WindowDays.Should().Be(90);
    }

    [Fact]
    public async Task NinetyDayCap_ExactBoundary_IncludesDayAtCutoff()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var atBoundary = today.AddDays(-90);
        var justBefore = today.AddDays(-91);

        var atDir = CreateHistoryDay(atBoundary);
        var beforeDir = CreateHistoryDay(justBefore);

        await WriteNuGetSnapshot(atDir, BuildPackage("AtBoundary", 100));
        await WriteNuGetSnapshot(beforeDir, BuildPackage("JustBefore", 50));

        var result = await _service.AggregateAsync(_tempRoot);

        result.Packages.Should().ContainKey("AtBoundary");
        result.Packages.Should().NotContainKey("JustBefore");
    }

    [Fact]
    public async Task NinetyDayCap_Given120Days_OnlyLast90Included()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Create 120 days of history
        for (int i = 0; i < 120; i++)
        {
            var date = today.AddDays(-i);
            var dir = CreateHistoryDay(date);
            await WriteNuGetSnapshot(dir, BuildPackage("BulkPkg", 1000 + i));
        }

        var result = await _service.AggregateAsync(_tempRoot);

        // Should have at most 91 data points (day 0 through day -90 inclusive)
        result.Packages["BulkPkg"].Downloads.Count.Should().BeInRange(89, 91);
    }

    [Fact]
    public async Task CustomWindow_HonorsWindowParameter()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var within30 = today.AddDays(-15);
        var outside30 = today.AddDays(-45);

        var withinDir = CreateHistoryDay(within30);
        var outsideDir = CreateHistoryDay(outside30);

        await WriteNuGetSnapshot(withinDir, BuildPackage("Near", 10));
        await WriteNuGetSnapshot(outsideDir, BuildPackage("Far", 20));

        var result = await _service.AggregateAsync(_tempRoot, windowDays: 30);

        result.WindowDays.Should().Be(30);
        result.Packages.Should().ContainKey("Near");
        result.Packages.Should().NotContainKey("Far");
    }

    // ──────────────────────────────────────────────────────────────────
    // 7. New package appears mid-history
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewPackageAppearsMidHistory_TrendStartsFromFirstAppearance()
    {
        var day1 = new DateOnly(2025, 1, 1);
        var day2 = new DateOnly(2025, 1, 15);
        var day3 = new DateOnly(2025, 1, 30);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        // Package A exists all 3 days; Package B appears on day 2 only
        await WriteNuGetSnapshot(dir1, BuildPackage("Always.Here", 10));
        await WriteNuGetSnapshot(dir2, BuildPackage("Always.Here", 20), BuildPackage("Late.Joiner", 5));
        await WriteNuGetSnapshot(dir3, BuildPackage("Always.Here", 30), BuildPackage("Late.Joiner", 15));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages["Always.Here"].Downloads.Should().HaveCount(3);

        var lateTrend = result.Packages["Late.Joiner"];
        lateTrend.Downloads.Should().HaveCount(2);
        lateTrend.Downloads[0].Date.Should().Be("2025-01-15");
        lateTrend.Downloads[0].Value.Should().Be(5);
    }

    [Fact]
    public async Task NewRepoAppearsMidHistory_TrendStartsFromFirstAppearance()
    {
        var day1 = new DateOnly(2025, 2, 1);
        var day2 = new DateOnly(2025, 2, 10);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        await WriteReposSnapshot(dir1, BuildRepo("o", "old-repo", stars: 5));
        await WriteReposSnapshot(dir2,
            BuildRepo("o", "old-repo", stars: 8),
            BuildRepo("o", "new-repo", stars: 1));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Repositories["o/old-repo"].Stars.Should().HaveCount(2);
        result.Repositories["o/new-repo"].Stars.Should().ContainSingle()
            .Which.Date.Should().Be("2025-02-10");
    }

    // ──────────────────────────────────────────────────────────────────
    // 8. Package disappears from later snapshots
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PackageDisappears_TrendStopsAtLastAppearance()
    {
        var day1 = new DateOnly(2025, 5, 1);
        var day2 = new DateOnly(2025, 5, 2);
        var day3 = new DateOnly(2025, 5, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Transient", 100), BuildPackage("Stable", 50));
        await WriteNuGetSnapshot(dir2, BuildPackage("Transient", 120), BuildPackage("Stable", 55));
        // Day 3: Transient is gone, only Stable remains
        await WriteNuGetSnapshot(dir3, BuildPackage("Stable", 60));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages["Transient"].Downloads.Should().HaveCount(2);
        result.Packages["Transient"].Downloads.Last().Date.Should().Be("2025-05-02");

        result.Packages["Stable"].Downloads.Should().HaveCount(3);
    }

    [Fact]
    public async Task RepoDisappears_TrendStopsAtLastAppearance()
    {
        var day1 = new DateOnly(2025, 6, 1);
        var day2 = new DateOnly(2025, 6, 2);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        await WriteReposSnapshot(dir1,
            BuildRepo("o", "archived", stars: 10),
            BuildRepo("o", "active", stars: 20));
        await WriteReposSnapshot(dir2,
            BuildRepo("o", "active", stars: 25));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Repositories["o/archived"].Stars.Should().ContainSingle();
        result.Repositories["o/active"].Stars.Should().HaveCount(2);
    }

    // ──────────────────────────────────────────────────────────────────
    // 9. Version events — track when latestVersion changes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VersionEvents_InitialVersionRecorded()
    {
        var day = new DateOnly(2025, 9, 1);
        var dir = CreateHistoryDay(day);

        await WriteNuGetSnapshot(dir, BuildPackage("Ver.Pkg", 10, "1.0.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        var versionHistory = result.Packages["Ver.Pkg"].VersionHistory;
        versionHistory.Should().ContainSingle();
        versionHistory[0].Date.Should().Be("2025-09-01");
        versionHistory[0].Version.Should().Be("1.0.0");
    }

    [Fact]
    public async Task VersionEvents_ChangeDetectedAcrossDays()
    {
        var day1 = new DateOnly(2025, 9, 1);
        var day2 = new DateOnly(2025, 9, 2);
        var day3 = new DateOnly(2025, 9, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Ver.Pkg", 10, "1.0.0"));
        await WriteNuGetSnapshot(dir2, BuildPackage("Ver.Pkg", 20, "1.0.0")); // same version
        await WriteNuGetSnapshot(dir3, BuildPackage("Ver.Pkg", 30, "2.0.0")); // version bump

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        var versionHistory = result.Packages["Ver.Pkg"].VersionHistory;
        versionHistory.Should().HaveCount(2);
        versionHistory[0].Version.Should().Be("1.0.0");
        versionHistory[1].Version.Should().Be("2.0.0");
        versionHistory[1].Date.Should().Be("2025-09-03");
    }

    [Fact]
    public async Task VersionEvents_MultipleVersionBumps()
    {
        var day1 = new DateOnly(2025, 10, 1);
        var day2 = new DateOnly(2025, 10, 2);
        var day3 = new DateOnly(2025, 10, 3);
        var day4 = new DateOnly(2025, 10, 4);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);
        var dir4 = CreateHistoryDay(day4);

        await WriteNuGetSnapshot(dir1, BuildPackage("Bumpy", 10, "1.0.0"));
        await WriteNuGetSnapshot(dir2, BuildPackage("Bumpy", 20, "1.1.0"));
        await WriteNuGetSnapshot(dir3, BuildPackage("Bumpy", 30, "1.1.0")); // no change
        await WriteNuGetSnapshot(dir4, BuildPackage("Bumpy", 40, "2.0.0-beta"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        var vh = result.Packages["Bumpy"].VersionHistory;
        vh.Should().HaveCount(3);
        vh.Select(v => v.Version).Should().ContainInOrder("1.0.0", "1.1.0", "2.0.0-beta");
    }

    [Fact]
    public async Task VersionEvents_NoVersionChange_SingleEntry()
    {
        var day1 = new DateOnly(2025, 11, 1);
        var day2 = new DateOnly(2025, 11, 2);
        var day3 = new DateOnly(2025, 11, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Stable.Pkg", 10, "3.0.0"));
        await WriteNuGetSnapshot(dir2, BuildPackage("Stable.Pkg", 20, "3.0.0"));
        await WriteNuGetSnapshot(dir3, BuildPackage("Stable.Pkg", 30, "3.0.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages["Stable.Pkg"].VersionHistory.Should().ContainSingle()
            .Which.Version.Should().Be("3.0.0");
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge cases: DiscoverDateDirectories (internal method)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DiscoverDateDirectories_SortsChronologically()
    {
        var historyRoot = Path.Combine(_tempRoot, "history");

        // Create out-of-order directories
        CreateDirInHistory(historyRoot, 2025, 12, 31);
        CreateDirInHistory(historyRoot, 2025, 1, 1);
        CreateDirInHistory(historyRoot, 2025, 6, 15);

        var result = TrendAggregationService.DiscoverDateDirectories(
            historyRoot, new DateOnly(2020, 1, 1));

        result.Should().HaveCount(3);
        result[0].Date.Should().Be(new DateOnly(2025, 1, 1));
        result[1].Date.Should().Be(new DateOnly(2025, 6, 15));
        result[2].Date.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public void DiscoverDateDirectories_FiltersOutDatesBeforeCutoff()
    {
        var historyRoot = Path.Combine(_tempRoot, "history");

        CreateDirInHistory(historyRoot, 2025, 1, 1);
        CreateDirInHistory(historyRoot, 2025, 6, 1);
        CreateDirInHistory(historyRoot, 2025, 12, 1);

        var cutoff = new DateOnly(2025, 6, 1);
        var result = TrendAggregationService.DiscoverDateDirectories(historyRoot, cutoff);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2025, 6, 1)); // inclusive
        result[1].Date.Should().Be(new DateOnly(2025, 12, 1));
    }

    [Fact]
    public void DiscoverDateDirectories_SkipsNonNumericFolders()
    {
        var historyRoot = Path.Combine(_tempRoot, "history");

        CreateDirInHistory(historyRoot, 2025, 3, 15);
        // Add non-numeric directory at the year level
        Directory.CreateDirectory(Path.Combine(historyRoot, "readme"));
        // Add non-numeric directory at the month level
        Directory.CreateDirectory(Path.Combine(historyRoot, "2025", "notes"));

        var result = TrendAggregationService.DiscoverDateDirectories(
            historyRoot, new DateOnly(2020, 1, 1));

        result.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2025, 3, 15));
    }

    [Fact]
    public void DiscoverDateDirectories_SkipsInvalidDates()
    {
        var historyRoot = Path.Combine(_tempRoot, "history");

        CreateDirInHistory(historyRoot, 2025, 3, 15); // valid
        CreateDirInHistory(historyRoot, 2025, 13, 1); // month 13 — invalid but TryParse succeeds; DateOnly ctor throws
        CreateDirInHistory(historyRoot, 2025, 2, 30); // Feb 30 — invalid

        var result = TrendAggregationService.DiscoverDateDirectories(
            historyRoot, new DateOnly(2020, 1, 1));

        result.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2025, 3, 15));
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge cases: NuGet-specific
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NuGetOnly_NoReposFiles_ProducesOnlyPackageTrends()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        await WriteNuGetSnapshot(dir, BuildPackage("NuGetOnly", 42));
        // No data.repositories.json written

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().ContainKey("NuGetOnly");
        result.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task ReposOnly_NoNuGetFiles_ProducesOnlyRepoTrends()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        // No data.nuget.json written
        await WriteReposSnapshot(dir, BuildRepo("o", "repos-only", stars: 99));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().BeEmpty();
        result.Repositories.Should().ContainKey("o/repos-only");
    }

    [Fact]
    public async Task EmptyPackageId_SkippedSilently()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        var emptyIdPkg = new NuGetPackageMetrics
        {
            PackageId = "",
            TotalDownloads = 100,
            LatestVersion = "1.0.0"
        };

        await WriteNuGetSnapshot(dir, emptyIdPkg, BuildPackage("Valid.Pkg", 50));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().ContainKey("Valid.Pkg");
        result.Packages.Should().NotContainKey("");
    }

    [Fact]
    public async Task EmptyRepoFullName_SkippedSilently()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        var emptyNameRepo = new GitHubRepoMetrics
        {
            Owner = "",
            Name = "",
            FullName = "",
            Stars = 99
        };

        await WriteReposSnapshot(dir, emptyNameRepo, BuildRepo("o", "valid", stars: 5));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Repositories.Should().ContainKey("o/valid");
        result.Repositories.Should().NotContainKey("");
    }

    [Fact]
    public async Task NullPackagesArray_SkippedGracefully()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        // JSON with null packages field
        var json = """{"generatedAt":"2025-07-01T00:00:00Z","packages":null}""";
        await File.WriteAllTextAsync(Path.Combine(dir, "data.nuget.json"), json);

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task NullRepositoriesArray_SkippedGracefully()
    {
        var day = new DateOnly(2025, 7, 1);
        var dir = CreateHistoryDay(day);

        var json = """{"generatedAt":"2025-07-01T00:00:00Z","repositories":null}""";
        await File.WriteAllTextAsync(Path.Combine(dir, "data.repositories.json"), json);

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Repositories.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    // Serialization round-trip for TrendData models
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TrendData_SerializesWithCorrectJsonPropertyNames()
    {
        var trendData = new TrendData
        {
            GeneratedAt = new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero),
            WindowDays = 90,
            Packages = new Dictionary<string, PackageTrend>
            {
                ["TestPkg"] = new PackageTrend
                {
                    Downloads = [new TrendPoint<long> { Date = "2025-07-01", Value = 42 }],
                    VersionHistory = [new VersionEvent { Date = "2025-07-01", Version = "1.0.0" }]
                }
            },
            Repositories = new Dictionary<string, RepositoryTrend>
            {
                ["owner/repo"] = new RepositoryTrend
                {
                    Stars = [new TrendPoint<int> { Date = "2025-07-01", Value = 10 }],
                    Forks = [new TrendPoint<int> { Date = "2025-07-01", Value = 3 }],
                    OpenIssues = [new TrendPoint<int> { Date = "2025-07-01", Value = 5 }],
                    OpenPullRequests = [new TrendPoint<int> { Date = "2025-07-01", Value = 2 }]
                }
            }
        };

        var json = JsonSerializer.Serialize(trendData, WriteOptions);

        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"windowDays\"");
        json.Should().Contain("\"packages\"");
        json.Should().Contain("\"repositories\"");
        json.Should().Contain("\"downloads\"");
        json.Should().Contain("\"versionHistory\"");
        json.Should().Contain("\"stars\"");
        json.Should().Contain("\"forks\"");
        json.Should().Contain("\"openIssues\"");
        json.Should().Contain("\"openPullRequests\"");
        json.Should().Contain("\"date\"");
        json.Should().Contain("\"value\"");
        json.Should().Contain("\"version\"");
    }

    [Fact]
    public void TrendData_RoundTripSerialization()
    {
        var original = new TrendData
        {
            GeneratedAt = new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero),
            WindowDays = 60
        };
        original.Packages["Pkg.Test"] = new PackageTrend
        {
            Downloads = [new TrendPoint<long> { Date = "2025-07-01", Value = 999 }],
            VersionHistory = [new VersionEvent { Date = "2025-07-01", Version = "5.0.0" }]
        };
        original.Repositories["org/repo"] = new RepositoryTrend
        {
            Stars = [new TrendPoint<int> { Date = "2025-07-01", Value = 100 }],
            Forks = [new TrendPoint<int> { Date = "2025-07-01", Value = 25 }],
            OpenIssues = [],
            OpenPullRequests = []
        };

        var json = JsonSerializer.Serialize(original, WriteOptions);
        var deserialized = JsonSerializer.Deserialize<TrendData>(json, WriteOptions);

        deserialized.Should().NotBeNull();
        deserialized!.WindowDays.Should().Be(60);
        deserialized.Packages.Should().ContainKey("Pkg.Test");
        deserialized.Packages["Pkg.Test"].Downloads.Should().ContainSingle()
            .Which.Value.Should().Be(999);
        deserialized.Repositories["org/repo"].Stars.Should().ContainSingle()
            .Which.Value.Should().Be(100);
    }

    [Fact]
    public void TrendData_DefaultsAreEmpty()
    {
        var data = new TrendData();

        data.Packages.Should().BeEmpty();
        data.Repositories.Should().BeEmpty();
        data.WindowDays.Should().Be(0);
    }

    [Fact]
    public void PackageTrend_DefaultsAreEmpty()
    {
        var trend = new PackageTrend();

        trend.Downloads.Should().BeEmpty();
        trend.VersionHistory.Should().BeEmpty();
    }

    [Fact]
    public void RepositoryTrend_DefaultsAreEmpty()
    {
        var trend = new RepositoryTrend();

        trend.Stars.Should().BeEmpty();
        trend.Forks.Should().BeEmpty();
        trend.OpenIssues.Should().BeEmpty();
        trend.OpenPullRequests.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    // Internal helper
    // ──────────────────────────────────────────────────────────────────

    private static void CreateDirInHistory(string historyRoot, int year, int month, int day)
    {
        Directory.CreateDirectory(Path.Combine(
            historyRoot,
            year.ToString(),
            month.ToString("D2"),
            day.ToString("D2")));
    }
}
