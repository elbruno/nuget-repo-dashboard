using RepoIdentity.Models;

namespace RepoIdentity.Services;

public interface IDashboardDataReader
{
    Task<DashboardData> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
