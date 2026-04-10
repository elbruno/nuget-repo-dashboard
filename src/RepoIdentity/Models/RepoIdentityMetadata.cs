namespace RepoIdentity.Models;

public record RepoIdentityMetadata
{
    public string? Name { get; init; }
    public string? Type { get; init; }        // e.g. "library", "tool", "sample"
    public string? AccentColor { get; init; } // hex color e.g. "#0078D4"
    public string? Icon { get; init; }        // emoji e.g. "🧠"
}
