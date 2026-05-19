using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

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
}
