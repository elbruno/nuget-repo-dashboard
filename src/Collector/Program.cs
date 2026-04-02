using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

// Resolve repo root (two levels up from src/Collector/)
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

// Allow overriding the repo root via env var for CI
if (Environment.GetEnvironmentVariable("DASHBOARD_REPO_ROOT") is { Length: > 0 } envRoot)
{
    repoRoot = envRoot;
}

var trackedPackagesPath = Path.Combine(repoRoot, "config", "tracked-packages.json");
var dashboardConfigPath = Path.Combine(repoRoot, "config", "dashboard-config.json");

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   NuGet + GitHub Dashboard Collector     ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();

// --- Shared HTTP client (reused for NuGet calls: discovery + metrics) ---
var nugetHandler = new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
};
using var nugetHttpClient = new HttpClient(nugetHandler);
nugetHttpClient.DefaultRequestHeaders.UserAgent.Add(
    new ProductInfoHeaderValue("NuGetDashboardCollector", "1.0"));
nugetHttpClient.DefaultRequestHeaders.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/json"));

// ═══════════════════════════════════════════════════════════════════════
// Pipeline 1: Discovery
// ═══════════════════════════════════════════════════════════════════════
Console.WriteLine("Pipeline 1: Discovery");
Console.WriteLine();

// --- 1. Load dashboard config ---
Console.WriteLine("  [1/3] Loading config...");
DashboardConfig dashboardConfig = new();
if (File.Exists(dashboardConfigPath))
{
    try
    {
        await using var configStream = File.OpenRead(dashboardConfigPath);
        dashboardConfig = await JsonSerializer.DeserializeAsync<DashboardConfig>(configStream) ?? new();
        Console.WriteLine($"    Profile: {dashboardConfig.NuGetProfile ?? "(none)"}");
        Console.WriteLine($"    Merge tracked packages: {dashboardConfig.MergeWithTrackedPackages}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"    WARNING: Failed to load dashboard config: {ex.Message}");
        Console.Error.WriteLine("    Falling back to tracked-packages.json only.");
    }
}
else
{
    Console.WriteLine("    No dashboard-config.json found. Using tracked-packages.json only.");
}

// --- 2. Discover packages from NuGet profile ---
Console.WriteLine("  [2/3] Discovering packages from NuGet profile...");
var discoveredPackages = new List<DiscoveredPackage>();
if (!string.IsNullOrWhiteSpace(dashboardConfig.NuGetProfile))
{
    try
    {
        INuGetProfileDiscoveryService discovery = new NuGetProfileDiscoveryService(nugetHttpClient);
        discoveredPackages = await discovery.DiscoverAsync(dashboardConfig.NuGetProfile);
        Console.WriteLine($"    Discovered {discoveredPackages.Count} package(s) from profile '{dashboardConfig.NuGetProfile}'.");

        var withRepo = discoveredPackages.Count(p => p.GitHubRepo is not null);
        Console.WriteLine($"    Resolved GitHub repos for {withRepo}/{discoveredPackages.Count} packages.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"    WARNING: Profile discovery failed: {ex.Message}");
    }
}
else
{
    Console.WriteLine("    No NuGet profile configured — skipping discovery.");
}

// --- 3. Build package and repository lists ---
Console.WriteLine("  [3/3] Building package and repository lists...");
var packages = new List<PackageConfig>();

// Convert discovered packages to PackageConfig entries
foreach (var dp in discoveredPackages)
{
    var repos = new List<string>();
    if (dp.GitHubRepo is not null)
    {
        repos.Add(dp.GitHubRepo);
    }

    packages.Add(new PackageConfig
    {
        PackageId = dp.PackageId,
        Repos = repos,
    });
}

// Merge manually tracked packages
if (dashboardConfig.MergeWithTrackedPackages && File.Exists(trackedPackagesPath))
{
    try
    {
        IConfigLoader configLoader = new ConfigLoader();
        var tracked = await configLoader.LoadAsync(trackedPackagesPath);

        // Add tracked packages that weren't already discovered
        var existingIds = new HashSet<string>(
            packages.Select(p => p.PackageId),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var tp in tracked)
        {
            if (!existingIds.Contains(tp.PackageId))
            {
                packages.Add(tp);
                added++;
            }
        }

        if (added > 0)
        {
            Console.WriteLine($"    Merged {added} additional package(s) from tracked-packages.json.");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"    WARNING: Failed to load tracked-packages.json: {ex.Message}");
    }
}

// Filter out ignored packages
if (dashboardConfig.IgnorePackages.Count > 0)
{
    var ignoreSet = new HashSet<string>(dashboardConfig.IgnorePackages, StringComparer.OrdinalIgnoreCase);
    int beforeCount = packages.Count;
    packages.RemoveAll(p => ignoreSet.Contains(p.PackageId));
    int filtered = beforeCount - packages.Count;
    if (filtered > 0)
    {
        Console.WriteLine($"    Filtered out {filtered} ignored package(s).");
    }
}

if (packages.Count == 0)
{
    Console.Error.WriteLine("    FATAL: No packages found from discovery or tracked config.");
    return 1;
}

// Deduplicate repos across all packages
var allRepos = packages
    .SelectMany(p => p.Repos)
    .Where(r => !string.IsNullOrWhiteSpace(r))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"    → {packages.Count} unique packages (after filtering)");
Console.WriteLine($"    → {allRepos.Count} unique GitHub repositories");

Console.WriteLine();
Console.WriteLine("Pipeline 2: Collection");
Console.WriteLine();

// --- 1. Collect NuGet metrics ---
Console.WriteLine("  [1/2] Collecting NuGet package metrics...");

INuGetCollector nugetCollector = new NuGetCollector(nugetHttpClient);
var nugetMetrics = await nugetCollector.CollectAsync(packages);
Console.WriteLine($"    → Collected {nugetMetrics.Count} packages");

// --- 2. Collect GitHub metrics ---
Console.WriteLine("  [2/2] Collecting GitHub repository metrics...");
using var githubHttpClient = new HttpClient();
githubHttpClient.DefaultRequestHeaders.UserAgent.Add(
    new ProductInfoHeaderValue("NuGetDashboardCollector", "1.0"));
githubHttpClient.DefaultRequestHeaders.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
if (!string.IsNullOrEmpty(token))
{
    githubHttpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);
}

