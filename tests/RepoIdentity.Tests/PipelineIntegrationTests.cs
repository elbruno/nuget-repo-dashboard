using FluentAssertions;
using RepoIdentity.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepoIdentity.Tests;

public class PipelineIntegrationTests
{
    private static string FindDataFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "nuget-repo-dashboard.sln")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "data", "latest", "data.repositories.json");
    }

    [Fact]
    public async Task FullPipeline_ReadsRealData_ReturnsNonEmptyRepoList()
    {
        var reader = new DashboardDataReader();

        var data = await reader.ReadAsync(FindDataFile());

        data.Repositories.Should().NotBeEmpty();
        data.Repositories.Should().AllSatisfy(r =>
        {
            r.FullName.Should().NotBeNullOrEmpty();
            r.HtmlUrl.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task FullPipeline_GeneratesProfileForEachActiveRepo()
    {
        var reader = new DashboardDataReader();
        var generator = new ConfigGenerator(new ColorGenerator());
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var data = await reader.ReadAsync(FindDataFile());
            var activeRepos = data.Repositories.Where(r => !r.Archived).ToList();

            var result = await generator.GenerateAsync(data.Repositories, outputDir);

            result.FilesGenerated.Should().Be(activeRepos.Count);
            File.Exists(Path.Combine(outputDir, "index.json")).Should().BeTrue();

            foreach (var repo in activeRepos)
            {
                var expectedFile = Path.Combine(outputDir, ConfigGenerator.SanitizeFileName(repo.FullName) + ".json");
                File.Exists(expectedFile).Should().BeTrue($"profile file for {repo.FullName} should exist");
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullPipeline_AllGeneratedProfilesAreValidJson()
    {
        var reader = new DashboardDataReader();
        var generator = new ConfigGenerator(new ColorGenerator());
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var data = await reader.ReadAsync(FindDataFile());
            await generator.GenerateAsync(data.Repositories, outputDir);

            var profileFiles = Directory.GetFiles(outputDir, "*.json")
                .Where(f => Path.GetFileName(f) != "index.json")
                .ToList();

            profileFiles.Should().NotBeEmpty();

            foreach (var file in profileFiles)
            {
                var json = await File.ReadAllTextAsync(file);
                var doc = JsonDocument.Parse(json); // must not throw

                doc.RootElement.GetProperty("$schema").GetString()
                    .Should().StartWith("https://raw.githubusercontent.com/JanDeDobbeleer");
                doc.RootElement.GetProperty("version").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("blocks").GetArrayLength().Should().BeGreaterThan(0);
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullPipeline_ColorsAreDeterministic_OnMultipleRuns()
    {
        var reader = new DashboardDataReader();
        var data = await reader.ReadAsync(FindDataFile());
        var activeRepo = data.Repositories.First(r => !r.Archived);

        var generator = new ConfigGenerator(new ColorGenerator());
        var outputDir1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var outputDir2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            await generator.GenerateAsync(data.Repositories, outputDir1);
            await generator.GenerateAsync(data.Repositories, outputDir2);

            var fileName = ConfigGenerator.SanitizeFileName(activeRepo.FullName) + ".json";

            var doc1 = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir1, fileName)));
            var doc2 = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir2, fileName)));

            var color1 = doc1.RootElement
                .GetProperty("blocks")[0].GetProperty("segments")[0].GetProperty("foreground").GetString();
            var color2 = doc2.RootElement
                .GetProperty("blocks")[0].GetProperty("segments")[0].GetProperty("foreground").GetString();

            color1.Should().Be(color2);
        }
        finally
        {
            if (Directory.Exists(outputDir1)) Directory.Delete(outputDir1, recursive: true);
            if (Directory.Exists(outputDir2)) Directory.Delete(outputDir2, recursive: true);
        }
    }

    [Fact]
    public async Task FullPipeline_IndexContainsAllActiveRepos()
    {
        var reader = new DashboardDataReader();
        var generator = new ConfigGenerator(new ColorGenerator());
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        try
        {
            var data = await reader.ReadAsync(FindDataFile());
            var activeCount = data.Repositories.Count(r => !r.Archived);

            await generator.GenerateAsync(data.Repositories, outputDir);

            var indexJson = await File.ReadAllTextAsync(Path.Combine(outputDir, "index.json"));
            var doc = JsonDocument.Parse(indexJson);

            doc.RootElement.GetProperty("totalRepos").GetInt32().Should().Be(activeCount);

            var profiles = doc.RootElement.GetProperty("profiles");
            profiles.GetArrayLength().Should().Be(activeCount);

            foreach (var profile in profiles.EnumerateArray())
            {
                profile.GetProperty("repo").GetString().Should().NotBeNullOrEmpty();
                profile.GetProperty("configFile").GetString().Should().NotBeNullOrEmpty();
                profile.GetProperty("accentColor").GetString().Should().NotBeNullOrEmpty();
            }
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task FullPipeline_GeneratedProfileHasCorrectStructure()
    {
        var reader = new DashboardDataReader();
        var generator = new ConfigGenerator(new ColorGenerator());
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var hexColorRegex = new Regex(@"^#[0-9A-F]{6}$");

        try
        {
            var data = await reader.ReadAsync(FindDataFile());
            var activeRepo = data.Repositories.First(r => !r.Archived);

            await generator.GenerateAsync(data.Repositories, outputDir);

            var fileName = ConfigGenerator.SanitizeFileName(activeRepo.FullName) + ".json";
            var json = await File.ReadAllTextAsync(Path.Combine(outputDir, fileName));
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("$schema").GetString()
                .Should().StartWith("https://raw.githubusercontent.com/JanDeDobbeleer");
            doc.RootElement.GetProperty("version").GetInt32().Should().Be(2);

            var segment = doc.RootElement
                .GetProperty("blocks")[0].GetProperty("segments")[0];

            var foreground = segment.GetProperty("foreground").GetString()!;
            hexColorRegex.IsMatch(foreground).Should().BeTrue($"foreground '{foreground}' should be a valid hex color");

            var template = segment.GetProperty("template").GetString()!;
            template.Should().Contain(activeRepo.Name);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
