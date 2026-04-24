using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

/// <summary>
/// Represents an entry in config/watch-list.json.
/// Watch-list entries are external (non-NuGet) repos to be monitored for metrics.
/// </summary>
public sealed class WatchListEntry
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dateAdded")]
    public string? DateAdded { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }
}
