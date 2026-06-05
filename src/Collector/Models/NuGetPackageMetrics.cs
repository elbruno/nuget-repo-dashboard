using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class PackageDependency
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }
}

public sealed class DependencyMetrics
{
    [JsonPropertyName("directCount")]
    public int DirectCount { get; set; }

    [JsonPropertyName("dependencies")]
    public List<PackageDependency> Dependencies { get; set; } = [];

    [JsonPropertyName("outdatedCount")]
    public int OutdatedCount { get; set; }

    [JsonPropertyName("freshnessPercent")]
    public decimal FreshnessPercent { get; set; }
}

public sealed class NuGetPackageMetrics
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public string Authors { get; set; } = string.Empty;

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("listed")]
    public bool Listed { get; set; } = true;

    [JsonPropertyName("publishedDate")]
    public DateTimeOffset? PublishedDate { get; set; }

    [JsonPropertyName("releasesLast30Days")]
    public int ReleasesLast30Days { get; set; }

    [JsonPropertyName("dependencies")]
    public DependencyMetrics? Dependencies { get; set; }
}
