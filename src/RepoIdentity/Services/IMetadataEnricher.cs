using RepoIdentity.Models;

namespace RepoIdentity.Services;

public interface IMetadataEnricher
{
    /// <summary>
    /// Looks for a repo.identity.json in the given local repo directory.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    Task<RepoIdentityMetadata?> TryReadAsync(string repoLocalPath, CancellationToken cancellationToken = default);
}
