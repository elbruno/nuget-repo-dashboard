using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

// Legacy: kept for backward compatibility with existing tests
public sealed class DashboardOutput
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("packages")]
    public List<NuGetPackageMetrics> Packages { get; set; } = [];

    [JsonPropertyName("repos")]
    public List<GitHubRepoMetrics> Repos { get; set; } = [];
}

public sealed class NuGetOutput
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("packages")]
    public List<NuGetPackageMetrics> Packages { get; set; } = [];
}

public sealed class RepositoriesOutput
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("repositories")]
    public List<GitHubRepoMetrics> Repositories { get; set; } = [];

    [JsonPropertyName("watchList")]
    public List<WatchListRepoMetrics> WatchList { get; set; } = [];
}

public sealed class DashboardMetadataOutput
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("nugetGeneratedAt")]
    public DateTimeOffset? NuGetGeneratedAt { get; set; }

    [JsonPropertyName("repositoriesGeneratedAt")]
    public DateTimeOffset? RepositoriesGeneratedAt { get; set; }

    [JsonPropertyName("trendsGeneratedAt")]
    public DateTimeOffset? TrendsGeneratedAt { get; set; }
}
