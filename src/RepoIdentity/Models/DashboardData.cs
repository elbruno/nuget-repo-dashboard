namespace RepoIdentity.Models;

public record DashboardData
{
    public DateTimeOffset GeneratedAt { get; init; }
    public IReadOnlyList<RepositoryInfo> Repositories { get; init; } = [];
}
