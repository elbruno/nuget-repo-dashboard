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

        foreach (var (date, dirPath) in dateDirs)
        {
            var dateStr = date.ToString("yyyy-MM-dd");

            // Process NuGet snapshot
            var nugetPath = Path.Combine(dirPath, "data.nuget.json");
            if (File.Exists(nugetPath))
            {
                await ProcessNuGetSnapshotAsync(nugetPath, dateStr, trendData, lastVersionByPackage);
            }

            // Process repositories snapshot
            var reposPath = Path.Combine(dirPath, "data.repositories.json");
            if (File.Exists(reposPath))
            {
                await ProcessReposSnapshotAsync(reposPath, dateStr, trendData);
            }
        }

        return trendData;
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
        Dictionary<string, string> lastVersionByPackage)
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
}
