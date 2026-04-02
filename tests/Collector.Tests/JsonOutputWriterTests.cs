using System.Text.Json;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class JsonOutputWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly JsonOutputWriter _writer;

    public JsonOutputWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"collector-writer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _writer = new JsonOutputWriter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    private static DashboardOutput CreateSampleOutput(DateTimeOffset? generatedAt = null)
    {
        return new DashboardOutput
        {
            GeneratedAt = generatedAt ?? new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            Packages =
            [
                new NuGetPackageMetrics
                {
                    PackageId = "TestPkg",
                    LatestVersion = "1.0.0",
                    TotalDownloads = 5000,
                    Description = "A test package",
                    Authors = "Tester",
                    Listed = true,
                    Tags = ["test"]
                }
            ],
            Repos =
            [
                new GitHubRepoMetrics
                {
                    Owner = "owner",
                    Name = "repo",
                    FullName = "owner/repo",
                    Stars = 42,
                    Forks = 5,
                    OpenIssues = 2,
                    OpenPullRequests = 1
                }
            ]
        };
    }

    [Fact]
    public async Task WriteAsync_CreatesLatestDataJson()
    {
        var output = CreateSampleOutput();

        await _writer.WriteAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_CreatesHistoryDataJson()
    {
        var dt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var output = CreateSampleOutput(dt);

        await _writer.WriteAsync(output, _tempRoot);

        var historyPath = Path.Combine(_tempRoot, "data", "history", "2024", "06", "15", "data.json");
        File.Exists(historyPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryStructureIfMissing()
    {
        var output = CreateSampleOutput();
        var nestedRoot = Path.Combine(_tempRoot, "deep", "nested", "root");
        // Directory doesn't exist yet — writer should create it
        Directory.Exists(nestedRoot).Should().BeFalse();

        await _writer.WriteAsync(output, nestedRoot);

        var latestPath = Path.Combine(nestedRoot, "data", "latest", "data.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_OutputJsonIsValidAndDeserializable()
    {
        var output = CreateSampleOutput();

        await _writer.WriteAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<DashboardOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Packages.Should().HaveCount(1);
        deserialized.Repos.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteAsync_JsonContainsExpectedPropertyNames()
    {
        var output = CreateSampleOutput();

        await _writer.WriteAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        var json = await File.ReadAllTextAsync(latestPath);

        // Verify camelCase property names from the JSON serialization
        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"packages\"");
        json.Should().Contain("\"repos\"");
        json.Should().Contain("\"packageId\"");
        json.Should().Contain("\"latestVersion\"");
        json.Should().Contain("\"totalDownloads\"");
        json.Should().Contain("\"stars\"");
        json.Should().Contain("\"forks\"");
    }

    [Fact]
    public async Task WriteAsync_IsIndentedJson()
    {
        var output = CreateSampleOutput();

        await _writer.WriteAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        var json = await File.ReadAllTextAsync(latestPath);

        // Indented JSON has newlines
        json.Should().Contain("\n");
    }

    [Fact]
    public async Task WriteAsync_EmptyCollections_ProducesValidJson()
    {
        var output = new DashboardOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = [],
            Repos = []
        };

        await _writer.WriteAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<DashboardOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Packages.Should().BeEmpty();
        deserialized.Repos.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile()
    {
        var output1 = CreateSampleOutput();
        await _writer.WriteAsync(output1, _tempRoot);

        var output2 = new DashboardOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = [],
            Repos = []
        };
        await _writer.WriteAsync(output2, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<DashboardOutput>(json);

        deserialized!.Packages.Should().BeEmpty();
    }
}
