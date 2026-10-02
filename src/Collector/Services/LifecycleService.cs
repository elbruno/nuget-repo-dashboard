using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public static class LifecycleService
{
    public static void Apply(
        DashboardConfig config,
        List<NuGetPackageMetrics> packages,
        List<GitHubRepoMetrics> repositories)
    {
        var retiredPackages = new HashSet<string>(config.RetiredPackages, StringComparer.OrdinalIgnoreCase);
        var retiredRepositories = new HashSet<string>(config.RetiredRepositories, StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            package.Retired = retiredPackages.Contains(package.PackageId);
        }

        foreach (var repository in repositories)
        {
            repository.Retired = retiredRepositories.Contains(repository.FullName);
        }
    }
}
