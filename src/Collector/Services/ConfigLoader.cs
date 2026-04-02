using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IConfigLoader
{
    Task<List<PackageConfig>> LoadAsync(string configPath);
}

public sealed class ConfigLoader : IConfigLoader
{
    public async Task<List<PackageConfig>> LoadAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        await using var stream = File.OpenRead(configPath);
        var packages = await System.Text.Json.JsonSerializer.DeserializeAsync<List<PackageConfig>>(stream);

        if (packages is null || packages.Count == 0)
        {
            throw new InvalidOperationException("Config file is empty or invalid.");
        }

        // Validate entries
        for (int i = 0; i < packages.Count; i++)
        {
            var pkg = packages[i];
            if (string.IsNullOrWhiteSpace(pkg.PackageId))
            {
                throw new InvalidOperationException($"Entry {i} has an empty packageId.");
            }
        }

        return packages;
    }
}
