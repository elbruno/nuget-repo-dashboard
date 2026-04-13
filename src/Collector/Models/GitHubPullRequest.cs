using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class GitHubPullRequest
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

    [JsonPropertyName("mergedAt")]
    public DateTimeOffset? MergedAt { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }

    [JsonPropertyName("userLogin")]
    public string? UserLogin { get; set; }

    [JsonPropertyName("labels")]
    public List<string> Labels { get; set; } = [];

    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("additions")]
    public int Additions { get; set; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; set; }

    [JsonPropertyName("changedFiles")]
    public int ChangedFiles { get; set; }

    [JsonPropertyName("reviewDecision")]
    public string? ReviewDecision { get; set; }

    [JsonPropertyName("headBranch")]
    public string? HeadBranch { get; set; }

    [JsonPropertyName("baseBranch")]
    public string? BaseBranch { get; set; }

    [JsonPropertyName("commentsCount")]
    public int CommentsCount { get; set; }
}
