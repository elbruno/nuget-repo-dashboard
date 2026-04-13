using System.Net;
using System.Text.Json;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class GitHubPullRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string ApiBase = "https://api.github.com";

    #region Helpers

    private static string BuildRepoJson(
        int stars = 100, int forks = 25, int openIssues = 10,
        string? description = "A test repo", string? language = "C#",
        string? license = "MIT", string? pushedAt = "2024-06-01T12:00:00Z",
        bool archived = false)
    {
        var descJson = description is null ? "null" : $"\"{description}\"";
        var langJson = language is null ? "null" : $"\"{language}\"";
        var pushedJson = pushedAt is null ? "null" : $"\"{pushedAt}\"";
        var licenseJson = license is null
            ? "null"
            : $$"""{ "spdx_id": "{{license}}", "name": "{{license}} License" }""";

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
          "subscribers_count": 50,
          "topics": [],
          "created_at": "2020-01-01T00:00:00Z",
          "updated_at": "2024-06-01T12:00:00Z",
          "size": 1024,
          "default_branch": "main",
          "homepage": null,
          "has_wiki": false,
          "has_pages": false,
          "network_count": 10,
          "visibility": "public",
          "html_url": "https://github.com/owner/repo"
        }
        """;
    }

    private static string BuildPullRequestJson(
        int number, string title, string state = "open",
        string? createdAt = "2024-06-01T10:00:00Z",
        string? updatedAt = "2024-06-02T10:00:00Z",
        string? mergedAt = null, string? closedAt = null,
        string? userLogin = "octocat", bool draft = false,
        List<string>? labels = null,
        int additions = 10, int deletions = 5, int changedFiles = 3,
        string? reviewDecision = null,
        string headBranch = "feature-branch", string baseBranch = "main",
        int comments = 2,
        string? htmlUrl = null)
    {
        htmlUrl ??= $"https://github.com/owner/repo/pull/{number}";
        var mergedAtJson = mergedAt is null ? "null" : $"\"{mergedAt}\"";
        var closedAtJson = closedAt is null ? "null" : $"\"{closedAt}\"";
        var userJson = userLogin is null ? "null" : $$"""{ "login": "{{userLogin}}" }""";
        var labelsJson = labels is null || labels.Count == 0
            ? "[]"
            : $"[{string.Join(",", labels.Select(l => $$"""{ "name": "{{l}}" }"""))}]";
        var reviewDecisionJson = reviewDecision is null ? "" : $"""
  , "review_decision": "{reviewDecision}"
