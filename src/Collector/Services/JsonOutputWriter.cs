using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IJsonOutputWriter
{
    Task WriteNuGetAsync(NuGetOutput output, string repoRoot);
    Task WriteRepositoriesAsync(RepositoriesOutput output, string repoRoot);
    Task WriteTrendsAsync(TrendData output, string repoRoot);
}

public sealed class JsonOutputWriter : IJsonOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteNuGetAsync(NuGetOutput output, string repoRoot)
    {
        // Write to data/latest/data.nuget.json
        var latestDir = Path.Combine(repoRoot, "data", "latest");
        Directory.CreateDirectory(latestDir);
        var latestPath = Path.Combine(latestDir, "data.nuget.json");
        await WriteJsonFileAsync(latestPath, output);
        Console.WriteLine($"  Written: {latestPath}");

        // Write to data/history/YYYY/MM/DD/data.nuget.json
        var now = output.GeneratedAt;
        var historyDir = Path.Combine(
            repoRoot, "data", "history",
            now.Year.ToString("D4"),
            now.Month.ToString("D2"),
            now.Day.ToString("D2"));
        Directory.CreateDirectory(historyDir);
        var historyPath = Path.Combine(historyDir, "data.nuget.json");
        await WriteJsonFileAsync(historyPath, output);
        Console.WriteLine($"  Written: {historyPath}");
    }

    public async Task WriteRepositoriesAsync(RepositoriesOutput output, string repoRoot)
    {
        // Write to data/latest/data.repositories.json
        var latestDir = Path.Combine(repoRoot, "data", "latest");
        Directory.CreateDirectory(latestDir);
        var latestPath = Path.Combine(latestDir, "data.repositories.json");
        await WriteJsonFileAsync(latestPath, output);
        Console.WriteLine($"  Written: {latestPath}");

        // Write to data/history/YYYY/MM/DD/data.repositories.json
        var now = output.GeneratedAt;
        var historyDir = Path.Combine(
            repoRoot, "data", "history",
            now.Year.ToString("D4"),
            now.Month.ToString("D2"),
            now.Day.ToString("D2"));
        Directory.CreateDirectory(historyDir);
        var historyPath = Path.Combine(historyDir, "data.repositories.json");
        await WriteJsonFileAsync(historyPath, output);
        Console.WriteLine($"  Written: {historyPath}");
    }

    public async Task WriteTrendsAsync(TrendData output, string repoRoot)
    {
        // Write to data/latest/data.trends.json only (no history copy — trends are derived data)
        var latestDir = Path.Combine(repoRoot, "data", "latest");
        Directory.CreateDirectory(latestDir);
        var latestPath = Path.Combine(latestDir, "data.trends.json");
        await WriteJsonFileAsync(latestPath, output);
        Console.WriteLine($"  Written: {latestPath}");
    }

    private static async Task WriteJsonFileAsync<T>(string path, T output)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, JsonOptions);
    }
}
