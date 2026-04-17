using System.Net;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

/// <summary>
/// Tests for NuGetCollector multi-shard query behavior.
/// Validates Math.Max strategy: both shards are queried and the higher value wins.
/// Download counts are monotonically increasing, so the max is always the freshest.
/// </summary>
public class NuGetCollectorMultiShardTests
{
    private const string ShardUSNC = "https://azuresearch-usnc.nuget.org/query?q=packageid:{0}&take=1";
    private const string ShardUSSC = "https://azuresearch-ussc.nuget.org/query?q=packageid:{0}&take=1";

    private static string BuildRegistrationJson()
    {
        return """
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "description": "Test package",
                    "authors": "Test Author",
                    "projectUrl": "https://example.com",
                    "listed": true,
                    "published": "2024-01-01T00:00:00+00:00",
                    "tags": ["test"]
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
    public async Task GetTotalDownloadsAsync_BothShardsReturnData_ReturnsMax()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "TestPkg";

        // Registration endpoint
        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns 50,000
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(50000));

        // USSC returns 75,000
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(75000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(75000, "should return max of both shards (USSC has fresher data)");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USNCHigher_ReturnsUSNCValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "HigherUSNC";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns 100,000 (higher)
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(100000));

        // USSC returns 60,000 (lower)
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(60000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(100000, "should return max — USNC is higher");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USSCHigher_ReturnsUSSCValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "HigherUSSC";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns 45,000 (lower)
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(45000));

        // USSC returns 90,000 (higher)
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(90000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(90000, "should return max — USSC is higher (fresher data)");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USNCFails_ReturnsUSSCValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "USNCFails";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC fails (500 error)
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.InternalServerError,
            "");

        // USSC succeeds with 55,000
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(55000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(55000, "should fallback to USSC when USNC fails");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USSCFails_ReturnsUSNCValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "USSCFails";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC succeeds with 65,000
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(65000));

        // USSC fails (404)
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.NotFound,
            "");

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(65000, "USSC failed; max of USNC(65000) and 0");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_BothShardsFail_ReturnsZero()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "BothFail";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // Both shards fail
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.InternalServerError,
            "");

        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.ServiceUnavailable,
            "");

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(0, "should return 0 when both shards fail");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_ShardsReturnSameValue_ReturnsThatValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "SameValue";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // Both shards return identical count
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(42000));

        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(42000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(42000, "should return same value when shards match");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_OneShardZeroOtherPositive_ReturnsPositiveValue()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "OneZero";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns 0
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(0));

        // USSC returns 12,000 (positive)
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(12000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(12000, "should return max — USSC has positive count while USNC returned 0");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USNCReturnsEmptyDataArray_FallsBackToUSSC()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "EmptyUSNC";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns empty data array (effectively 0)
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            """{ "data": [] }""");

        // USSC returns valid count
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(8000));

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(8000, "should return max — USSC has data while USNC returned empty");
    }

    [Fact]
    public async Task GetTotalDownloadsAsync_USSCReturnsEmptyDataArray_FallsBackToUSNC()
    {
        var handler = new MockHttpMessageHandler();
        var packageId = "EmptyUSSC";

        handler.AddResponse(
            $"https://api.nuget.org/v3/registration5-gz-semver2/{packageId.ToLowerInvariant()}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationJson());

        // USNC returns valid count
        handler.AddResponse(
            string.Format(ShardUSNC, packageId),
            HttpStatusCode.OK,
            BuildSearchJson(15000));

        // USSC returns empty data array (effectively 0)
        handler.AddResponse(
            string.Format(ShardUSSC, packageId),
            HttpStatusCode.OK,
            """{ "data": [] }""");

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var packages = new List<PackageConfig>
        {
            new() { PackageId = packageId, Repos = [] }
        };

        var results = await collector.CollectAsync(packages);

        results.Should().ContainSingle();
        results[0].TotalDownloads.Should().Be(15000, "should use USNC value when USSC returns empty data");
    }
}
