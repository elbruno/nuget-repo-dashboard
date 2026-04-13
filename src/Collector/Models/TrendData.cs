using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class TrendData
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("windowDays")]
    public int WindowDays { get; set; }

    [JsonPropertyName("packages")]
    public Dictionary<string, PackageTrend> Packages { get; set; } = new();

    [JsonPropertyName("repositories")]
    public Dictionary<string, RepositoryTrend> Repositories { get; set; } = new();

    [JsonPropertyName("velocities")]
    public List<PackageVelocity> Velocities { get; set; } = [];

    [JsonPropertyName("stalePackageCount")]
    public int StalePackageCount { get; set; }

    [JsonPropertyName("issueActivity")]
    public List<IssueActivityPoint> IssueActivity { get; set; } = [];

    [JsonPropertyName("versionActivity")]
    public List<VersionActivityPoint> VersionActivity { get; set; } = [];

    [JsonPropertyName("newPackages")]
    public List<NewPackageEvent> NewPackages { get; set; } = [];
}

public sealed class PackageTrend
{
    [JsonPropertyName("downloads")]
    public List<TrendPoint<long>> Downloads { get; set; } = [];

    [JsonPropertyName("versionHistory")]
    public List<VersionEvent> VersionHistory { get; set; } = [];
}

public sealed class RepositoryTrend
{
    [JsonPropertyName("stars")]
    public List<TrendPoint<int>> Stars { get; set; } = [];

    [JsonPropertyName("forks")]
    public List<TrendPoint<int>> Forks { get; set; } = [];

    [JsonPropertyName("openIssues")]
    public List<TrendPoint<int>> OpenIssues { get; set; } = [];

    [JsonPropertyName("openPullRequests")]
    public List<TrendPoint<int>> OpenPullRequests { get; set; } = [];
}

public sealed class TrendPoint<T>
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public T Value { get; set; } = default!;
}

public sealed class VersionEvent
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class PackageVelocity
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("avgDailyDownloads")]
    public double AvgDailyDownloads { get; set; }

    [JsonPropertyName("staleDays")]
    public int StaleDays { get; set; }

    [JsonPropertyName("isStale")]
    public bool IsStale { get; set; }
}

public sealed class IssueActivityPoint
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("opened")]
    public int Opened { get; set; }

    [JsonPropertyName("closed")]
    public int Closed { get; set; }
}

public sealed class VersionActivityPoint
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("newVersions")]
    public int NewVersions { get; set; }

    [JsonPropertyName("packages")]
    public List<string> Packages { get; set; } = [];
}

public sealed class NewPackageEvent
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;
}
