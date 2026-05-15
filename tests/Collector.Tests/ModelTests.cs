using System.Text.Json;
using FluentAssertions;
using NuGetDashboard.Collector.Models;

namespace Collector.Tests;

public class ModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region DashboardOutput

    [Fact]
    public void DashboardOutput_SerializeDeserialize_RoundTrip()
    {
        var original = new DashboardOutput
        {
            GeneratedAt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            Packages =
            [
                new NuGetPackageMetrics
                {
                    PackageId = "TestPkg",
                    LatestVersion = "2.0.0",
                    TotalDownloads = 100000,
                    Description = "Test desc",
                    Authors = "Author",
                    ProjectUrl = "https://example.com",
                    Tags = ["tag1", "tag2"],
                    Listed = true,
                    PublishedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                }
            ],
            Repos =
            [
                new GitHubRepoMetrics
                {
                    Owner = "owner",
                    Name = "repo",
                    FullName = "owner/repo",
                    Stars = 500,
                    Forks = 100,
                    OpenIssues = 10,
                    OpenPullRequests = 3,
                    Description = "A repo",
                    Language = "C#",
                    License = "MIT",
                    LastPush = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    Archived = false
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DashboardOutput>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.GeneratedAt.Should().Be(original.GeneratedAt);
        deserialized.Packages.Should().HaveCount(1);
        deserialized.Repos.Should().HaveCount(1);
    }

    [Fact]
    public void DashboardOutput_DefaultValues_AreEmpty()
    {
        var output = new DashboardOutput();

        output.Packages.Should().BeEmpty();
        output.Repos.Should().BeEmpty();
    }

    #endregion

    #region PackageConfig

    [Fact]
    public void PackageConfig_SerializeDeserialize_RoundTrip()
    {
        var original = new PackageConfig
        {
            PackageId = "Newtonsoft.Json",
            Repos = ["JamesNK/Newtonsoft.Json", "dotnet/runtime"]
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PackageConfig>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("Newtonsoft.Json");
        deserialized.Repos.Should().HaveCount(2);
    }

    [Fact]
    public void PackageConfig_JsonPropertyNames_MatchContract()
    {
        var config = new PackageConfig { PackageId = "Test", Repos = ["a/b"] };
        var json = JsonSerializer.Serialize(config);

        json.Should().Contain("\"packageId\"");
        json.Should().Contain("\"repos\"");
    }

    [Fact]
    public void PackageConfig_DefaultValues()
    {
        var config = new PackageConfig();

        config.PackageId.Should().BeEmpty();
        config.Repos.Should().BeEmpty();
    }

    #endregion

    #region NuGetPackageMetrics

    [Fact]
    public void NuGetPackageMetrics_SerializeDeserialize_RoundTrip()
    {
        var original = new NuGetPackageMetrics
        {
            PackageId = "TestPkg",
            LatestVersion = "3.0.1",
            TotalDownloads = 999999,
            Description = "A great package",
            Authors = "Author Name",
            ProjectUrl = "https://github.com/test",
            Tags = ["tag1", "tag2", "tag3"],
            Listed = true,
            PublishedDate = new DateTimeOffset(2024, 3, 15, 8, 0, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<NuGetPackageMetrics>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be(original.PackageId);
        deserialized.LatestVersion.Should().Be(original.LatestVersion);
        deserialized.TotalDownloads.Should().Be(original.TotalDownloads);
        deserialized.Description.Should().Be(original.Description);
        deserialized.Authors.Should().Be(original.Authors);
        deserialized.ProjectUrl.Should().Be(original.ProjectUrl);
        deserialized.Tags.Should().BeEquivalentTo(original.Tags);
        deserialized.Listed.Should().Be(original.Listed);
        deserialized.PublishedDate.Should().Be(original.PublishedDate);
    }

    [Fact]
    public void NuGetPackageMetrics_NullOptionalFields_SerializesCorrectly()
    {
        var metrics = new NuGetPackageMetrics
        {
            PackageId = "Minimal",
            LatestVersion = "1.0.0",
            ProjectUrl = null,
            PublishedDate = null
        };

        var json = JsonSerializer.Serialize(metrics, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<NuGetPackageMetrics>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.ProjectUrl.Should().BeNull();
        deserialized.PublishedDate.Should().BeNull();
    }

    [Fact]
    public void NuGetPackageMetrics_JsonPropertyNames_MatchContract()
    {
        var metrics = new NuGetPackageMetrics
        {
            PackageId = "P",
            LatestVersion = "1.0",
            TotalDownloads = 1,
            Tags = ["x"]
        };
        var json = JsonSerializer.Serialize(metrics);

        json.Should().Contain("\"packageId\"");
        json.Should().Contain("\"latestVersion\"");
        json.Should().Contain("\"totalDownloads\"");
        json.Should().Contain("\"description\"");
        json.Should().Contain("\"authors\"");
        json.Should().Contain("\"tags\"");
        json.Should().Contain("\"listed\"");
    }

    [Fact]
    public void NuGetPackageMetrics_DefaultValues()
    {
        var metrics = new NuGetPackageMetrics();

        metrics.PackageId.Should().BeEmpty();
        metrics.LatestVersion.Should().BeEmpty();
        metrics.TotalDownloads.Should().Be(0);
        metrics.Description.Should().BeEmpty();
        metrics.Authors.Should().BeEmpty();
        metrics.ProjectUrl.Should().BeNull();
        metrics.Tags.Should().BeEmpty();
        metrics.Listed.Should().BeTrue();
        metrics.PublishedDate.Should().BeNull();
    }

    #endregion

    #region NuGetOutput

    [Fact]
    public void NuGetOutput_SerializeDeserialize_RoundTrip()
    {
        var original = new NuGetOutput
        {
            GeneratedAt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            Packages =
            [
                new NuGetPackageMetrics
                {
                    PackageId = "TestPkg",
                    LatestVersion = "2.0.0",
                    TotalDownloads = 100000
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<NuGetOutput>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.GeneratedAt.Should().Be(original.GeneratedAt);
        deserialized.Packages.Should().HaveCount(1);
    }

    [Fact]
    public void NuGetOutput_JsonPropertyNames_MatchContract()
    {
        var output = new NuGetOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Packages = []
        };
        var json = JsonSerializer.Serialize(output);

        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"packages\"");
    }

    [Fact]
    public void NuGetOutput_DefaultValues_AreEmpty()
    {
        var output = new NuGetOutput();

        output.Packages.Should().BeEmpty();
    }

    #endregion

    #region RepositoriesOutput

    [Fact]
    public void RepositoriesOutput_SerializeDeserialize_RoundTrip()
    {
        var original = new RepositoriesOutput
        {
            GeneratedAt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero),
            Repositories =
            [
                new GitHubRepoMetrics
                {
                    Owner = "owner",
                    Name = "repo",
                    FullName = "owner/repo",
                    Stars = 500
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
                    Stars = 123
                }
            ]
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RepositoriesOutput>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.GeneratedAt.Should().Be(original.GeneratedAt);
        deserialized.Repositories.Should().HaveCount(1);
        deserialized.WatchList.Should().HaveCount(1);
    }

    [Fact]
    public void RepositoriesOutput_JsonPropertyNames_MatchContract()
    {
        var output = new RepositoriesOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Repositories = []
        };
        var json = JsonSerializer.Serialize(output);

        json.Should().Contain("\"generatedAt\"");
        json.Should().Contain("\"repositories\"");
        json.Should().Contain("\"watchList\"");
    }

    [Fact]
    public void RepositoriesOutput_DefaultValues_AreEmpty()
    {
        var output = new RepositoriesOutput();

        output.Repositories.Should().BeEmpty();
        output.WatchList.Should().BeEmpty();
    }

    #endregion

    #region GitHubRepoMetrics

    [Fact]
    public void GitHubRepoMetrics_SerializeDeserialize_RoundTrip()
    {
        var original = new GitHubRepoMetrics
        {
            Owner = "dotnet",
            Name = "runtime",
            FullName = "dotnet/runtime",
            Stars = 15000,
            Forks = 4500,
            OpenIssues = 2000,
            OpenPullRequests = 150,
            Description = "The .NET runtime",
            Language = "C#",
            License = "MIT",
            LastPush = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Archived = false,
            WatchersCount = 850,
            Topics = ["dotnet", "runtime", "csharp"],
            CreatedAt = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Size = 1024000,
            DefaultBranch = "main",
            Homepage = "https://dot.net",
            HasWiki = true,
            HasPages = true,
            NetworkCount = 500,
            Visibility = "public",
            HtmlUrl = "https://github.com/dotnet/runtime",
            IsWatchList = true,
            WatchPurpose = "Reference architecture",
            WatchDateAdded = "2025-04-02"
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GitHubRepoMetrics>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Owner.Should().Be(original.Owner);
        deserialized.Name.Should().Be(original.Name);
        deserialized.FullName.Should().Be(original.FullName);
        deserialized.Stars.Should().Be(original.Stars);
        deserialized.Forks.Should().Be(original.Forks);
        deserialized.OpenIssues.Should().Be(original.OpenIssues);
        deserialized.OpenPullRequests.Should().Be(original.OpenPullRequests);
        deserialized.Description.Should().Be(original.Description);
        deserialized.Language.Should().Be(original.Language);
        deserialized.License.Should().Be(original.License);
        deserialized.LastPush.Should().Be(original.LastPush);
        deserialized.Archived.Should().Be(original.Archived);
        deserialized.WatchersCount.Should().Be(original.WatchersCount);
        deserialized.Topics.Should().BeEquivalentTo(original.Topics);
        deserialized.CreatedAt.Should().Be(original.CreatedAt);
        deserialized.UpdatedAt.Should().Be(original.UpdatedAt);
        deserialized.Size.Should().Be(original.Size);
        deserialized.DefaultBranch.Should().Be(original.DefaultBranch);
        deserialized.Homepage.Should().Be(original.Homepage);
        deserialized.HasWiki.Should().Be(original.HasWiki);
        deserialized.HasPages.Should().Be(original.HasPages);
        deserialized.NetworkCount.Should().Be(original.NetworkCount);
        deserialized.Visibility.Should().Be(original.Visibility);
        deserialized.HtmlUrl.Should().Be(original.HtmlUrl);
        deserialized.IsWatchList.Should().BeTrue();
        deserialized.WatchPurpose.Should().Be("Reference architecture");
        deserialized.WatchDateAdded.Should().Be("2025-04-02");
    }

    [Fact]
    public void GitHubRepoMetrics_NullOptionalFields_SerializesCorrectly()
    {
        var metrics = new GitHubRepoMetrics
        {
            Owner = "owner",
            Name = "repo",
            FullName = "owner/repo",
            Description = null,
            Language = null,
            License = null,
            LastPush = null,
            CreatedAt = null,
            UpdatedAt = null,
            DefaultBranch = null,
            Homepage = null,
            Visibility = null,
            HtmlUrl = null
        };

        var json = JsonSerializer.Serialize(metrics, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GitHubRepoMetrics>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Description.Should().BeNull();
        deserialized.Language.Should().BeNull();
        deserialized.License.Should().BeNull();
        deserialized.LastPush.Should().BeNull();
        deserialized.CreatedAt.Should().BeNull();
        deserialized.UpdatedAt.Should().BeNull();
        deserialized.DefaultBranch.Should().BeNull();
        deserialized.Homepage.Should().BeNull();
        deserialized.Visibility.Should().BeNull();
        deserialized.HtmlUrl.Should().BeNull();
    }

    [Fact]
    public void GitHubRepoMetrics_JsonPropertyNames_MatchContract()
    {
        var metrics = new GitHubRepoMetrics
        {
            Owner = "o",
            Name = "r",
            FullName = "o/r",
            Topics = ["topic1"],
            DefaultBranch = "main",
            Homepage = "https://example.com",
            Visibility = "public",
            HtmlUrl = "https://github.com/o/r",
            IsWatchList = true,
            WatchPurpose = "Purpose",
            WatchDateAdded = "2025-04-02"
        };
        var json = JsonSerializer.Serialize(metrics);

        json.Should().Contain("\"owner\"");
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"fullName\"");
        json.Should().Contain("\"stars\"");
        json.Should().Contain("\"forks\"");
        json.Should().Contain("\"openIssues\"");
        json.Should().Contain("\"openPullRequests\"");
        json.Should().Contain("\"archived\"");
        json.Should().Contain("\"watchersCount\"");
        json.Should().Contain("\"topics\"");
        json.Should().Contain("\"createdAt\"");
        json.Should().Contain("\"updatedAt\"");
        json.Should().Contain("\"size\"");
        json.Should().Contain("\"defaultBranch\"");
        json.Should().Contain("\"homepage\"");
        json.Should().Contain("\"hasWiki\"");
        json.Should().Contain("\"hasPages\"");
        json.Should().Contain("\"networkCount\"");
        json.Should().Contain("\"visibility\"");
        json.Should().Contain("\"htmlUrl\"");
        json.Should().Contain("\"isWatchList\"");
        json.Should().Contain("\"watchPurpose\"");
        json.Should().Contain("\"watchDateAdded\"");
    }

    [Fact]
    public void GitHubRepoMetrics_DefaultValues()
    {
        var metrics = new GitHubRepoMetrics();

        metrics.Owner.Should().BeEmpty();
        metrics.Name.Should().BeEmpty();
        metrics.FullName.Should().BeEmpty();
        metrics.Stars.Should().Be(0);
        metrics.Forks.Should().Be(0);
        metrics.OpenIssues.Should().Be(0);
        metrics.OpenPullRequests.Should().Be(0);
        metrics.Description.Should().BeNull();
        metrics.Language.Should().BeNull();
        metrics.License.Should().BeNull();
        metrics.LastPush.Should().BeNull();
        metrics.Archived.Should().BeFalse();
        metrics.WatchersCount.Should().Be(0);
        metrics.Topics.Should().BeEmpty();
        metrics.CreatedAt.Should().BeNull();
        metrics.UpdatedAt.Should().BeNull();
        metrics.Size.Should().Be(0);
        metrics.DefaultBranch.Should().BeNull();
        metrics.Homepage.Should().BeNull();
        metrics.HasWiki.Should().BeFalse();
        metrics.HasPages.Should().BeFalse();
        metrics.NetworkCount.Should().Be(0);
        metrics.Visibility.Should().BeNull();
        metrics.HtmlUrl.Should().BeNull();
        metrics.IsWatchList.Should().BeFalse();
        metrics.WatchPurpose.Should().BeNull();
        metrics.WatchDateAdded.Should().BeNull();
    }

    [Fact]
    public void GitHubRepoMetrics_EnrichedFields_SerializeCorrectly()
    {
        var metrics = new GitHubRepoMetrics
        {
            Owner = "owner",
            Name = "repo",
            FullName = "owner/repo",
            WatchersCount = 125,
            Topics = ["csharp", "dotnet", "nuget"],
            CreatedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Size = 2048,
            DefaultBranch = "main",
            Homepage = "https://example.com",
            HasWiki = true,
            HasPages = false,
            NetworkCount = 42,
            Visibility = "public",
            HtmlUrl = "https://github.com/owner/repo"
        };

        var json = JsonSerializer.Serialize(metrics, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GitHubRepoMetrics>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.WatchersCount.Should().Be(125);
        deserialized.Topics.Should().BeEquivalentTo(["csharp", "dotnet", "nuget"]);
        deserialized.CreatedAt.Should().Be(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        deserialized.UpdatedAt.Should().Be(new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero));
        deserialized.Size.Should().Be(2048);
        deserialized.DefaultBranch.Should().Be("main");
        deserialized.Homepage.Should().Be("https://example.com");
        deserialized.HasWiki.Should().BeTrue();
        deserialized.HasPages.Should().BeFalse();
        deserialized.NetworkCount.Should().Be(42);
        deserialized.Visibility.Should().Be("public");
        deserialized.HtmlUrl.Should().Be("https://github.com/owner/repo");
    }

    #endregion
}