IGitHubCollector githubCollector = new GitHubCollector(githubHttpClient);
var githubMetrics = await githubCollector.CollectAsync(allRepos);
Console.WriteLine($"    → Collected {githubMetrics.Count} repos");

// --- Write output ---
var generatedAt = DateTimeOffset.UtcNow;

var nugetOutput = new NuGetOutput
{
    GeneratedAt = generatedAt,
    Packages = nugetMetrics
};

var reposOutput = new RepositoriesOutput
{
    GeneratedAt = generatedAt,
    Repositories = githubMetrics
};

IJsonOutputWriter writer = new JsonOutputWriter();
await writer.WriteNuGetAsync(nugetOutput, repoRoot);
Console.WriteLine($"    → data.nuget.json");
await writer.WriteRepositoriesAsync(reposOutput, repoRoot);
Console.WriteLine($"    → data.repositories.json");

// --- Summary ---
Console.WriteLine();
Console.WriteLine("═══ Summary ═══════════════════════════════");
Console.WriteLine($"  Generated at : {generatedAt:O}");
Console.WriteLine($"  Packages     : {nugetOutput.Packages.Count}");
Console.WriteLine($"  Repos        : {reposOutput.Repositories.Count}");

if (nugetOutput.Packages.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("  NuGet Packages:");
    foreach (var pkg in nugetOutput.Packages)
    {
        Console.WriteLine($"    • {pkg.PackageId} v{pkg.LatestVersion} — {pkg.TotalDownloads:N0} downloads");
    }
}

if (reposOutput.Repositories.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("  GitHub Repos:");
    foreach (var repo in reposOutput.Repositories)
    {
        Console.WriteLine($"    • {repo.FullName} — ★ {repo.Stars:N0} | Forks: {repo.Forks:N0} | Issues: {repo.OpenIssues} | PRs: {repo.OpenPullRequests}");
    }
}

Console.WriteLine();
Console.WriteLine("Done.");
return 0;
