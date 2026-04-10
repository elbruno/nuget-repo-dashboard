using System.Text.Json;
using RepoIdentity.Models;

namespace RepoIdentity.Services;

public sealed class MetadataEnricher : IMetadataEnricher
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public async Task<RepoIdentityMetadata?> TryReadAsync(string repoLocalPath, CancellationToken cancellationToken = default)
    {
        var identityFile = Path.Combine(repoLocalPath, "repo.identity.json");
        if (!File.Exists(identityFile))
            return null;

        try
        {
            await using var stream = File.OpenRead(identityFile);
            return await JsonSerializer.DeserializeAsync<RepoIdentityMetadata>(stream, Options, cancellationToken);
        }
        catch (Exception)
        {
            // If the file is malformed, treat as if it doesn't exist
            return null;
        }
    }
}
