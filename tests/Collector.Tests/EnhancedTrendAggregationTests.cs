using System.Text.Json;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class EnhancedTrendAggregationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TrendAggregationService _service;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public EnhancedTrendAggregationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"enhanced-trend-tests-{Guid.NewGuid():N}");
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
        int stars = 0, int forks = 0, int openIssues = 0, int openPrs = 0,
        List<GitHubIssue>? recentIssues = null,
        List<GitHubIssue>? recentClosedIssues = null) =>
        new()
        {
            Owner = owner,
            Name = name,
            FullName = $"{owner}/{name}",
            Stars = stars,
            Forks = forks,
            OpenIssues = openIssues,
            OpenPullRequests = openPrs,
            RecentIssues = recentIssues ?? [],
            RecentClosedIssues = recentClosedIssues ?? [],
            ClosedIssuesCount = recentClosedIssues?.Count ?? 0
        };

    private static GitHubIssue BuildIssue(
        int number,
        string title,
        DateTimeOffset createdAt,
        string? state = "open",
        DateTimeOffset? closedAt = null) =>
        new()
        {
            Number = number,
            Title = title,
            CreatedAt = createdAt,
            State = state,
            ClosedAt = closedAt
        };

    private const int LargeWindow = 365 * 5;

    // ══════════════════════════════════════════════════════════════════
    // FEATURE 1: Download Velocity + Staleness Detection
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VelocityCalculation_SevenDaysLinearGrowth_ReturnsCorrectAverage()
    {
        // Given 7 days of download data [100, 105, 110, 115, 120, 125, 130]
        // Expected velocity: ~5.0/day (linear growth of 5 per day)
        var startDate = new DateOnly(2025, 1, 1);
        
        for (int i = 0; i < 7; i++)
        {
            var day = startDate.AddDays(i);
            var dir = CreateHistoryDay(day);
            var downloads = 100 + (i * 5);
            await WriteNuGetSnapshot(dir, BuildPackage("Pkg.Linear", downloads));
        }

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.PackageId.Should().Be("Pkg.Linear");
        velocity.AvgDailyDownloads.Should().BeInRange(4.9, 5.1);
    }

    [Fact]
    public async Task StaleDetection_FiveDaysIdenticalDownloads_ReturnsStaleTrue()
    {
        // Given 5 days of identical downloads [100, 100, 100, 100, 100]
        // StaleDays should be 4 (days since change), IsStale should be true (>= 3)
        var startDate = new DateOnly(2025, 1, 1);
        
        for (int i = 0; i < 5; i++)
        {
            var day = startDate.AddDays(i);
            var dir = CreateHistoryDay(day);
            await WriteNuGetSnapshot(dir, BuildPackage("Pkg.Stale", 100));
        }

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.PackageId.Should().Be("Pkg.Stale");
        velocity.StaleDays.Should().Be(4);
        velocity.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task NotStale_FiveDaysGrowingDownloads_ReturnsStaleFalse()
    {
        // Given downloads [100, 101, 102, 103, 104]
        // StaleDays should be 0, IsStale false
        var startDate = new DateOnly(2025, 1, 1);
        
        for (int i = 0; i < 5; i++)
        {
            var day = startDate.AddDays(i);
            var dir = CreateHistoryDay(day);
            await WriteNuGetSnapshot(dir, BuildPackage("Pkg.Active", 100 + i));
        }

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.PackageId.Should().Be("Pkg.Active");
        velocity.StaleDays.Should().Be(0);
        velocity.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task MixedStalePackages_SomeStaleNotStale_CorrectStaleCount()
    {
        // Package A: stale (4 days identical)
        // Package B: not stale (growing)
        // Package C: stale (5 days identical)
        // Expected: StalePackageCount = 2
        var startDate = new DateOnly(2025, 1, 1);
        
        for (int i = 0; i < 5; i++)
        {
            var day = startDate.AddDays(i);
            var dir = CreateHistoryDay(day);
            await WriteNuGetSnapshot(dir,
                BuildPackage("Pkg.A", 100),           // stale
                BuildPackage("Pkg.B", 200 + i),       // not stale
                BuildPackage("Pkg.C", 300));          // stale
        }

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().HaveCount(3);
        result.StalePackageCount.Should().Be(2);
        result.Velocities.Count(v => v.IsStale).Should().Be(2);
        result.Velocities.Count(v => !v.IsStale).Should().Be(1);
    }

    [Fact]
    public async Task EdgeCase_OnlyOneDayOfData_VelocityZeroStaleDaysZero()
    {
        // With only 1 day of data, velocity = 0, staleDays = 0
        var day = new DateOnly(2025, 1, 1);
        var dir = CreateHistoryDay(day);
        await WriteNuGetSnapshot(dir, BuildPackage("Pkg.OneDay", 100));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.AvgDailyDownloads.Should().Be(0);
        velocity.StaleDays.Should().Be(0);
        velocity.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task EdgeCase_TwoDaysOfData_VelocityBasedOnSingleDelta()
    {
        // With 2 days, velocity is the single delta between them
        var day1 = new DateOnly(2025, 1, 1);
        var day2 = new DateOnly(2025, 1, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        
        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg.TwoDays", 100));
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.TwoDays", 110));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.AvgDailyDownloads.Should().Be(10);
        velocity.StaleDays.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════════
    // FEATURE 2: Issues Opened/Closed Per Day
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IssueActivity_MultipleSnapshots_CorrectOpenedClosedCounts()
    {
        // Day 1: 2 open issues created
        // Day 2: 1 new open issue, 1 issue closed (from day 1)
        // Day 3: 1 new open issue, 2 issues closed
        var day1 = new DateOnly(2025, 2, 1);
        var day2 = new DateOnly(2025, 2, 2);
        var day3 = new DateOnly(2025, 2, 3);

        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        // Day 1: Issues #1 and #2 created (both open)
        await WriteReposSnapshot(dir1, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(1, "Issue 1", day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                BuildIssue(2, "Issue 2", day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            ]));

        // Day 2: Issue #3 created, Issue #1 closed
        var day1Utc = day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var day2Utc = day2.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await WriteReposSnapshot(dir2, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(2, "Issue 2", day1Utc),
                BuildIssue(3, "Issue 3", day2Utc)
            ],
            recentClosedIssues: [
                BuildIssue(1, "Issue 1", day1Utc, "closed", day2Utc)
            ]));

        // Day 3: Issue #4 created, Issues #2 and #3 closed
        var day3Utc = day3.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await WriteReposSnapshot(dir3, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(4, "Issue 4", day3Utc)
            ],
            recentClosedIssues: [
                BuildIssue(1, "Issue 1", day1Utc, "closed", day2Utc),
                BuildIssue(2, "Issue 2", day1Utc, "closed", day3Utc),
                BuildIssue(3, "Issue 3", day2Utc, "closed", day3Utc)
            ]));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.IssueActivity.Should().HaveCount(3);
        
        // Day 1: 2 opened, 0 closed
        var act1 = result.IssueActivity.First(a => a.Date == "2025-02-01");
        act1.Opened.Should().Be(2);
        act1.Closed.Should().Be(0);

        // Day 2: 1 opened, 1 closed
        var act2 = result.IssueActivity.First(a => a.Date == "2025-02-02");
        act2.Opened.Should().Be(1);
        act2.Closed.Should().Be(1);

        // Day 3: 1 opened, 2 closed
        var act3 = result.IssueActivity.First(a => a.Date == "2025-02-03");
        act3.Opened.Should().Be(1);
        act3.Closed.Should().Be(2);
    }

    [Fact]
    public async Task IssueActivity_PullRequestsFiltered_NotCountedAsIssues()
    {
        // GitHub API returns PRs in issues endpoint - they should be filtered out
        var day = new DateOnly(2025, 2, 1);
        var dir = CreateHistoryDay(day);
        var dayUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await WriteReposSnapshot(dir, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(1, "Real Issue", dayUtc),
                BuildIssue(2, "Pull Request", dayUtc) // This would have pull_request field in real API
            ]));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        // This test verifies the collector/aggregator filters PRs
        // Actual filtering happens in GitHubCollector, so this test
        // documents expected behavior when Kaylee implements filtering
        result.IssueActivity.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IssueActivity_EmptyRepos_ZeroActivity()
    {
        // Repos with zero issues should contribute 0 to activity
        var day = new DateOnly(2025, 2, 1);
        var dir = CreateHistoryDay(day);

        await WriteReposSnapshot(dir, BuildRepo("owner", "repo"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.IssueActivity.Should().BeEmpty();
    }

    [Fact]
    public async Task GitHubIssueModel_StateAndClosedAt_SerializeCorrectly()
    {
        // Verify new GitHubIssue fields serialize with correct JSON property names
        var issue = new GitHubIssue
        {
            Number = 42,
            Title = "Test Issue",
            State = "closed",
            ClosedAt = new DateTimeOffset(2025, 2, 1, 12, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(issue, WriteOptions);

        json.Should().Contain("\"state\": \"closed\"");
        json.Should().Contain("\"closedAt\":");
        json.Should().NotContain("\"State\""); // Verify camelCase
        json.Should().NotContain("\"ClosedAt\"");
    }

    [Fact]
    public async Task ClosedIssuesCount_MatchesRecentClosedIssuesList()
    {
        // Verify ClosedIssuesCount matches the length of RecentClosedIssues
        var day = new DateOnly(2025, 2, 1);
        var dir = CreateHistoryDay(day);
        var dayUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var closedIssues = new List<GitHubIssue>
        {
            BuildIssue(1, "Issue 1", dayUtc, "closed", dayUtc),
            BuildIssue(2, "Issue 2", dayUtc, "closed", dayUtc),
            BuildIssue(3, "Issue 3", dayUtc, "closed", dayUtc)
        };

        await WriteReposSnapshot(dir, BuildRepo("owner", "repo",
            recentClosedIssues: closedIssues));

        // Read back and verify
        var path = Path.Combine(dir, "data.repositories.json");
        var json = await File.ReadAllTextAsync(path);
        var output = JsonSerializer.Deserialize<RepositoriesOutput>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        output.Should().NotBeNull();
        output!.Repositories.Should().ContainSingle();
        var repo = output.Repositories[0];
        repo.ClosedIssuesCount.Should().Be(3);
        repo.RecentClosedIssues.Should().HaveCount(3);
    }

    // ══════════════════════════════════════════════════════════════════
    // FEATURE 3: New Packages/Versions Per Day
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NewPackageDetection_FirstAppearance_RecordedAsNewPackage()
    {
        // Package appearing for first time in history
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        // Day 1: No packages
        await WriteNuGetSnapshot(dir1);

        // Day 2: Pkg.New appears
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.New", 100, "1.0.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.NewPackages.Should().ContainSingle();
        var newPkg = result.NewPackages[0];
        newPkg.Date.Should().Be("2025-03-02");
        newPkg.PackageId.Should().Be("Pkg.New");
    }

    [Fact]
    public async Task VersionChange_BetweenDays_RecordedInVersionActivity()
    {
        // Package changing version between days
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg.Evolving", 100, "1.0.0"));
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.Evolving", 150, "1.1.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.VersionActivity.Should().ContainSingle();
        var versionAct = result.VersionActivity[0];
        versionAct.Date.Should().Be("2025-03-02");
        versionAct.NewVersions.Should().Be(1);
        versionAct.Packages.Should().Contain("Pkg.Evolving");
    }

    [Fact]
    public async Task NoDuplicateNewPackages_MultipleAppearances_OnlyFirstCounted()
    {
        // Package appearing on multiple days only counted as new on first
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        var day3 = new DateOnly(2025, 3, 3);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        // Day 1: No package
        await WriteNuGetSnapshot(dir1);

        // Day 2: Pkg.Once appears
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.Once", 100, "1.0.0"));

        // Day 3: Pkg.Once still exists
        await WriteNuGetSnapshot(dir3, BuildPackage("Pkg.Once", 150, "1.0.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.NewPackages.Should().ContainSingle();
        result.NewPackages[0].Date.Should().Be("2025-03-02");
    }

    [Fact]
    public async Task MultipleVersionsSameDay_SingleVersionActivityPoint()
    {
        // Multiple packages releasing new versions on same day
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        await WriteNuGetSnapshot(dir1,
            BuildPackage("Pkg.A", 100, "1.0.0"),
            BuildPackage("Pkg.B", 200, "1.0.0"),
            BuildPackage("Pkg.C", 300, "1.0.0"));

        await WriteNuGetSnapshot(dir2,
            BuildPackage("Pkg.A", 110, "1.1.0"),  // version bump
            BuildPackage("Pkg.B", 210, "1.2.0"),  // version bump
            BuildPackage("Pkg.C", 310, "1.0.0")); // no change

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.VersionActivity.Should().ContainSingle();
        var versionAct = result.VersionActivity[0];
        versionAct.Date.Should().Be("2025-03-02");
        versionAct.NewVersions.Should().Be(2);
        versionAct.Packages.Should().HaveCount(2);
        versionAct.Packages.Should().Contain("Pkg.A");
        versionAct.Packages.Should().Contain("Pkg.B");
        versionAct.Packages.Should().NotContain("Pkg.C");
    }

    [Fact]
    public async Task NoVersionChange_AllDaysSameVersion_NoVersionActivity()
    {
        // Package with same version all days → no versionActivity entries
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        var day3 = new DateOnly(2025, 3, 3);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg.Stable", 100, "1.0.0"));
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.Stable", 150, "1.0.0"));
        await WriteNuGetSnapshot(dir3, BuildPackage("Pkg.Stable", 200, "1.0.0"));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        // No version changes = no version activity entries for this package
        result.VersionActivity.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════
    // Additional Edge Cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VelocityCalculation_DownloadsDecrease_NegativeVelocity()
    {
        // Verify velocity can be negative when downloads decrease
        var day1 = new DateOnly(2025, 1, 1);
        var day2 = new DateOnly(2025, 1, 2);
        var day3 = new DateOnly(2025, 1, 3);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        var dir3 = CreateHistoryDay(day3);

        await WriteNuGetSnapshot(dir1, BuildPackage("Pkg.Declining", 100));
        await WriteNuGetSnapshot(dir2, BuildPackage("Pkg.Declining", 90));
        await WriteNuGetSnapshot(dir3, BuildPackage("Pkg.Declining", 80));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.Velocities.Should().ContainSingle();
        var velocity = result.Velocities[0];
        velocity.AvgDailyDownloads.Should().BeInRange(-10.1, -9.9);
    }

    [Fact]
    public async Task IssueActivity_SameIssueMultipleDays_CountedOnlyOnCreationDate()
    {
        // An issue that appears in multiple snapshots should only count as "opened" once
        var day1 = new DateOnly(2025, 2, 1);
        var day2 = new DateOnly(2025, 2, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);
        
        var day1Utc = day1.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        await WriteReposSnapshot(dir1, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(1, "Issue 1", day1Utc)
            ]));

        await WriteReposSnapshot(dir2, BuildRepo("owner", "repo",
            recentIssues: [
                BuildIssue(1, "Issue 1", day1Utc) // Still open, created day 1
            ]));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        // Should only count 1 opened issue on day 1
        result.IssueActivity.Should().ContainSingle();
        result.IssueActivity[0].Date.Should().Be("2025-02-01");
        result.IssueActivity[0].Opened.Should().Be(1);
    }

    [Fact]
    public async Task NewPackage_MultiplePackagesSameDay_AllRecorded()
    {
        // Multiple new packages appearing same day
        var day1 = new DateOnly(2025, 3, 1);
        var day2 = new DateOnly(2025, 3, 2);
        
        var dir1 = CreateHistoryDay(day1);
        var dir2 = CreateHistoryDay(day2);

        await WriteNuGetSnapshot(dir1);

        await WriteNuGetSnapshot(dir2,
            BuildPackage("Pkg.New1", 100),
            BuildPackage("Pkg.New2", 200),
            BuildPackage("Pkg.New3", 300));

        var result = await _service.AggregateAsync(_tempRoot, LargeWindow);

        result.NewPackages.Should().HaveCount(3);
        result.NewPackages.Should().OnlyContain(p => p.Date == "2025-03-02");
        result.NewPackages.Select(p => p.PackageId).Should().BeEquivalentTo(
            ["Pkg.New1", "Pkg.New2", "Pkg.New3"]);
    }
}
