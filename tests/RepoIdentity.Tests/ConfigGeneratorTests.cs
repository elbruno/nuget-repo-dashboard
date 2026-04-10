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

    [Fact]
    public async Task GenerateAsync_ProfileHasConsoleTitleTemplate()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "TestRepo", "C#") };
        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "elbruno-TestRepo.json"));
            var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("console_title_template", out var titleProp).Should().BeTrue();
            titleProp.GetString().Should().Contain("TestRepo");
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_SegmentHasSolidBackground()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "TestRepo", "C#") };
        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "elbruno-TestRepo.json"));
            var doc = JsonDocument.Parse(json);
            var segment = doc.RootElement
                .GetProperty("blocks")[0]
                .GetProperty("segments")[0];
            var bg = segment.GetProperty("background").GetString();
            bg.Should().NotBe("transparent");
            bg.Should().MatchRegex(@"^#[0-9A-Fa-f]{6}$");
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_SegmentHasContrastForeground()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = new[] { MakeRepo("elbruno", "TestRepo", "C#") };
        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "elbruno-TestRepo.json"));
            var doc = JsonDocument.Parse(json);
            var segment = doc.RootElement
                .GetProperty("blocks")[0]
                .GetProperty("segments")[0];
            var fg = segment.GetProperty("foreground").GetString();
            fg.Should().BeOneOf("#FFFFFF", "#1C1C1C");
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Theory]
    [InlineData("nuget-mcp-server", "🔌")]
    [InlineData("whisper-net", "🎙️")]
    [InlineData("tts-azure", "🔊")]
    [InlineData("semantic-memory", "🧠")]
    [InlineData("qrcode-generator", "📷")]
    [InlineData("llm-helper", "🤖")]
    [InlineData("vision-demo", "🖼️")]
    [InlineData("normal-csharp-lib", "🔷")]  // C# fallback
    [InlineData("unknown-lang-repo", "📦")]   // no language, no keyword
    public async Task GenerateAsync_IconMatchesPurposeOrLanguage(string repoName, string expectedIcon)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var language = repoName.Contains("csharp") ? "C#" : null;
        var repos = new[] { MakeRepo("elbruno", repoName, language) };
        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, $"elbruno-{repoName}.json"));
            json.Should().Contain(expectedIcon);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }

    [Fact]
    public async Task GenerateAsync_MultipleReposHaveDistinctColors()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var repos = Enumerable.Range(1, 10)
            .Select(i => MakeRepo("elbruno", $"repo-{i}"))
            .ToArray();
        try
        {
            await _sut.GenerateAsync(repos, outputDir);
            var colors = new List<(int r, int g, int b)>();
            for (int i = 1; i <= 10; i++)
            {
                var json = await File.ReadAllTextAsync(Path.Combine(outputDir, $"elbruno-repo-{i}.json"));
                var doc = JsonDocument.Parse(json);
                var bg = doc.RootElement.GetProperty("blocks")[0].GetProperty("segments")[0]
                    .GetProperty("background").GetString()!;
                var r = Convert.ToInt32(bg[1..3], 16);
                var g = Convert.ToInt32(bg[3..5], 16);
                var b = Convert.ToInt32(bg[5..7], 16);
                colors.Add((r, g, b));
            }
            // All colors should be distinct (no duplicates)
            colors.Distinct().Should().HaveCount(colors.Count);
        }
        finally { Directory.Delete(outputDir, recursive: true); }
    }
}
