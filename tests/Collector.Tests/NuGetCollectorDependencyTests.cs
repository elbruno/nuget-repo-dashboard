using System.Net;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

/// <summary>
/// Deterministic tests for NuGetCollector.ExtractDependencyMetricsAsync behavior.
/// Covers valid dependency JSON, malformed shapes (string/object/missing), and
/// mixed valid/invalid dependency groups.
/// </summary>
public class NuGetCollectorDependencyTests
{
    private const string RegistrationBase = "https://api.nuget.org/v3/registration5-gz-semver2";

    // ─── JSON builders ───────────────────────────────────────────────────────

    private static string BuildRegistrationIndexJson(string version = "1.0.0") => $$"""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "{{version}}",
                    "description": "Dependency test package",
                    "authors": "Test Author",
                    "projectUrl": null,
                    "listed": true,
                    "published": "2024-01-01T00:00:00+00:00",
                    "tags": []
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string BuildSearchJson(long downloads = 0) => $$"""
        { "data": [{ "totalDownloads": {{downloads}} }] }
        """;

    /// <summary>
    /// Builds a valid version leaf JSON with the given dependencyGroups array content.
    /// </summary>
    private static string BuildVersionLeafJson(string dependencyGroupsJson) => $$"""
        {
          "catalogEntry": {
            "version": "1.0.0",
            "dependencyGroups": {{dependencyGroupsJson}}
          }
        }
        """;

    // ─── Test: valid dependency JSON ─────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_ValidDependencyJson_ParsesDependenciesCorrectly()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "DepPkg";
        const string pkgLow = "deppkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "dependencies": [
                  { "id": "Newtonsoft.Json", "range": "*" },
                  { "id": "Serilog",         "range": "*" }
                ]
              }
            ]
            """));

        // Downloads shards
        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        var deps = results[0].Dependencies;
        deps.Should().NotBeNull();
        deps!.DirectCount.Should().Be(2);
        deps.Dependencies.Should().HaveCount(2);
        deps.Dependencies.Select(d => d.Id).Should().BeEquivalentTo(["Newtonsoft.Json", "Serilog"]);
        // "*" range means IsLatest == true for all
        deps.Dependencies.Should().AllSatisfy(d => d.IsLatest.Should().BeTrue());
        deps.OutdatedCount.Should().Be(0);
        deps.FreshnessPercent.Should().Be(100m);
    }

    [Fact]
    public async Task CollectAsync_ValidDependencyJson_OutdatedRange_ReturnsIsLatestFalse()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "OutdatedPkg";
        const string pkgLow = "outdatedpkg";
        const string version = "2.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Use a pinned version range — parser treats "[1.0.0, )" as outdated
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "dependencies": [
                  { "id": "OldDep", "range": "[1.0.0, 2.0.0)" }
                ]
              }
            ]
            """));

        // Dependency lookup for "OldDep" returns 404 (IsLatest fallback branch: false for "[" range)
        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        var deps = results[0].Dependencies;
        deps.Should().NotBeNull();
        deps!.DirectCount.Should().Be(1);
        deps.Dependencies[0].Id.Should().Be("OldDep");
        deps.Dependencies[0].IsLatest.Should().BeFalse();
        deps.OutdatedCount.Should().Be(1);
        deps.FreshnessPercent.Should().Be(0m);
    }

    // ─── Test: malformed dependencyGroups — string instead of array ──────────

    [Fact]
    public async Task CollectAsync_MalformedDependencyGroups_StringShape_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "MalformedStringPkg";
        const string pkgLow = "malformedstringpkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // dependencyGroups is a plain string — not an array
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            """
            {
              "catalogEntry": {
                "version": "1.0.0",
                "dependencyGroups": "not-an-array"
              }
            }
            """);

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        // Malformed JSON shape should not crash the collector
        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    // ─── Test: malformed dependency entry — object without "id" ─────────────

    [Fact]
    public async Task CollectAsync_MalformedDependencyEntry_MissingIdField_SkipsEntry()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "NoIdDepPkg";
        const string pkgLow = "noiddeppkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Dependency objects are present but none have an "id" field
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "dependencies": [
                  { "name": "SomePackage", "version": "1.0.0" },
                  { "range": "2.0.0" }
                ]
              }
            ]
            """));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        // All entries skipped because they lack "id" — returns null metrics
        results[0].Dependencies.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_MalformedDependencyEntry_EmptyIdField_SkipsEntry()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "EmptyIdDepPkg";
        const string pkgLow = "emptyiddeppkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Dependency objects have "id" but with empty string value
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "dependencies": [
                  { "id": "", "range": "1.0.0" },
                  { "id": "", "range": "2.0.0" }
                ]
              }
            ]
            """));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    // ─── Test: missing catalogEntry or dependencyGroups ──────────────────────

    [Fact]
    public async Task CollectAsync_MissingCatalogEntry_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "NoCatalogPkg";
        const string pkgLow = "nocatalogpkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Version leaf has no "catalogEntry" property at all
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            """{ "version": "1.0.0", "listed": true }""");

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_MissingDependencyGroups_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "NoDepsGroupPkg";
        const string pkgLow = "nodepsgrouppkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // catalogEntry exists but has no "dependencyGroups" property
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            """
            {
              "catalogEntry": {
                "version": "1.0.0",
                "description": "No deps"
              }
            }
            """);

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_EmptyDependencyGroupsArray_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "EmptyGroupsPkg";
        const string pkgLow = "emptygroupspkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("[]"));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    [Fact]
    public async Task CollectAsync_DependencyGroupWithNoDependenciesProperty_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "NoDepsPropPkg";
        const string pkgLow = "nodepsproppkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Group object exists but no "dependencies" property inside it
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""[{ "targetFramework": ".NETStandard2.0" }]"""));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    // ─── Test: version endpoint 404 ──────────────────────────────────────────

    [Fact]
    public async Task CollectAsync_VersionLeaf404_ReturnsNullDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "Missing404Pkg";
        const string pkgLow = "missing404pkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // No response added for the version leaf — MockHttpMessageHandler returns 404

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        results[0].Dependencies.Should().BeNull();
    }

    // ─── Test: mixed valid/invalid dependency groups ─────────────────────────

    [Fact]
    public async Task CollectAsync_MixedDependencyGroups_ValidAndMissingIds_CountsOnlyValidEntries()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "MixedDepsPkg";
        const string pkgLow = "mixeddepspkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // Three groups: first valid, second has no "dependencies" property, third has two deps with
        // one missing id and one valid id
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "targetFramework": ".NETStandard2.0",
                "dependencies": [
                  { "id": "ValidDep1", "range": "*" }
                ]
              },
              {
                "targetFramework": ".NETCoreApp3.1"
              },
              {
                "targetFramework": "net6.0",
                "dependencies": [
                  { "range": "1.0.0" },
                  { "id": "ValidDep2", "range": "*" }
                ]
              }
            ]
            """));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        var deps = results[0].Dependencies;
        deps.Should().NotBeNull();
        deps!.DirectCount.Should().Be(2);
        deps.Dependencies.Select(d => d.Id).Should().BeEquivalentTo(["ValidDep1", "ValidDep2"]);
        deps.Dependencies.Should().AllSatisfy(d => d.IsLatest.Should().BeTrue());
        deps.OutdatedCount.Should().Be(0);
        deps.FreshnessPercent.Should().Be(100m);
    }

    [Fact]
    public async Task CollectAsync_MixedDependencyGroups_SomeOutdated_CalculatesFreshnessCorrectly()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        const string pkg = "FreshnessPkg";
        const string pkgLow = "freshnesspkg";
        const string version = "1.0.0";

        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/index.json",
            HttpStatusCode.OK,
            BuildRegistrationIndexJson(version));

        // 2 fresh ("*"), 1 outdated ("[1.0.0, 2.0.0)") => 66.67%
        handler.AddResponse(
            $"{RegistrationBase}/{pkgLow}/{version}.json",
            HttpStatusCode.OK,
            BuildVersionLeafJson("""
            [
              {
                "dependencies": [
                  { "id": "FreshDep1",   "range": "*" },
                  { "id": "FreshDep2",   "range": "*" },
                  { "id": "OutdatedDep", "range": "[1.0.0, 2.0.0)" }
                ]
              }
            ]
            """));

        handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());
        handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{pkg}&take=1", HttpStatusCode.OK, BuildSearchJson());

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([new PackageConfig { PackageId = pkg, Repos = [] }]);

        results.Should().ContainSingle();
        var deps = results[0].Dependencies;
        deps.Should().NotBeNull();
        deps!.DirectCount.Should().Be(3);
        deps.OutdatedCount.Should().Be(1);
        deps.FreshnessPercent.Should().Be(Math.Round(2m / 3m * 100m, 2));
    }

    [Fact]
    public async Task CollectAsync_MultiplePackages_DependencyIsolation_EachPackageHasCorrectDependencies()
    {
        var handler = new MockHttpMessageHandler();
        handler.SetDefaultResponse(HttpStatusCode.NotFound);

        // Package A has 1 dependency; Package B has none (missing dependencyGroups)
        foreach (var (id, low, version, versionLeafJson) in new[]
        {
            ("PkgA", "pkga", "1.0.0", BuildVersionLeafJson("""
                [{ "dependencies": [{ "id": "SharedLib", "range": "*" }] }]
                """)),
            ("PkgB", "pkgb", "2.0.0", """{ "catalogEntry": { "version": "2.0.0" } }""")
        })
        {
            handler.AddResponse($"{RegistrationBase}/{low}/index.json", HttpStatusCode.OK, BuildRegistrationIndexJson(version));
            handler.AddResponse($"{RegistrationBase}/{low}/{version}.json", HttpStatusCode.OK, versionLeafJson);
            handler.AddResponse($"https://azuresearch-usnc.nuget.org/query?q=packageid:{id}&take=1", HttpStatusCode.OK, BuildSearchJson());
            handler.AddResponse($"https://azuresearch-ussc.nuget.org/query?q=packageid:{id}&take=1", HttpStatusCode.OK, BuildSearchJson());
        }

        using var httpClient = new HttpClient(handler);
        var collector = new NuGetCollector(httpClient);

        var results = await collector.CollectAsync([
            new PackageConfig { PackageId = "PkgA", Repos = [] },
            new PackageConfig { PackageId = "PkgB", Repos = [] }
        ]);

        results.Should().HaveCount(2);

        var a = results.Single(r => r.PackageId == "PkgA");
        a.Dependencies.Should().NotBeNull();
        a.Dependencies!.DirectCount.Should().Be(1);
        a.Dependencies.Dependencies[0].Id.Should().Be("SharedLib");

        var b = results.Single(r => r.PackageId == "PkgB");
        b.Dependencies.Should().BeNull();
    }
}
