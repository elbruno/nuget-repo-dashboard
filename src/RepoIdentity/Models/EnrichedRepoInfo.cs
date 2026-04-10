namespace RepoIdentity.Models;

public record EnrichedRepoInfo
{
    public string Owner { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Language { get; init; }
    public int Stars { get; init; }
    public string HtmlUrl { get; init; } = string.Empty;
    // Computed/enriched fields:
    public string AccentColor { get; init; } = string.Empty;  // hex color
    public string? Icon { get; init; }
    public string? Type { get; init; }
}
