using RepoIdentity.Models;

namespace RepoIdentity.Services;

public interface IConfigGenerator
{
    Task<GenerationResult> GenerateAsync(
        IReadOnlyList<RepositoryInfo> repositories,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
