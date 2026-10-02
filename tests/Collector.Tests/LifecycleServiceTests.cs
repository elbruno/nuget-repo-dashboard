using System.Text.Json;
using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class LifecycleServiceTests
{
    [Fact]
    public void Apply_MarksExplicitRetirementsCaseInsensitivelyWithoutChangingMetrics()
    {
        var config = JsonSerializer.Deserialize<DashboardConfig>("""
            {"retiredPackages":["PKG.RETIRED"],"retiredRepositories":["OWNER/RETIRED"]}
            """)!;
        List<NuGetPackageMetrics> packages =
        [
            new() { PackageId = "Pkg.Retired", TotalDownloads = 500 },
            new() { PackageId = "Pkg.Active", TotalDownloads = 100 }
        ];
        List<GitHubRepoMetrics> repos =
        [
            new() { FullName = "owner/retired", Stars = 10 },
            new() { FullName = "owner/active", Stars = 20 }
        ];

        LifecycleService.Apply(config, packages, repos);

        packages.Select(p => p.Retired).Should().Equal(true, false);
        repos.Select(r => r.Retired).Should().Equal(true, false);
        packages.Sum(p => p.TotalDownloads).Should().Be(600);
        repos.Sum(r => r.Stars).Should().Be(30);
        JsonSerializer.Serialize(packages[0]).Should().Contain("\"retired\":true");
        JsonSerializer.Serialize(repos[0]).Should().Contain("\"retired\":true");
    }

    [Fact]
    public void Apply_ArchivedRepositoryDoesNotRetirePackage()
    {
        var package = new NuGetPackageMetrics { PackageId = "Pkg.Active", ProjectUrl = "https://github.com/owner/archived" };
        var repo = new GitHubRepoMetrics { FullName = "owner/archived", Archived = true };

        LifecycleService.Apply(new DashboardConfig(), [package], [repo]);

        package.Retired.Should().BeFalse();
        repo.Retired.Should().BeFalse();
        repo.Archived.Should().BeTrue();
    }

    [Fact]
    public void Apply_RemovingOverrideRestoresActiveStatus()
    {
        var package = new NuGetPackageMetrics { PackageId = "Pkg", Retired = true };
        var repo = new GitHubRepoMetrics { FullName = "owner/repo", Retired = true };

        LifecycleService.Apply(new DashboardConfig(), [package], [repo]);

        package.Retired.Should().BeFalse();
        repo.Retired.Should().BeFalse();
    }
}
