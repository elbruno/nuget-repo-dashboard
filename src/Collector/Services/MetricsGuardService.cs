using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IMetricsGuardService
{
    /// <summary>
    /// Ensures download counts never decrease by comparing freshly collected metrics
    /// against previously stored values and taking the maximum.
    /// </summary>
    List<NuGetPackageMetrics> ApplyMonotonicityGuard(
        List<NuGetPackageMetrics> freshMetrics,
        List<NuGetPackageMetrics>? previousMetrics);

    /// <summary>
    /// Loads the previously stored NuGet output from data/latest/data.nuget.json.
    /// Returns null if the file doesn't exist or can't be read.
    /// </summary>
    Task<NuGetOutput?> LoadPreviousNuGetOutputAsync(string repoRoot);

    /// <summary>
    /// Checks trend data for packages with no download growth over consecutive data points.
    /// Logs warnings for packages with >100 total downloads that show 0% growth
    /// for 5 or more consecutive data points.
    /// </summary>
    void CheckStaleness(TrendData trendData, List<NuGetPackageMetrics> currentMetrics);
}

public sealed class MetricsGuardService : IMetricsGuardService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal const int StalenessThreshold = 5;
    internal const long MinDownloadsForStalenessCheck = 100;

    public List<NuGetPackageMetrics> ApplyMonotonicityGuard(
        List<NuGetPackageMetrics> freshMetrics,
        List<NuGetPackageMetrics>? previousMetrics)
    {
        if (previousMetrics is null || previousMetrics.Count == 0)
        {
            return freshMetrics;
        }

        var previousLookup = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var prev in previousMetrics)
        {
            if (!string.IsNullOrWhiteSpace(prev.PackageId))
            {
                previousLookup[prev.PackageId] = prev.TotalDownloads;
            }
        }

        foreach (var metric in freshMetrics)
        {
            if (previousLookup.TryGetValue(metric.PackageId, out var previousDownloads)
                && metric.TotalDownloads < previousDownloads)
            {
                Console.WriteLine($"  [Guard] {metric.PackageId}: kept previous value {previousDownloads:N0} (collected {metric.TotalDownloads:N0} was lower)");
                metric.TotalDownloads = previousDownloads;
            }
        }

        return freshMetrics;
    }

    public async Task<NuGetOutput?> LoadPreviousNuGetOutputAsync(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "data", "latest", "data.nuget.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<NuGetOutput>(stream, ReadOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  WARNING: Could not read previous NuGet data: {ex.Message}");
            return null;
        }
    }

    public void CheckStaleness(TrendData trendData, List<NuGetPackageMetrics> currentMetrics)
    {
        // Build a lookup of current download counts
        var currentDownloads = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in currentMetrics)
        {
            if (!string.IsNullOrWhiteSpace(m.PackageId))
            {
                currentDownloads[m.PackageId] = m.TotalDownloads;
            }
        }

        foreach (var (packageId, trend) in trendData.Packages)
        {
            // Only check packages with >100 total downloads
            if (!currentDownloads.TryGetValue(packageId, out var downloads)
                || downloads <= MinDownloadsForStalenessCheck)
            {
                continue;
            }

            var points = trend.Downloads.OrderBy(p => p.Date).ToList();
            if (points.Count < StalenessThreshold)
            {
                continue;
            }

            // Count consecutive trailing zero-growth data points
            int consecutiveZeroGrowth = CountTrailingZeroGrowth(points);

            if (consecutiveZeroGrowth >= StalenessThreshold)
            {
                Console.WriteLine($"  [Staleness] {packageId}: no download growth for {consecutiveZeroGrowth} consecutive days — API data may be stale");
            }
        }
    }

    /// <summary>
    /// Counts the number of consecutive zero-delta data points from the end of the list.
    /// </summary>
    internal static int CountTrailingZeroGrowth(List<TrendPoint<long>> points)
    {
        int count = 0;
        for (int i = points.Count - 1; i >= 1; i--)
        {
            if (points[i].Value == points[i - 1].Value)
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }
}
