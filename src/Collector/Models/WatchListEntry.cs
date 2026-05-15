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

/// <summary>
/// Represents the watch-list section written to data.repositories.json.
/// Combines static watch-list metadata with live GitHub metrics.
/// </summary>
public sealed class WatchListRepoMetrics
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("htmlUrl")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("dateAdded")]
    public string? DateAdded { get; set; }

    [JsonPropertyName("stars")]
    public int? Stars { get; set; }

    [JsonPropertyName("lastUpdate")]
    public DateTimeOffset? LastUpdate { get; set; }

    [JsonPropertyName("isStub")]
    public bool IsStub { get; set; }

    [JsonPropertyName("stubReason")]
    public string? StubReason { get; set; }
}