""";

        return $$"""
        {
          "number": {{number}},
          "title": "{{title}}",
          "html_url": "{{htmlUrl}}",
          "state": "{{state}}",
          "created_at": "{{createdAt}}",
          "updated_at": "{{updatedAt}}",
          "merged_at": {{mergedAtJson}},
          "closed_at": {{closedAtJson}},
          "draft": {{draft.ToString().ToLower()}},
          "user": {{userJson}},
          "labels": {{labelsJson}},
          "head": { "ref": "{{headBranch}}" },
          "base": { "ref": "{{baseBranch}}" },
          "additions": {{additions}},
          "deletions": {{deletions}},
          "changed_files": {{changedFiles}},
          "comments": {{comments}}{{reviewDecisionJson}}
        }
        """;
    }

    /// <summary>
    /// Registers all standard mock responses needed by CollectAsync for a repo,
    /// including the new PR endpoints. Returns the handler for further customization.
    /// </summary>
    private static MockHttpMessageHandler CreateHandlerWithStandardEndpoints(
        string owner = "owner", string repo = "repo",
        string? repoJson = null,
        string? openPrsJson = null,
        string? closedPrsJson = null,
        string? openIssuesJson = null)
    {
        var handler = new MockHttpMessageHandler();

        // Repo metadata
        handler.AddResponse(
            $"{ApiBase}/repos/{owner}/{repo}",
            HttpStatusCode.OK,
            repoJson ?? BuildRepoJson());

        // Open PRs (new endpoint pattern from Kaylee's refactoring)
        handler.AddResponse(
            $"{ApiBase}/repos/{owner}/{repo}/pulls?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            openPrsJson ?? "[]");

        // Closed/Merged PRs (new endpoint pattern)
        handler.AddResponse(
            $"{ApiBase}/repos/{owner}/{repo}/pulls?state=closed&sort=updated&direction=desc&per_page=40",
            HttpStatusCode.OK,
            closedPrsJson ?? "[]");

        // Open issues (static URL — same as existing)
        handler.AddResponse(
            $"{ApiBase}/repos/{owner}/{repo}/issues?state=open&sort=created&direction=desc&per_page=40",
            HttpStatusCode.OK,
            openIssuesJson ?? "[]");

        // Closed issues URL has a dynamic `since` param, so we can't register it exactly.
        // The default 404 handler causes the collector to gracefully return an empty list.

        return handler;
    }

    #endregion

    #region A. Model Tests — GitHubPullRequest

    [Fact]
    public void GitHubPullRequest_DefaultValues_AreCorrect()
    {
        var pr = new GitHubPullRequest();

        pr.Number.Should().Be(0);
        pr.Title.Should().BeEmpty();
        pr.HtmlUrl.Should().BeNull();
        pr.CreatedAt.Should().Be(default);
        pr.UpdatedAt.Should().Be(default);
        pr.MergedAt.Should().BeNull();
        pr.ClosedAt.Should().BeNull();
        pr.UserLogin.Should().BeNull();
        pr.Labels.Should().BeEmpty();
        pr.IsDraft.Should().BeFalse();
        pr.State.Should().BeNull();
        pr.Additions.Should().Be(0);
        pr.Deletions.Should().Be(0);
        pr.ChangedFiles.Should().Be(0);
        pr.ReviewDecision.Should().BeNull();
        pr.HeadBranch.Should().BeNull();
        pr.BaseBranch.Should().BeNull();
        pr.CommentsCount.Should().Be(0);
    }

    [Fact]
    public void GitHubPullRequest_Serialization_RoundTrip()
    {
        var original = new GitHubPullRequest
        {
            Number = 42,
            Title = "Add PR collection",
            HtmlUrl = "https://github.com/owner/repo/pull/42",
            CreatedAt = new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2024, 6, 2, 14, 30, 0, TimeSpan.Zero),
            MergedAt = new DateTimeOffset(2024, 6, 2, 15, 0, 0, TimeSpan.Zero),
            ClosedAt = new DateTimeOffset(2024, 6, 2, 15, 0, 0, TimeSpan.Zero),
            UserLogin = "octocat",
            Labels = ["bug", "enhancement"],
            IsDraft = false,
            State = "closed",
            Additions = 120,
            Deletions = 45,
            ChangedFiles = 8,
            ReviewDecision = "APPROVED",
            HeadBranch = "feature/pr-collection",
            BaseBranch = "main",
            CommentsCount = 5
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GitHubPullRequest>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Number.Should().Be(original.Number);
        deserialized.Title.Should().Be(original.Title);
        deserialized.HtmlUrl.Should().Be(original.HtmlUrl);
        deserialized.CreatedAt.Should().Be(original.CreatedAt);
        deserialized.UpdatedAt.Should().Be(original.UpdatedAt);
        deserialized.MergedAt.Should().Be(original.MergedAt);
        deserialized.ClosedAt.Should().Be(original.ClosedAt);
        deserialized.UserLogin.Should().Be(original.UserLogin);
        deserialized.Labels.Should().BeEquivalentTo(original.Labels);
        deserialized.IsDraft.Should().Be(original.IsDraft);
        deserialized.State.Should().Be(original.State);
        deserialized.Additions.Should().Be(original.Additions);
        deserialized.Deletions.Should().Be(original.Deletions);
        deserialized.ChangedFiles.Should().Be(original.ChangedFiles);
        deserialized.ReviewDecision.Should().Be(original.ReviewDecision);
        deserialized.HeadBranch.Should().Be(original.HeadBranch);
        deserialized.BaseBranch.Should().Be(original.BaseBranch);
        deserialized.CommentsCount.Should().Be(original.CommentsCount);
    }

    [Fact]
    public void GitHubPullRequest_JsonPropertyNames_AreCamelCase()
    {
        var pr = new GitHubPullRequest
        {
            Number = 1,
            Title = "Test",
            Labels = ["x"],
            IsDraft = true,
            State = "open",
            HeadBranch = "dev",
            BaseBranch = "main"
        };
        var json = JsonSerializer.Serialize(pr);

        json.Should().Contain("\"number\"");
        json.Should().Contain("\"title\"");
        json.Should().Contain("\"htmlUrl\"");
        json.Should().Contain("\"createdAt\"");
        json.Should().Contain("\"updatedAt\"");
        json.Should().Contain("\"mergedAt\"");
        json.Should().Contain("\"closedAt\"");
        json.Should().Contain("\"userLogin\"");
        json.Should().Contain("\"labels\"");
        json.Should().Contain("\"isDraft\"");
        json.Should().Contain("\"state\"");
        json.Should().Contain("\"additions\"");
        json.Should().Contain("\"deletions\"");
        json.Should().Contain("\"changedFiles\"");
        json.Should().Contain("\"reviewDecision\"");
        json.Should().Contain("\"headBranch\"");
        json.Should().Contain("\"baseBranch\"");
        json.Should().Contain("\"commentsCount\"");
    }

    #endregion

    #region B. GitHubRepoMetrics PR Fields

    [Fact]
    public void GitHubRepoMetrics_HasPullRequestProperties()
    {
        var metrics = new GitHubRepoMetrics();

        metrics.RecentPullRequests.Should().NotBeNull().And.BeEmpty();
        metrics.RecentMergedPullRequests.Should().NotBeNull().And.BeEmpty();
        metrics.MergedPullRequestsCount.Should().Be(0);
    }

    [Fact]
    public void GitHubRepoMetrics_PullRequestCount_MatchesList()
    {
        var metrics = new GitHubRepoMetrics
        {
            RecentPullRequests =
            [
                new GitHubPullRequest { Number = 1, Title = "PR 1" },
                new GitHubPullRequest { Number = 2, Title = "PR 2" },
                new GitHubPullRequest { Number = 3, Title = "PR 3" }
            ]
        };

        // After Kaylee's refactoring: OpenPullRequests = RecentPullRequests.Count
        metrics.OpenPullRequests = metrics.RecentPullRequests.Count;
        metrics.OpenPullRequests.Should().Be(3);
    }

    #endregion

    #region C. GitHubCollector PR Collection

    [Fact]
    public async Task CollectAsync_ParsesOpenPullRequests_WithFullDetail()
    {
        var pr1Json = BuildPullRequestJson(
            number: 101, title: "Add new feature", state: "open",
            createdAt: "2024-06-01T10:00:00Z", updatedAt: "2024-06-02T14:00:00Z",
            userLogin: "alice", draft: false,
            labels: ["enhancement", "needs-review"],
            additions: 150, deletions: 30, changedFiles: 12,
            headBranch: "feature/new-thing", baseBranch: "main",
            comments: 4);

        var pr2Json = BuildPullRequestJson(
            number: 102, title: "Fix critical bug", state: "open",
            createdAt: "2024-06-03T08:00:00Z", updatedAt: "2024-06-03T12:00:00Z",
            userLogin: "bob", draft: true,
            labels: ["bug"],
            additions: 5, deletions: 2, changedFiles: 1,
            headBranch: "fix/critical", baseBranch: "develop",
            comments: 0);

        var openPrsJson = $"[{pr1Json},{pr2Json}]";

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: openPrsJson);
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];
        metrics.RecentPullRequests.Should().HaveCount(2);

        var first = metrics.RecentPullRequests[0];
        first.Number.Should().Be(101);
        first.Title.Should().Be("Add new feature");
        first.State.Should().Be("open");
        first.UserLogin.Should().Be("alice");
        first.IsDraft.Should().BeFalse();
        first.Labels.Should().BeEquivalentTo(["enhancement", "needs-review"]);
        // additions/deletions/changed_files not returned by list endpoint — always 0
        first.Additions.Should().Be(0);
        first.Deletions.Should().Be(0);
        first.ChangedFiles.Should().Be(0);
        first.HeadBranch.Should().Be("feature/new-thing");
        first.BaseBranch.Should().Be("main");
        first.CommentsCount.Should().Be(4);
        first.HtmlUrl.Should().Contain("/pull/101");
    }

    [Fact]
    public async Task CollectAsync_ParsesMergedPullRequests_Within30Days()
    {
        var recentMergedAt = DateTimeOffset.UtcNow.AddDays(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var recentClosedAt = recentMergedAt;

        // PR merged recently (within 30 days) — should be included
        var mergedPrJson = BuildPullRequestJson(
            number: 200, title: "Merged recently", state: "closed",
            mergedAt: recentMergedAt, closedAt: recentClosedAt,
            userLogin: "carol");

        // PR closed but NOT merged — should be excluded from merged list
        var closedNotMergedJson = BuildPullRequestJson(
            number: 201, title: "Closed without merge", state: "closed",
            mergedAt: null, closedAt: recentClosedAt,
            userLogin: "dave");

        var closedPrsJson = $"[{mergedPrJson},{closedNotMergedJson}]";

        var handler = CreateHandlerWithStandardEndpoints(closedPrsJson: closedPrsJson);
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];

        // Only the merged PR should be in RecentMergedPullRequests
        metrics.RecentMergedPullRequests.Should().ContainSingle();
        metrics.RecentMergedPullRequests[0].Number.Should().Be(200);
        metrics.RecentMergedPullRequests[0].Title.Should().Be("Merged recently");
        metrics.MergedPullRequestsCount.Should().Be(1);
    }

    [Fact]
    public async Task CollectAsync_FiltersMergedPrOlderThan30Days()
    {
        var oldMergedAt = DateTimeOffset.UtcNow.AddDays(-45).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var oldClosedAt = oldMergedAt;

        // PR merged more than 30 days ago — should be excluded
        var oldMergedPrJson = BuildPullRequestJson(
            number: 300, title: "Old merged PR", state: "closed",
            mergedAt: oldMergedAt, closedAt: oldClosedAt);

        var closedPrsJson = $"[{oldMergedPrJson}]";

        var handler = CreateHandlerWithStandardEndpoints(closedPrsJson: closedPrsJson);
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];

        // Old merged PR should be filtered out
        metrics.RecentMergedPullRequests.Should().BeEmpty();
        metrics.MergedPullRequestsCount.Should().Be(0);
    }

    [Fact]
    public async Task CollectAsync_SkipsPullRequestsFromIssuesEndpoint()
    {
        // Issues endpoint returns a mix: real issue + a pull request entry
        var issuesJson = """
        [
          {
            "number": 10,
            "title": "Real issue",
            "html_url": "https://github.com/owner/repo/issues/10",
            "state": "open",
            "created_at": "2024-06-01T10:00:00Z",
            "updated_at": "2024-06-02T10:00:00Z",
            "user": { "login": "alice" },
            "labels": [],
            "comments": 1
          },
          {
            "number": 11,
            "title": "This is actually a PR",
            "html_url": "https://github.com/owner/repo/pull/11",
            "state": "open",
            "created_at": "2024-06-01T10:00:00Z",
            "updated_at": "2024-06-02T10:00:00Z",
            "user": { "login": "bob" },
            "labels": [],
            "comments": 0,
            "pull_request": {
              "url": "https://api.github.com/repos/owner/repo/pulls/11"
            }
          }
        ]
        """;

        var handler = CreateHandlerWithStandardEndpoints(openIssuesJson: issuesJson);
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];

        // Issues list should only contain the real issue, not the PR
        metrics.RecentIssues.Should().ContainSingle();
        metrics.RecentIssues[0].Number.Should().Be(10);
        metrics.RecentIssues[0].Title.Should().Be("Real issue");
    }

    [Fact]
    public async Task CollectAsync_HandlesEmptyPullRequestList()
    {
        var handler = CreateHandlerWithStandardEndpoints(
            openPrsJson: "[]",
            closedPrsJson: "[]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];

        // Empty lists, not null
        metrics.RecentPullRequests.Should().NotBeNull().And.BeEmpty();
        metrics.RecentMergedPullRequests.Should().NotBeNull().And.BeEmpty();
        metrics.OpenPullRequests.Should().Be(0);
        metrics.MergedPullRequestsCount.Should().Be(0);
    }

    [Fact]
    public async Task CollectAsync_HandlesPrWithNullOptionalFields()
    {
        // PR with minimal data — nulls for optional fields
        var minimalPrJson = """
        {
          "number": 50,
          "title": "Minimal PR",
          "html_url": null,
          "state": "open",
          "created_at": "2024-06-01T10:00:00Z",
          "updated_at": "2024-06-02T10:00:00Z",
          "merged_at": null,
          "closed_at": null,
          "draft": false,
          "user": null,
          "labels": null,
          "head": { "ref": "patch-1" },
          "base": { "ref": "main" },
          "additions": 0,
          "deletions": 0,
          "changed_files": 0,
          "comments": 0
        }
        """;

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{minimalPrJson}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var pr = results[0].RecentPullRequests[0];

        pr.Number.Should().Be(50);
        pr.Title.Should().Be("Minimal PR");
        pr.MergedAt.Should().BeNull();
        pr.ClosedAt.Should().BeNull();
        pr.UserLogin.Should().BeNull();
        pr.Labels.Should().NotBeNull(); // empty list, not null
    }

    [Fact]
    public async Task CollectAsync_DraftPrFlagIsParsedCorrectly()
    {
        var draftPr = BuildPullRequestJson(number: 60, title: "WIP: Draft PR", draft: true);
        var normalPr = BuildPullRequestJson(number: 61, title: "Ready PR", draft: false);

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{draftPr},{normalPr}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var prs = results[0].RecentPullRequests;
        prs.Should().HaveCount(2);
        prs[0].IsDraft.Should().BeTrue();
        prs[1].IsDraft.Should().BeFalse();
    }

    [Fact]
    public async Task CollectAsync_PrLabelsAreParsed()
    {
        var prWithLabels = BuildPullRequestJson(
            number: 70, title: "Labeled PR",
            labels: ["bug", "priority:high", "area:core"]);

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{prWithLabels}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var pr = results[0].RecentPullRequests[0];
        pr.Labels.Should().HaveCount(3);
        pr.Labels.Should().BeEquivalentTo(["bug", "priority:high", "area:core"]);
    }

    [Fact]
    public async Task CollectAsync_OpenPullRequestsCount_MatchesRecentPrsList()
    {
        var pr1 = BuildPullRequestJson(number: 80, title: "PR 1");
        var pr2 = BuildPullRequestJson(number: 81, title: "PR 2");
        var pr3 = BuildPullRequestJson(number: 82, title: "PR 3");

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{pr1},{pr2},{pr3}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var metrics = results[0];

        // After Kaylee's refactoring: OpenPullRequests == RecentPullRequests.Count
        metrics.OpenPullRequests.Should().Be(metrics.RecentPullRequests.Count);
        metrics.OpenPullRequests.Should().Be(3);
    }

    #endregion

    #region D. TrendData PR Activity

    [Fact]
    public void PullRequestActivityPoint_DefaultValues()
    {
        var point = new PullRequestActivityPoint();

        point.Date.Should().BeEmpty();
        point.Opened.Should().Be(0);
        point.Merged.Should().Be(0);
        point.Closed.Should().Be(0);
    }

    [Fact]
    public void PullRequestActivityPoint_Serialization_RoundTrip()
    {
        var original = new PullRequestActivityPoint
        {
            Date = "2024-06-15",
            Opened = 5,
            Merged = 3,
            Closed = 1
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<PullRequestActivityPoint>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Date.Should().Be("2024-06-15");
        deserialized.Opened.Should().Be(5);
        deserialized.Merged.Should().Be(3);
        deserialized.Closed.Should().Be(1);
    }

    [Fact]
    public void TrendData_HasPullRequestActivity_DefaultEmpty()
    {
        var trendData = new TrendData();

        trendData.PullRequestActivity.Should().NotBeNull().And.BeEmpty();
    }

    #endregion

    #region E. Edge Cases

    [Fact]
    public async Task CollectAsync_PrWithNoReviewDecision_DefaultsNull()
    {
        // PR JSON without review_decision field at all
        var prJson = BuildPullRequestJson(
            number: 90, title: "No review", reviewDecision: null);

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{prJson}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var pr = results[0].RecentPullRequests[0];
        pr.ReviewDecision.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_PrBranchInfo_ParsedCorrectly()
    {
        var prJson = BuildPullRequestJson(
            number: 95, title: "Branch test",
            headBranch: "users/alice/experiment",
            baseBranch: "release/v2.0");

        var handler = CreateHandlerWithStandardEndpoints(openPrsJson: $"[{prJson}]");
        using var httpClient = new HttpClient(handler);
        var collector = new GitHubCollector(httpClient);

        var results = await collector.CollectAsync(["owner/repo"]);

        results.Should().ContainSingle();
        var pr = results[0].RecentPullRequests[0];
        pr.HeadBranch.Should().Be("users/alice/experiment");
        pr.BaseBranch.Should().Be("release/v2.0");
    }

    #endregion
}
