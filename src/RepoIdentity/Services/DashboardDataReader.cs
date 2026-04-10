using System.Text.Json;
using RepoIdentity.Models;

namespace RepoIdentity.Services;

public sealed class DashboardDataReader : IDashboardDataReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DashboardData> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Dashboard data file not found: {filePath}", filePath);

        await using var stream = File.OpenRead(filePath);
        var data = await JsonSerializer.DeserializeAsync<DashboardData>(stream, Options, cancellationToken);
        return data ?? throw new InvalidDataException($"Failed to deserialize dashboard data from: {filePath}");
    }
}
