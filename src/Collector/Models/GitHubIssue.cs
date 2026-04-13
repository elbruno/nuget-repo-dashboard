using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class GitHubIssue
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("htmlUrl")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("userLogin")]
    public string? UserLogin { get; set; }

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];

    [JsonPropertyName("commentsCount")]
    public int CommentsCount { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }
}
