using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

/// <summary>
/// A package discovered from a NuGet user profile, including its resolved GitHub repo.
/// </summary>
public sealed class DiscoveredPackage
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonPropertyName("gitHubRepo")]
    public string? GitHubRepo { get; set; }
}
