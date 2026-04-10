using RepoIdentity.Models;
using RepoIdentity.Services;
using FluentAssertions;
using System.Text.Json;

namespace RepoIdentity.Tests;

public class ConfigGeneratorTests
{
    private readonly ConfigGenerator _sut = new(new ColorGenerator());

    private static RepositoryInfo MakeRepo(string owner, string name, string? language = "C#", bool archived = false) => new()
    {
        Owner = owner,
        Name = name,
        FullName = $"{owner}/{name}",
        Language = language,
        Stars = 1,
        LastPush = DateTimeOffset.UtcNow,
        Archived = archived,
        HtmlUrl = $"https://github.com/{owner}/{name}"
    };

    [Fact]
    public async Task GenerateAsync_CreatesOneFilePerActiveRepo()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "RepoA"), MakeRepo("elbruno", "RepoB") };

        try
        {
            var result = await _sut.GenerateAsync(repos, outputDir);
            result.FilesGenerated.Should().Be(2);
            File.Exists(Path.Combine(outputDir, "elbruno-RepoA.json")).Should().BeTrue();
            File.Exists(Path.Combine(outputDir, "elbruno-RepoB.json")).Should().BeTrue();
            File.Exists(Path.Combine(outputDir, "index.json")).Should().BeTrue();
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_SkipsArchivedRepos()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "Active"), MakeRepo("elbruno", "Archived", archived: true) };

        try
        {
            var result = await _sut.GenerateAsync(repos, outputDir);
            result.FilesGenerated.Should().Be(1);
            File.Exists(Path.Combine(outputDir, "elbruno-Active.json")).Should().BeTrue();
            File.Exists(Path.Combine(outputDir, "elbruno-Archived.json")).Should().BeFalse();
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_ProducesValidOhMyPoshJson()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "TestRepo", "C#") };

        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "elbruno-TestRepo.json"));
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("$schema").GetString().Should().Contain("oh-my-posh");
            doc.RootElement.GetProperty("version").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("blocks").GetArrayLength().Should().Be(1);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_IndexContainsAllProfiles()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "RepoA"), MakeRepo("elbruno", "RepoB") };

        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var indexJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "index.json"));
            var doc = JsonDocument.Parse(indexJson);

            doc.RootElement.GetProperty("totalRepos").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("profiles").GetArrayLength().Should().Be(2);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Theory]
    [InlineData("elbruno/my-repo", "elbruno-my-repo")]
    [InlineData("owner/Repo Name", "owner-Repo Name")]
    public void SanitizeFileName_ReplacesSlashWithDash(string input, string expected)
    {
        ConfigGenerator.SanitizeFileName(input).Should().Be(expected);
    }
}
