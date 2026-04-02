using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface INuGetProfileDiscoveryService
{
    /// <summary>
    /// Discovers all NuGet packages owned by the given profile username.
    /// Returns discovered packages with metadata and resolved GitHub repos.
    /// </summary>
    Task<List<DiscoveredPackage>> DiscoverAsync(string username);
}

public sealed partial class NuGetProfileDiscoveryService : INuGetProfileDiscoveryService
{
    private readonly HttpClient _httpClient;
    private const string SearchBaseUrl = "https://azuresearch-usnc.nuget.org/query";
    private const int PageSize = 100;
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public NuGetProfileDiscoveryService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<DiscoveredPackage>> DiscoverAsync(string username)
    {
        var packages = new List<DiscoveredPackage>();
        int skip = 0;
        int totalHits;

        do
        {
            var url = $"{SearchBaseUrl}?q=owner:{Uri.EscapeDataString(username)}&take={PageSize}&skip={skip}";
            var json = await GetWithRetryAsync(url);
            if (json is null) break;

            var root = json.RootElement;

            totalHits = root.TryGetProperty("totalHits", out var hitsEl) ? hitsEl.GetInt32() : 0;

            if (root.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var packageId = item.GetStringOrDefault("id", string.Empty);
                    if (string.IsNullOrWhiteSpace(packageId)) continue;

                    var projectUrl = item.GetStringOrNull("projectUrl");
                    var gitHubRepo = ParseGitHubRepo(projectUrl);

                    packages.Add(new DiscoveredPackage
                    {
                        PackageId = packageId,
                        LatestVersion = item.GetStringOrDefault("version", string.Empty),
                        Description = item.GetStringOrDefault("description", string.Empty),
                        TotalDownloads = item.TryGetProperty("totalDownloads", out var dl) ? dl.GetInt64() : 0,
                        ProjectUrl = projectUrl,
                        GitHubRepo = gitHubRepo,
                    });
                }
            }

            json.Dispose();
            skip += PageSize;

        } while (skip < totalHits);

        return packages;
    }

    /// <summary>
    /// Extracts "owner/repo" from a GitHub URL.
    /// Supports: https://github.com/{owner}/{repo}, with optional trailing path segments.
    /// </summary>
    internal static string? ParseGitHubRepo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var match = GitHubUrlPattern().Match(url);
        if (!match.Success) return null;

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;

        // Strip .git suffix if present
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        return $"{owner}/{repo}";
    }

    [GeneratedRegex(@"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrlPattern();

    private async Task<JsonDocument?> GetWithRetryAsync(string url)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.AcceptEncoding.ParseAdd("gzip");

                using var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.Error.WriteLine($"[Discovery] 404 for {url}");
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? RetryDelay;
                    Console.Error.WriteLine($"[Discovery] Rate limited. Waiting {retryAfter.TotalSeconds}s...");
                    await Task.Delay(retryAfter);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync();
                return await JsonDocument.ParseAsync(stream);
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                Console.Error.WriteLine($"[Discovery] Attempt {attempt + 1} failed for {url}. Retrying...");
                await Task.Delay(RetryDelay * (attempt + 1));
            }
        }

        return null;
    }
}

file static class JsonElementExtensions
{
    public static string GetStringOrDefault(this JsonElement element, string name, string defaultValue)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }

    public static string? GetStringOrNull(this JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetString() : null;
    }
}
