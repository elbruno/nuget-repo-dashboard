using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IJsonOutputWriter
{
    Task WriteAsync(DashboardOutput output, string repoRoot);
}

public sealed class JsonOutputWriter : IJsonOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteAsync(DashboardOutput output, string repoRoot)
    {
        // Write to data/latest/data.json
        var latestDir = Path.Combine(repoRoot, "data", "latest");
        Directory.CreateDirectory(latestDir);
        var latestPath = Path.Combine(latestDir, "data.json");
        await WriteJsonFileAsync(latestPath, output);
        Console.WriteLine($"  Written: {latestPath}");

        // Write to data/history/YYYY/MM/DD/data.json
        var now = output.GeneratedAt;
        var historyDir = Path.Combine(
            repoRoot, "data", "history",
            now.Year.ToString("D4"),
            now.Month.ToString("D2"),
            now.Day.ToString("D2"));
        Directory.CreateDirectory(historyDir);
        var historyPath = Path.Combine(historyDir, "data.json");
        await WriteJsonFileAsync(historyPath, output);
        Console.WriteLine($"  Written: {historyPath}");
    }

    private static async Task WriteJsonFileAsync(string path, DashboardOutput output)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, output, JsonOptions);
    }
}
