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

    private static NuGetOutput CreateSampleNuGetOutput(DateTimeOffset? generatedAt = null)
    {
        return new NuGetOutput
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
            ]
        };
    }

    private static RepositoriesOutput CreateSampleRepositoriesOutput(DateTimeOffset? generatedAt = null)
    {
        return new RepositoriesOutput
        {
            GeneratedAt = generatedAt ?? new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            Repositories =
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
            ],
            WatchList =
            [
                new WatchListRepoMetrics
                {
                    Owner = "elbruno",
                    Repo = "openclawnet",
                    FullName = "elbruno/openclawnet",
                    Purpose = "Reference architecture",
                    Stars = 42
                }
            ]
        };
    }

    #region WriteNuGetAsync Tests

    [Fact]
    public async Task WriteNuGetAsync_CreatesLatestDataNuGetJson()
    {
        var output = CreateSampleNuGetOutput();

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteNuGetAsync_CreatesHistoryDataNuGetJson()
    {
        var dt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var output = CreateSampleNuGetOutput(dt);

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var historyPath = Path.Combine(_tempRoot, "data", "history", "2024", "06", "15", "data.nuget.json");
        File.Exists(historyPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteNuGetAsync_CreatesDirectoryStructureIfMissing()
    {
        var output = CreateSampleNuGetOutput();
        var nestedRoot = Path.Combine(_tempRoot, "deep", "nested", "root");
        Directory.Exists(nestedRoot).Should().BeFalse();

        await _writer.WriteNuGetAsync(output, nestedRoot);

        var latestPath = Path.Combine(nestedRoot, "data", "latest", "data.nuget.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteNuGetAsync_OutputJsonIsValidAndDeserializable()
    {
        var output = CreateSampleNuGetOutput();

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<NuGetOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Packages.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteNuGetAsync_JsonContainsExpectedPropertyNames()
    {
        var output = CreateSampleNuGetOutput();

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        var json = await File.ReadAllTextAsync(latestPath);

        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"packages\"");
        json.Should().Contain("\"packageId\"");
        json.Should().Contain("\"latestVersion\"");
        json.Should().Contain("\"totalDownloads\"");
    }

    [Fact]
    public async Task WriteNuGetAsync_IsIndentedJson()
    {
        var output = CreateSampleNuGetOutput();

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        var json = await File.ReadAllTextAsync(latestPath);

        json.Should().Contain("\n");
    }

    [Fact]
    public async Task WriteNuGetAsync_EmptyCollections_ProducesValidJson()
    {
        var output = new NuGetOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = []
        };

        await _writer.WriteNuGetAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<NuGetOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Packages.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteNuGetAsync_OverwritesExistingFile()
    {
        var output1 = CreateSampleNuGetOutput();
        await _writer.WriteNuGetAsync(output1, _tempRoot);

        var output2 = new NuGetOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = []
        };
        await _writer.WriteNuGetAsync(output2, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.nuget.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<NuGetOutput>(json);

        deserialized!.Packages.Should().BeEmpty();
    }

    #endregion

    #region WriteRepositoriesAsync Tests

    [Fact]
    public async Task WriteRepositoriesAsync_CreatesLatestDataRepositoriesJson()
    {
        var output = CreateSampleRepositoriesOutput();

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteRepositoriesAsync_CreatesHistoryDataRepositoriesJson()
    {
        var dt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var output = CreateSampleRepositoriesOutput(dt);

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var historyPath = Path.Combine(_tempRoot, "data", "history", "2024", "06", "15", "data.repositories.json");
        File.Exists(historyPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteRepositoriesAsync_CreatesDirectoryStructureIfMissing()
    {
        var output = CreateSampleRepositoriesOutput();
        var nestedRoot = Path.Combine(_tempRoot, "deep", "nested", "root");
        Directory.Exists(nestedRoot).Should().BeFalse();

        await _writer.WriteRepositoriesAsync(output, nestedRoot);

        var latestPath = Path.Combine(nestedRoot, "data", "latest", "data.repositories.json");
        File.Exists(latestPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteRepositoriesAsync_OutputJsonIsValidAndDeserializable()
    {
        var output = CreateSampleRepositoriesOutput();

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<RepositoriesOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Repositories.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteRepositoriesAsync_JsonContainsExpectedPropertyNames()
    {
        var output = CreateSampleRepositoriesOutput();

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        var json = await File.ReadAllTextAsync(latestPath);

        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"repositories\"");
        json.Should().Contain("\"watchList\"");
        json.Should().Contain("\"stars\"");
        json.Should().Contain("\"forks\"");
    }

    [Fact]
    public async Task WriteRepositoriesAsync_IsIndentedJson()
    {
        var output = CreateSampleRepositoriesOutput();

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        var json = await File.ReadAllTextAsync(latestPath);

        json.Should().Contain("\n");
    }

    [Fact]
    public async Task WriteRepositoriesAsync_EmptyCollections_ProducesValidJson()
    {
        var output = new RepositoriesOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Repositories = []
        };

        await _writer.WriteRepositoriesAsync(output, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<RepositoriesOutput>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Repositories.Should().BeEmpty();
        deserialized.WatchList.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteRepositoriesAsync_OverwritesExistingFile()
    {
        var output1 = CreateSampleRepositoriesOutput();
        await _writer.WriteRepositoriesAsync(output1, _tempRoot);

        var output2 = new RepositoriesOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Repositories = []
        };
        await _writer.WriteRepositoriesAsync(output2, _tempRoot);

        var latestPath = Path.Combine(_tempRoot, "data", "latest", "data.repositories.json");
        var json = await File.ReadAllTextAsync(latestPath);
        var deserialized = JsonSerializer.Deserialize<RepositoriesOutput>(json);

        deserialized!.Repositories.Should().BeEmpty();
        deserialized.WatchList.Should().BeEmpty();
    }

    #endregion
}
