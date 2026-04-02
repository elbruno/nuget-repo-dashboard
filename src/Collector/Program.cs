using System.Net.Http.Headers;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

// Resolve repo root (two levels up from src/Collector/)
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var configPath = Path.Combine(repoRoot, "config", "tracked-packages.json");

// Allow overriding the repo root via env var for CI
if (Environment.GetEnvironmentVariable("DASHBOARD_REPO_ROOT") is { Length: > 0 } envRoot)
{
    repoRoot = envRoot;
    configPath = Path.Combine(repoRoot, "config", "tracked-packages.json");
}

Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   NuGet + GitHub Dashboard Collector     ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();

// --- 1. Load config ---
Console.WriteLine("[1/5] Loading config...");
IConfigLoader configLoader = new ConfigLoader();
List<PackageConfig> packages;
try
{
    packages = await configLoader.LoadAsync(configPath);
    Console.WriteLine($"  Loaded {packages.Count} package(s) from config.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"  FATAL: {ex.Message}");
    return 1;
}

// --- 2. Collect NuGet metrics ---
Console.WriteLine("[2/5] Collecting NuGet metrics...");
using var nugetHttpClient = new HttpClient();
nugetHttpClient.DefaultRequestHeaders.UserAgent.Add(
    new ProductInfoHeaderValue("NuGetDashboardCollector", "1.0"));
nugetHttpClient.DefaultRequestHeaders.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/json"));

INuGetCollector nugetCollector = new NuGetCollector(nugetHttpClient);
var nugetMetrics = await nugetCollector.CollectAsync(packages);
Console.WriteLine($"  Collected metrics for {nugetMetrics.Count} package(s).");

// --- 3. Collect GitHub metrics ---
Console.WriteLine("[3/5] Collecting GitHub metrics...");
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
    Console.WriteLine("  Using GITHUB_TOKEN for authentication.");
}
else
{
    Console.WriteLine("  No GITHUB_TOKEN set — using unauthenticated requests (lower rate limits).");
}

// Deduplicate repos across all packages
var allRepos = packages
    .SelectMany(p => p.Repos)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

IGitHubCollector githubCollector = new GitHubCollector(githubHttpClient);
var githubMetrics = await githubCollector.CollectAsync(allRepos);
Console.WriteLine($"  Collected metrics for {githubMetrics.Count} repo(s).");

// --- 4. Build output ---
Console.WriteLine("[4/5] Building dashboard output...");
var output = new DashboardOutput
{
    GeneratedAt = DateTimeOffset.UtcNow,
    Packages = nugetMetrics,
    Repos = githubMetrics
};

// --- 5. Write JSON files ---
Console.WriteLine("[5/5] Writing output files...");
IJsonOutputWriter writer = new JsonOutputWriter();
await writer.WriteAsync(output, repoRoot);

// --- Summary ---
Console.WriteLine();
Console.WriteLine("═══ Summary ═══════════════════════════════");
Console.WriteLine($"  Generated at : {output.GeneratedAt:O}");
Console.WriteLine($"  Packages     : {output.Packages.Count}");
Console.WriteLine($"  Repos        : {output.Repos.Count}");

if (output.Packages.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("  NuGet Packages:");
    foreach (var pkg in output.Packages)
    {
        Console.WriteLine($"    • {pkg.PackageId} v{pkg.LatestVersion} — {pkg.TotalDownloads:N0} downloads");
    }
}

if (output.Repos.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("  GitHub Repos:");
    foreach (var repo in output.Repos)
    {
        Console.WriteLine($"    • {repo.FullName} — ★ {repo.Stars:N0} | Forks: {repo.Forks:N0} | Issues: {repo.OpenIssues} | PRs: {repo.OpenPullRequests}");
    }
}

Console.WriteLine();
Console.WriteLine("Done.");
return 0;
