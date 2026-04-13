using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface ITrendAggregationService
{
    Task<TrendData> AggregateAsync(string repoRoot, int windowDays = 90);
}

public sealed class TrendAggregationService : ITrendAggregationService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<TrendData> AggregateAsync(string repoRoot, int windowDays = 90)
    {
        var historyRoot = Path.Combine(repoRoot, "data", "history");
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-windowDays);

        var trendData = new TrendData
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            WindowDays = windowDays
        };

        if (!Directory.Exists(historyRoot))
        {
            return trendData;
        }

        // Discover all date directories: data/history/YYYY/MM/DD
        var dateDirs = DiscoverDateDirectories(historyRoot, cutoff);

        // Track version changes per package for VersionHistory
        var lastVersionByPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Track first appearance of packages for NewPackages
        var knownPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect version activity per date
        var versionActivityByDate = new Dictionary<string, List<string>>();

        // Collect issue activity per date (opened/closed counts)
        var openedByDate = new Dictionary<string, int>();
        var closedByDate = new Dictionary<string, int>();

        // Collect PR activity per date (opened/merged/closed counts)
        var prOpenedByDate = new Dictionary<string, int>();
        var prMergedByDate = new Dictionary<string, int>();
        var prClosedByDate = new Dictionary<string, int>();

        foreach (var (date, dirPath) in dateDirs)
        {
            var dateStr = date.ToString("yyyy-MM-dd");

            // Process NuGet snapshot
            var nugetPath = Path.Combine(dirPath, "data.nuget.json");
            if (File.Exists(nugetPath))
            {
                await ProcessNuGetSnapshotAsync(nugetPath, dateStr, trendData, lastVersionByPackage,
                    knownPackageIds, versionActivityByDate);
            }

            // Process repositories snapshot
            var reposPath = Path.Combine(dirPath, "data.repositories.json");
            if (File.Exists(reposPath))
            {
                await ProcessReposSnapshotAsync(reposPath, dateStr, trendData);
                await ProcessIssueActivityAsync(reposPath, dateStr, openedByDate, closedByDate);
                await ProcessPullRequestActivityAsync(reposPath, dateStr, prOpenedByDate, prMergedByDate, prClosedByDate);
            }
        }

        // Compute package velocity from download trend points
        ComputeVelocities(trendData);

        // Build version activity list
        foreach (var (dateStr, packages) in versionActivityByDate.OrderBy(kv => kv.Key))
        {
            trendData.VersionActivity.Add(new VersionActivityPoint
            {
                Date = dateStr,
                NewVersions = packages.Count,
                Packages = packages
            });
        }

        // Build issue activity list
        var allIssueDates = openedByDate.Keys.Union(closedByDate.Keys).OrderBy(d => d);
        foreach (var dateStr in allIssueDates)
        {
            openedByDate.TryGetValue(dateStr, out var opened);
            closedByDate.TryGetValue(dateStr, out var closed);
            trendData.IssueActivity.Add(new IssueActivityPoint
            {
                Date = dateStr,
                Opened = opened,
                Closed = closed
            });
        }

        // Build PR activity list
        var allPrDates = prOpenedByDate.Keys.Union(prMergedByDate.Keys).Union(prClosedByDate.Keys).OrderBy(d => d);
        foreach (var dateStr in allPrDates)
        {
            prOpenedByDate.TryGetValue(dateStr, out var prOpened);
            prMergedByDate.TryGetValue(dateStr, out var prMerged);
            prClosedByDate.TryGetValue(dateStr, out var prClosed);
            trendData.PullRequestActivity.Add(new PullRequestActivityPoint
            {
                Date = dateStr,
                Opened = prOpened,
                Merged = prMerged,
                Closed = prClosed
            });
        }

        return trendData;
    }

    /// <summary>
    /// Computes download velocity and staleness for each package from its trend download points.
    /// </summary>
    private static void ComputeVelocities(TrendData trendData)
    {
        foreach (var (packageId, trend) in trendData.Packages)
        {
            var points = trend.Downloads.OrderBy(p => p.Date).ToList();

            double avgDailyDownloads = 0;
            int staleDays = 0;

            if (points.Count >= 2)
            {
                // Calculate daily deltas
                var deltas = new List<long>();
                for (int i = 1; i < points.Count; i++)
                {
                    deltas.Add(points[i].Value - points[i - 1].Value);
                }

                // Average of the last 7 deltas (or fewer if not enough data)
                var recentDeltas = deltas.Skip(Math.Max(0, deltas.Count - 7)).ToList();
                if (recentDeltas.Count > 0)
                {
                    avgDailyDownloads = recentDeltas.Average(d => (double)d);
                }

                // Count consecutive trailing days with zero delta
                for (int i = deltas.Count - 1; i >= 0; i--)
                {
                    if (deltas[i] == 0)
                        staleDays++;
                    else
                        break;
                }
            }

            trendData.Velocities.Add(new PackageVelocity
            {
                PackageId = packageId,
                AvgDailyDownloads = Math.Round(avgDailyDownloads, 2),
                StaleDays = staleDays,
                IsStale = staleDays >= 3
            });
        }

        trendData.StalePackageCount = trendData.Velocities.Count(v => v.IsStale);
    }

    /// <summary>
    /// Scans the history directory for YYYY/MM/DD folder structures and returns them
    /// sorted chronologically, filtered to dates on or after the cutoff.
    /// </summary>
    internal static List<(DateOnly Date, string Path)> DiscoverDateDirectories(string historyRoot, DateOnly cutoff)
    {
        var result = new List<(DateOnly, string)>();

        foreach (var yearDir in Directory.EnumerateDirectories(historyRoot))
        {
            var yearName = System.IO.Path.GetFileName(yearDir);
            if (!int.TryParse(yearName, out var year)) continue;

            foreach (var monthDir in Directory.EnumerateDirectories(yearDir))
            {
                var monthName = System.IO.Path.GetFileName(monthDir);
                if (!int.TryParse(monthName, out var month) || month < 1 || month > 12) continue;

                foreach (var dayDir in Directory.EnumerateDirectories(monthDir))
                {
                    var dayName = System.IO.Path.GetFileName(dayDir);
                    if (!int.TryParse(dayName, out var day)) continue;

                    try
                    {
                        var date = new DateOnly(year, month, day);
                        if (date >= cutoff)
                        {
                            result.Add((date, dayDir));
                        }
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Invalid date — skip
                    }
                }
            }
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }

    private async Task ProcessNuGetSnapshotAsync(
        string path,
        string dateStr,
        TrendData trendData,
        Dictionary<string, string> lastVersionByPackage,
        HashSet<string> knownPackageIds,
        Dictionary<string, List<string>> versionActivityByDate)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<NuGetOutput>(stream, ReadOptions);
            if (snapshot?.Packages is null) return;

            foreach (var pkg in snapshot.Packages)
            {
                if (string.IsNullOrWhiteSpace(pkg.PackageId)) continue;

                if (!trendData.Packages.TryGetValue(pkg.PackageId, out var trend))
                {
                    trend = new PackageTrend();
                    trendData.Packages[pkg.PackageId] = trend;
                }

                trend.Downloads.Add(new TrendPoint<long>
                {
                    Date = dateStr,
                    Value = pkg.TotalDownloads
                });

                // Track first appearance of a package
                if (knownPackageIds.Add(pkg.PackageId))
                {
                    trendData.NewPackages.Add(new NewPackageEvent
                    {
                        Date = dateStr,
                        PackageId = pkg.PackageId
                    });
                }

                // Track version changes
                if (!string.IsNullOrWhiteSpace(pkg.LatestVersion))
                {
                    if (!lastVersionByPackage.TryGetValue(pkg.PackageId, out var prevVersion)
                        || prevVersion != pkg.LatestVersion)
                    {
                        trend.VersionHistory.Add(new VersionEvent
                        {
                            Date = dateStr,
                            Version = pkg.LatestVersion
                        });
                        lastVersionByPackage[pkg.PackageId] = pkg.LatestVersion;

                        // Record in version activity aggregation (skip initial appearances)
                        if (prevVersion is not null)
                        {
                            if (!versionActivityByDate.TryGetValue(dateStr, out var list))
                            {
                                list = [];
                                versionActivityByDate[dateStr] = list;
                            }
                            list.Add(pkg.PackageId);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    WARNING: Failed to read NuGet snapshot {path}: {ex.Message}");
        }
    }

    private async Task ProcessReposSnapshotAsync(string path, string dateStr, TrendData trendData)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<RepositoriesOutput>(stream, ReadOptions);
            if (snapshot?.Repositories is null) return;

            foreach (var repo in snapshot.Repositories)
            {
                var key = repo.FullName;
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!trendData.Repositories.TryGetValue(key, out var trend))
                {
                    trend = new RepositoryTrend();
                    trendData.Repositories[key] = trend;
                }

                trend.Stars.Add(new TrendPoint<int> { Date = dateStr, Value = repo.Stars });
                trend.Forks.Add(new TrendPoint<int> { Date = dateStr, Value = repo.Forks });
                trend.OpenIssues.Add(new TrendPoint<int> { Date = dateStr, Value = repo.OpenIssues });
                trend.OpenPullRequests.Add(new TrendPoint<int> { Date = dateStr, Value = repo.OpenPullRequests });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    WARNING: Failed to read repos snapshot {path}: {ex.Message}");
        }
    }

    private async Task ProcessIssueActivityAsync(
        string path,
        string dateStr,
        Dictionary<string, int> openedByDate,
        Dictionary<string, int> closedByDate)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<RepositoriesOutput>(stream, ReadOptions);
            if (snapshot?.Repositories is null) return;

            foreach (var repo in snapshot.Repositories)
            {
                // Count open issues created on this date
                foreach (var issue in repo.RecentIssues)
                {
                    var issueDate = issue.CreatedAt.ToString("yyyy-MM-dd");
                    if (issueDate == dateStr)
                    {
                        openedByDate[dateStr] = openedByDate.GetValueOrDefault(dateStr) + 1;
                    }
                }

                // Count closed issues closed on this date
                foreach (var issue in repo.RecentClosedIssues)
                {
                    if (issue.ClosedAt.HasValue)
                    {
                        var closedDate = issue.ClosedAt.Value.ToString("yyyy-MM-dd");
                        if (closedDate == dateStr)
                        {
                            closedByDate[dateStr] = closedByDate.GetValueOrDefault(dateStr) + 1;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    WARNING: Failed to read issue activity from {path}: {ex.Message}");
        }
    }

    private async Task ProcessPullRequestActivityAsync(
        string path,
        string dateStr,
        Dictionary<string, int> openedByDate,
        Dictionary<string, int> mergedByDate,
        Dictionary<string, int> closedByDate)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<RepositoriesOutput>(stream, ReadOptions);
            if (snapshot?.Repositories is null) return;

            foreach (var repo in snapshot.Repositories)
            {
                // Count open PRs created on this date
                foreach (var pr in repo.RecentPullRequests)
                {
                    var prDate = pr.CreatedAt.ToString("yyyy-MM-dd");
                    if (prDate == dateStr)
                    {
                        openedByDate[dateStr] = openedByDate.GetValueOrDefault(dateStr) + 1;
                    }
                }

                // Count merged PRs merged on this date
                foreach (var pr in repo.RecentMergedPullRequests)
                {
                    if (pr.MergedAt.HasValue)
                    {
                        var mergedDate = pr.MergedAt.Value.ToString("yyyy-MM-dd");
                        if (mergedDate == dateStr)
                        {
                            mergedByDate[dateStr] = mergedByDate.GetValueOrDefault(dateStr) + 1;
                        }
                    }

                    if (pr.ClosedAt.HasValue)
                    {
                        var closedDate = pr.ClosedAt.Value.ToString("yyyy-MM-dd");
                        if (closedDate == dateStr)
                        {
                            closedByDate[dateStr] = closedByDate.GetValueOrDefault(dateStr) + 1;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    WARNING: Failed to read PR activity from {path}: {ex.Message}");
        }
    }
}
