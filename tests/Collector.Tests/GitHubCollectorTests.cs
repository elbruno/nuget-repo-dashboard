using System.Net;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class GitHubCollectorTests
{
    private static string BuildRepoJson(
        int stars = 100,
        int forks = 25,
        int openIssues = 10,
        string? description = "A test repo",
        string? language = "C#",
        string? license = "MIT",
        string? pushedAt = "2024-06-01T12:00:00Z",
        bool archived = false,
        int subscribersCount = 50,
        List<string>? topics = null,
        string? createdAt = "2020-01-01T00:00:00Z",
        string? updatedAt = "2024-06-01T12:00:00Z",
        int size = 1024,
        string? defaultBranch = "main",
        string? homepage = null,
        bool hasWiki = false,
        bool hasPages = false,
        int networkCount = 10,
        string? visibility = "public",
        string? htmlUrl = "https://github.com/owner/repo")
    {
        var descJson = description is null ? "null" : $"\"{description}\"";
        var langJson = language is null ? "null" : $"\"{language}\"";
        var pushedJson = pushedAt is null ? "null" : $"\"{pushedAt}\"";
        var createdJson = createdAt is null ? "null" : $"\"{createdAt}\"";
        var updatedJson = updatedAt is null ? "null" : $"\"{updatedAt}\"";
        var homepageJson = homepage is null ? "null" : $"\"{homepage}\"";
        var visibilityJson = visibility is null ? "null" : $"\"{visibility}\"";
        var htmlUrlJson = htmlUrl is null ? "null" : $"\"{htmlUrl}\"";
        var defaultBranchJson = defaultBranch is null ? "null" : $"\"{defaultBranch}\"";
        var licenseJson = license is null
            ? "null"
            : $$"""{ "spdx_id": "{{license}}", "name": "{{license}} License" }""";
        var topicsJson = topics is null || topics.Count == 0
            ? "[]"
            : $"[{string.Join(",", topics.Select(t => $"\"{t}\""))}]";

        return $$"""
        {
          "stargazers_count": {{stars}},
          "forks_count": {{forks}},
          "open_issues_count": {{openIssues}},
          "description": {{descJson}},
          "language": {{langJson}},
          "license": {{licenseJson}},
          "pushed_at": {{pushedJson}},
          "archived": {{archived.ToString().ToLower()}},
          "subscribers_count": {{subscribersCount}},
          "topics": {{topicsJson}},
          "created_at": {{createdJson}},
          "updated_at": {{updatedJson}},
          "size": {{size}},
          "default_branch": {{defaultBranchJson}},
          "homepage": {{homepageJson}},
          "has_wiki": {{hasWiki.ToString().ToLower()}},
          "has_pages": {{hasPages.ToString().ToLower()}},
          "network_count": {{networkCount}},
          "visibility": {{visibilityJson}},
          "html_url": {{htmlUrlJson}}
        }
        """;
    }

    private static string BuildPullsJson(int count)
    {
        if (count == 0) return "[]";
        var items = Enumerable.Range(1, count).Select(i => $$"""{ "number": {{i}} }""");
        return $"[{string.Join(",", items)}]";
    }

    [Fact]
    public async Task CollectAsync_KnownRepo_ReturnsCorrectMetrics()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/testowner/testrepo",
            HttpStatusCode.OK,
            BuildRepoJson(stars: 500, forks: 120, openIssues: 15, description: "Awesome repo", language: "C#", license: "Apache-2.0"));
        handler.AddResponse(
            "https://api.github.com/repos/testowner/testrepo/pulls?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            BuildPullsJson(3));
        handler.AddResponse(
            "https://api.github.com/repos/testowner/testrepo/pulls?state=closed&sort=updated&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["testowner/testrepo"]);

        results.Should().ContainSingle();
        var m = results[0];
        m.Owner.Should().Be("testowner");
        m.Name.Should().Be("testrepo");
        m.FullName.Should().Be("testowner/testrepo");
        m.Stars.Should().Be(500);
        m.Forks.Should().Be(120);
        m.OpenIssues.Should().Be(15);
        m.Description.Should().Be("Awesome repo");
        m.Language.Should().Be("C#");
        m.License.Should().Be("Apache-2.0");
        m.OpenPullRequests.Should().Be(3);
        m.Archived.Should().BeFalse();
    }

    [Fact]
    public async Task CollectAsync_RepoNotFound404_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["nonexistent/repo"]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_RateLimited403_HandlesGracefully()
    {
        var handler = new MockHttpMessageHandler();
        var resetEpoch = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds().ToString();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.Forbidden,
            "{}",
            new Dictionary<string, string>
            {
                ["X-RateLimit-Remaining"] = "0",
                ["X-RateLimit-Reset"] = resetEpoch
            });

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        // Should handle gracefully — either retry or return empty
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ZeroPullRequests_ReturnsZero()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson());
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].OpenPullRequests.Should().Be(0);
    }

    [Fact]
    public async Task CollectAsync_InvalidRepoFormat_SkipsEntry()
    {
        var handler = new MockHttpMessageHandler();

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["invalid-no-slash"]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_NetworkError_HandlesGracefully()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetCustomHandler(_ => throw new HttpRequestException("DNS resolution failed"));

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_EmptyRepoList_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync([]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_ArchivedRepo_SetsArchivedTrue()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/archived-repo",
            HttpStatusCode.OK,
            BuildRepoJson(archived: true));
        handler.AddResponse(
            "https://api.github.com/repos/owner/archived-repo/pulls?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");
        handler.AddResponse(
            "https://api.github.com/repos/owner/archived-repo/pulls?state=closed&sort=updated&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/archived-repo"]);

        results.Should().ContainSingle();
        results[0].Archived.Should().BeTrue();
    }

    [Fact]
    public async Task CollectAsync_NullOptionalFields_HandledCorrectly()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/minimal",
            HttpStatusCode.OK,
            BuildRepoJson(description: null, language: null, license: null, pushedAt: null));
        handler.AddResponse(
            "https://api.github.com/repos/owner/minimal/pulls?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");
        handler.AddResponse(
            "https://api.github.com/repos/owner/minimal/pulls?state=closed&sort=updated&direction=desc&per_page=40",
            HttpStatusCode.OK,
            "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/minimal"]);

        results.Should().ContainSingle();
        var m = results[0];
        m.Description.Should().BeNull();
        m.Language.Should().BeNull();
        m.License.Should().BeNull();
        m.LastPush.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_MultipleRepos_OneFailsOthersSucceed()
    {
        var handler = new MockHttpMessageHandler();

        // First repo succeeds
        handler.AddResponse("https://api.github.com/repos/owner/repo1", HttpStatusCode.OK, BuildRepoJson(stars: 10));
        handler.AddResponse("https://api.github.com/repos/owner/repo1/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo1/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        // Second repo 404
        handler.AddResponse("https://api.github.com/repos/owner/repo2", HttpStatusCode.NotFound, "");

        // Third repo succeeds
        handler.AddResponse("https://api.github.com/repos/owner/repo3", HttpStatusCode.OK, BuildRepoJson(stars: 30));
        handler.AddResponse("https://api.github.com/repos/owner/repo3/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo3/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo1", "owner/repo2", "owner/repo3"]);

        results.Should().HaveCount(2);
        results[0].Stars.Should().Be(10);
        results[1].Stars.Should().Be(30);
    }

    [Fact]
    public async Task CollectAsync_ParsesLastPushDate()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(pushedAt: "2024-12-25T10:30:00Z"));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].LastPush.Should().NotBeNull();
        results[0].LastPush!.Value.Year.Should().Be(2024);
        results[0].LastPush!.Value.Month.Should().Be(12);
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_WatchersCount()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(subscribersCount: 125));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].WatchersCount.Should().Be(125);
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_Topics()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(topics: ["csharp", "dotnet", "nuget"]));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].Topics.Should().BeEquivalentTo(["csharp", "dotnet", "nuget"]);
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_CreatedAndUpdatedAt()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(
                createdAt: "2020-01-15T10:00:00Z",
                updatedAt: "2024-06-20T14:30:00Z"));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].CreatedAt.Should().NotBeNull();
        results[0].CreatedAt!.Value.Year.Should().Be(2020);
        results[0].CreatedAt!.Value.Month.Should().Be(1);
        results[0].UpdatedAt.Should().NotBeNull();
        results[0].UpdatedAt!.Value.Year.Should().Be(2024);
        results[0].UpdatedAt!.Value.Month.Should().Be(6);
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_SizeAndBranch()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(size: 2048, defaultBranch: "develop"));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].Size.Should().Be(2048);
        results[0].DefaultBranch.Should().Be("develop");
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_HomepageAndUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(
                homepage: "https://example.com",
                htmlUrl: "https://github.com/owner/repo"));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].Homepage.Should().Be("https://example.com");
        results[0].HtmlUrl.Should().Be("https://github.com/owner/repo");
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_WikiAndPages()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(hasWiki: true, hasPages: false));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].HasWiki.Should().BeTrue();
        results[0].HasPages.Should().BeFalse();
    }

    [Fact]
    public async Task CollectAsync_ParsesEnrichedFields_NetworkCountAndVisibility()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/repo",
            HttpStatusCode.OK,
            BuildRepoJson(networkCount: 42, visibility: "public"));
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/repo/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        results[0].NetworkCount.Should().Be(42);
        results[0].Visibility.Should().Be("public");
    }

    [Fact]
    public async Task CollectAsync_EnrichedFields_HandleNullOptionalFields()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.github.com/repos/owner/minimal",
            HttpStatusCode.OK,
            BuildRepoJson(
                description: null,
                language: null,
                license: null,
                pushedAt: null,
                topics: null,
                createdAt: null,
                updatedAt: null,
                defaultBranch: null,
                homepage: null,
                visibility: null,
                htmlUrl: null));
        handler.AddResponse("https://api.github.com/repos/owner/minimal/pulls?state=open&sort=created&direction=desc&per_page=40", HttpStatusCode.OK, "[]");
        handler.AddResponse("https://api.github.com/repos/owner/minimal/pulls?state=closed&sort=updated&direction=desc&per_page=40", HttpStatusCode.OK, "[]");

        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/minimal"]);

        results.Should().ContainSingle();
        var m = results[0];
        m.Description.Should().BeNull();
        m.Language.Should().BeNull();
        m.License.Should().BeNull();
        m.LastPush.Should().BeNull();
        m.Topics.Should().BeEmpty();
        m.CreatedAt.Should().BeNull();
        m.UpdatedAt.Should().BeNull();
        m.DefaultBranch.Should().BeNull();
        m.Homepage.Should().BeNull();
        m.Visibility.Should().BeNull();
        m.HtmlUrl.Should().BeNull();
    }
}
