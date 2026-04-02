using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class DashboardOutput
{
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("packages")]
    public List<NuGetPackageMetrics> Packages { get; set; } = [];

    [JsonPropertyName("repos")]
    public List<GitHubRepoMetrics> Repos { get; set; } = [];
}
