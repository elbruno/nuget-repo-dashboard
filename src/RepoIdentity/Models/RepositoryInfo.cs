namespace RepoIdentity.Models;

public record RepositoryInfo
{
    public string Owner { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Language { get; init; }
    public int Stars { get; init; }
    public DateTimeOffset LastPush { get; init; }
    public bool Archived { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
}
