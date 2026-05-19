using System.Net;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class MaintainabilityScoreServiceTests
{
    [Fact]
    public async Task ApplyAsync_PopulatesMaintainabilityScoreAndBreakdown()
    {
        using var httpClient = new HttpClient(new MaintainabilityMockHandler());
        var service = new MaintainabilityScoreService(httpClient);

        var repo = new GitHubRepoMetrics
        {
            Owner = "owner",
            Name = "repo",
            FullName = "owner/repo",
            RecentClosedIssues =
            [
                new GitHubIssue
                {
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-6),
                    ClosedAt = DateTimeOffset.UtcNow.AddDays(-2)
                }
            ]
        };

        List<PackageConfig> packages =
        [
            new PackageConfig
            {
                PackageId = "Pkg.Core",
                Repos = ["owner/repo"]
            }
        ];

        List<NuGetPackageMetrics> packageMetrics =
        [
            new NuGetPackageMetrics
            {
                PackageId = "Pkg.Core",
                ReleasesLast30Days = 2
            }
        ];

        await service.ApplyAsync([repo], packages, packageMetrics);

        repo.Maintainability.Should().NotBeNull();
        repo.Maintainability!.TotalScore.Should().BeGreaterThan(0);
        repo.Maintainability.Activity.Available.Should().BeTrue();
        repo.Maintainability.Activity.Value.Should().Be(12);
        repo.Maintainability.ReleaseFrequency.Value.Should().Be(2);
        repo.Maintainability.TestCoverage.Value.Should().Be(82);
        repo.Maintainability.Documentation.Score.Should().Be(20);
    }

    [Fact]
    public void BuildScore_NormalizesOnlyAvailableMetrics()
    {
        var repo = new GitHubRepoMetrics
        {
            Owner = "owner",
            Name = "repo",
            FullName = "owner/repo"
        };

        var signals = new MaintainabilityScoreService.RepositoryHealthSignals
        {
            RecentCommitsLast30Days = 10,
            ReadmeKnown = true,
            HasReadme = true,
            DocsKnown = true,
            HasDocsDirectory = true,
            CoveragePercent = null
        };

        var score = MaintainabilityScoreService.BuildScore(repo, signals, null);

        score.TotalScore.Should().Be(75);
        score.Activity.Score.Should().Be(10);
        score.Documentation.Score.Should().Be(20);
        score.TestCoverage.Available.Should().BeFalse();
        score.ReleaseFrequency.Available.Should().BeFalse();
    }

    [Fact]
    public void TryExtractCoveragePercent_ParsesCoverageBadgeFromReadme()
    {
        const string readme = """
        # Demo

        ![Coverage](https://img.shields.io/badge/coverage-88%25-brightgreen)
        """;

        var coverage = MaintainabilityScoreService.TryExtractCoveragePercent(readme);

        coverage.Should().Be(88);
    }

    private sealed class MaintainabilityMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/commits?", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[" + string.Join(",", Enumerable.Range(1, 12).Select(i => $$"""{ "sha": "{{i}}" }""")) + "]",
                        System.Text.Encoding.UTF8,
                        "application/json")
                });
            }

            if (url.EndsWith("/readme", StringComparison.Ordinal))
            {
                var content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("""
                # Repo

                ![Coverage](https://img.shields.io/badge/coverage-82%25-brightgreen)
                """));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    {
                      "encoding": "base64",
                      "content": "{{content}}"
                    }
                    """, System.Text.Encoding.UTF8, "application/json")
                });
            }

            if (url.EndsWith("/contents/docs", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{ "name": "index.md", "type": "file" }]""", System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
