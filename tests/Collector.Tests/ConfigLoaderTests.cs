using FluentAssertions;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"collector-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string WriteTempFile(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidConfig_ReturnsPackageConfigList()
    {
        var json = """
        [
          { "packageId": "Newtonsoft.Json", "repos": ["JamesNK/Newtonsoft.Json"] },
          { "packageId": "Serilog", "repos": ["serilog/serilog", "serilog/serilog-sinks-console"] }
        ]
        """;
        var path = WriteTempFile("valid.json", json);

        var result = await _loader.LoadAsync(path);

        result.Should().HaveCount(2);
        result[0].PackageId.Should().Be("Newtonsoft.Json");
        result[0].Repos.Should().ContainSingle().Which.Should().Be("JamesNK/Newtonsoft.Json");
        result[1].PackageId.Should().Be("Serilog");
        result[1].Repos.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Config file not found*");
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ThrowsException()
    {
        var path = WriteTempFile("invalid.json", "{ not valid json !!!");

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task LoadAsync_EmptyArray_ThrowsInvalidOperationException()
    {
        var path = WriteTempFile("empty.json", "[]");

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty or invalid*");
    }

    [Fact]
    public async Task LoadAsync_NullContent_ThrowsInvalidOperationException()
    {
        var path = WriteTempFile("null.json", "null");

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty or invalid*");
    }

    [Fact]
    public async Task LoadAsync_EntryWithEmptyPackageId_ThrowsInvalidOperationException()
    {
        var json = """
        [
          { "packageId": "", "repos": ["owner/repo"] }
        ]
        """;
        var path = WriteTempFile("empty-id.json", json);

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty packageId*");
    }

    [Fact]
    public async Task LoadAsync_EntryWithMissingPackageId_ThrowsInvalidOperationException()
    {
        var json = """
        [
          { "repos": ["owner/repo"] }
        ]
        """;
        var path = WriteTempFile("missing-id.json", json);

        var act = () => _loader.LoadAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty packageId*");
    }

    [Fact]
    public async Task LoadAsync_SingleValidEntry_ReturnsSingleItem()
    {
        var json = """
        [
          { "packageId": "xunit", "repos": [] }
        ]
        """;
        var path = WriteTempFile("single.json", json);

        var result = await _loader.LoadAsync(path);

        result.Should().ContainSingle();
        result[0].PackageId.Should().Be("xunit");
        result[0].Repos.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_PreservesRepoOrder()
    {
        var json = """
        [
          { "packageId": "TestPkg", "repos": ["c/c", "a/a", "b/b"] }
        ]
        """;
        var path = WriteTempFile("order.json", json);

        var result = await _loader.LoadAsync(path);

        result[0].Repos.Should().ContainInOrder("c/c", "a/a", "b/b");
    }
}
