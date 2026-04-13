using System.Net;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class NuGetCollectorTests
{
    private static string BuildRegistrationJson(
        string version = "1.2.3",
        string description = "A test package",
        string authors = "Test Author",
        string? projectUrl = "https://example.com",
        bool listed = true,
        string? publishedDate = "2024-01-15T00:00:00+00:00",
        string[]? tags = null)
    {
        tags ??= ["tag1", "tag2"];
        var tagsJson = string.Join(",", tags.Select(t => $"\"{t}\""));
        var projectUrlJson = projectUrl is null ? "null" : $"\"{projectUrl}\"";
        var publishedJson = publishedDate is null ? "" : $"\"published\": \"{publishedDate}\",";

        return $$"""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "{{version}}",
                    "description": "{{description}}",
                    "authors": "{{authors}}",
                    "projectUrl": {{projectUrlJson}},
                    "listed": {{listed.ToString().ToLower()}},
                    {{publishedJson}}
                    "tags": [{{tagsJson}}]
                  }
                }
              ]
            }
          ]
        }
        """;
    }

    private static string BuildSearchJson(long totalDownloads)
    {
        return $$"""
        {
          "data": [
            {
              "totalDownloads": {{totalDownloads}}
            }
          ]
        }
        """;
    }

    [Fact]
    public async Task CollectAsync_KnownPackage_ReturnsCorrectMetrics()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/testpkg/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(version: "2.0.0", description: "My package", authors: "Bruno"));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:TestPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(42000));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:TestPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(42000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "TestPkg", Repos = ["owner/repo"] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        var m = results[0];
        m.PackageId.Should().Be("TestPkg");
        m.LatestVersion.Should().Be("2.0.0");
        m.Description.Should().Be("My package");
        m.Authors.Should().Be("Bruno");
        m.TotalDownloads.Should().Be(42000);
        m.Listed.Should().BeTrue();
        m.Tags.Should().Contain("tag1");
    }

    [Fact]
    public async Task CollectAsync_PackageNotFound404_ReturnsEmptyList()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "NonExistentPackage", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_RateLimited429_RetriesAndHandles()
    {
        int callCount = 0;
        var handler = new MockHttpMessageHandler();
        handler.SetCustomHandler(request =>
        {
            callCount++;
            if (callCount <= 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50));
                return Task.FromResult(response);
            }

            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("registration5-gz-semver2"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildRegistrationJson(), System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildSearchJson(100), System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "RateLimitedPkg", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        // It should have retried (callCount > 1)
        callCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task CollectAsync_EmptyResponse_ReturnsZeroDownloads()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/emptypkg/index.json",
            HttpStatusCode.OK,
            """{ "items": [] }""");
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:EmptyPkg&take=1",
            HttpStatusCode.OK,
            """{ "data": [] }""");
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:EmptyPkg&take=1",
            HttpStatusCode.OK,
            """{ "data": [] }""");

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "EmptyPkg", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(0);
        results[0].LatestVersion.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_NetworkError_HandlesGracefully()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetCustomHandler(_ => throw new HttpRequestException("Network error simulation"));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "FailPkg", Repos = [] }
        };

        // Should not throw — the collector catches exceptions per package
        var results = await collector.CollectAsync(packages);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_MultiplePackages_ReturnsAllSuccessful()
    {
        var handler = new MockHttpMessageHandler();

        // First package succeeds
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/pkg1/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(version: "1.0.0"));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:Pkg1&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(500));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:Pkg1&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(500));

        // Second package 404
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/pkg2/index.json",
            HttpStatusCode.NotFound, "");

        // Third package succeeds
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/pkg3/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(version: "3.0.0"));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:Pkg3&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(9999));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:Pkg3&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(9999));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "Pkg1", Repos = [] },
            new() { PackageId = "Pkg2", Repos = [] },
            new() { PackageId = "Pkg3", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().HaveCount(2);
        results[0].PackageId.Should().Be("Pkg1");
        results[1].PackageId.Should().Be("Pkg3");
    }

    [Fact]
    public async Task CollectAsync_UnlistedPackage_ReturnsListedFalse()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/unlistedpkg/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(listed: false));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:UnlistedPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(10));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:UnlistedPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(10));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "UnlistedPkg", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].Listed.Should().BeFalse();
    }

    [Fact]
    public async Task CollectAsync_EmptyPackageList_ReturnsEmptyResults()
    {
        var handler = new MockHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectAsync_PackageWithTags_ParsesTagsCorrectly()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/tagpkg/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(tags: ["ai", "machine-learning", "dotnet"]));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:TagPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(0));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:TagPkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(0));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "TagPkg", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].Tags.Should().BeEquivalentTo(["ai", "machine-learning", "dotnet"]);
    }

    [Fact]
    public async Task CollectAsync_ParsesPublishedDate()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse(
            "https://api.nuget.org/v3/registration5-gz-semver2/datepkg/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson(publishedDate: "2024-06-15T12:00:00+00:00"));
        handler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=packageid:DatePkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(0));
        handler.AddResponse(
            "https://azuresearch-ussc.nuget.org/query?q=packageid:DatePkg&take=1",
            HttpStatusCode.OK,
            BuildSearchJson(0));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = "DatePkg", Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].PublishedDate.Should().NotBeNull();
        results[0].PublishedDate!.Value.Year.Should().Be(2024);
    }
}
