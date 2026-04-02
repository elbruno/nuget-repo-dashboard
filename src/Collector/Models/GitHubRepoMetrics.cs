using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class GitHubRepoMetrics
{
    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("stars")]
    public int Stars { get; set; }

    [JsonPropertyName("forks")]
    public int Forks { get; set; }

    [JsonPropertyName("openIssues")]
    public int OpenIssues { get; set; }

    [JsonPropertyName("openPullRequests")]
    public int OpenPullRequests { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("lastPush")]
    public DateTimeOffset? LastPush { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("watchersCount")]
    public int WatchersCount { get; set; }

    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("defaultBranch")]
    public string? DefaultBranch { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("hasWiki")]
    public bool HasWiki { get; set; }

    [JsonPropertyName("hasPages")]
    public bool HasPages { get; set; }

    [JsonPropertyName("networkCount")]
    public int NetworkCount { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("htmlUrl")]
    public string? HtmlUrl { get; set; }
}
