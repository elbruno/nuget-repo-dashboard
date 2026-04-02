using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

/// <summary>
/// Represents an entry in config/tracked-packages.json.
/// Maps a NuGet package to its source GitHub repo(s).
/// </summary>
public sealed class PackageConfig
{
    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("repos")]
    public List<string> Repos { get; set; } = [];
}
