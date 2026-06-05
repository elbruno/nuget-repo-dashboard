using System.Net;
using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface INuGetCollector
{
    Task<List<NuGetPackageMetrics>> CollectAsync(List<PackageConfig> packages);
}

public sealed class NuGetCollector : INuGetCollector
{
    private readonly HttpClient _httpClient;
    private const string RegistrationBaseUrl = "https://api.nuget.org/v3/registration5-gz-semver2";
    private const string SearchShardUsnc = "https://azuresearch-usnc.nuget.org/query";
    private const string SearchShardUssc = "https://azuresearch-ussc.nuget.org/query";
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public NuGetCollector(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<NuGetPackageMetrics>> CollectAsync(List<PackageConfig> packages)
    {
        var results = new List<NuGetPackageMetrics>();

        foreach (var pkg in packages)
        {
            try
            {
                var metrics = await CollectPackageAsync(pkg.PackageId);
                if (metrics is not null)
                {
                    results.Add(metrics);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[NuGet] Failed to collect '{pkg.PackageId}': {ex.Message}");
            }
        }

        return results;
    }

    private async Task<NuGetPackageMetrics?> CollectPackageAsync(string packageId)
    {
        var url = $"{RegistrationBaseUrl}/{packageId.ToLowerInvariant()}/index.json";

        var json = await GetWithRetryAsync(url);
        if (json is null) return null;

        var root = json.RootElement;

        long totalDownloads = 0;
        string latestVersion = string.Empty;
        string description = string.Empty;
        string authors = string.Empty;
        string? projectUrl = null;
        List<string> tags = [];
        bool listed = true;
        DateTimeOffset? publishedDate = null;
        int releasesLast30Days = 0;
        var releaseCutoff = DateTimeOffset.UtcNow.AddDays(-30);

        // Registration index contains "items" (pages). Walk them to find the latest catalog entry.
        if (root.TryGetProperty("items", out var pages))
        {
            JsonElement? latestLeaf = null;

            foreach (var page in pages.EnumerateArray())
            {
                // Pages may be inlined or require fetching
                JsonElement items;
                if (page.TryGetProperty("items", out var inlineItems))
                {
                    items = inlineItems;
                }
                else if (page.TryGetProperty("@id", out var pageUrl))
                {
                    var pageJson = await GetWithRetryAsync(pageUrl.GetString()!);
                    if (pageJson is null) continue;
                    if (!pageJson.RootElement.TryGetProperty("items", out items)) continue;
                }
                else
                {
                    continue;
                }

                foreach (var leaf in items.EnumerateArray())
                {
                    latestLeaf = leaf; // keep overwriting — last one is latest

                    if (leaf.TryGetProperty("catalogEntry", out var releaseEntry) &&
                        releaseEntry.TryGetProperty("published", out var releasePublished) &&
                        DateTimeOffset.TryParse(releasePublished.GetString(), out var releasePublishedDate) &&
                        releasePublishedDate >= releaseCutoff)
                    {
                        releasesLast30Days++;
                    }
                }
            }

            if (latestLeaf.HasValue)
            {
                var leaf = latestLeaf.Value;

                if (leaf.TryGetProperty("catalogEntry", out var entry))
                {
                    latestVersion = entry.GetStringOrDefault("version", string.Empty);
                    description = entry.GetStringOrDefault("description", string.Empty);
                    authors = entry.GetStringOrDefault("authors", string.Empty);
                    projectUrl = entry.GetStringOrNull("projectUrl");
                    listed = entry.TryGetProperty("listed", out var listedProp) ? listedProp.GetBoolean() : true;

                    if (entry.TryGetProperty("published", out var pub) &&
                        DateTimeOffset.TryParse(pub.GetString(), out var pubDate))
                    {
                        publishedDate = pubDate;
                    }

                    if (entry.TryGetProperty("tags", out var tagsElement))
                    {
                        foreach (var tag in tagsElement.EnumerateArray())
                        {
                            var val = tag.GetString();
                            if (!string.IsNullOrEmpty(val)) tags.Add(val);
                        }
                    }
                }
            }
        }

        // Get total downloads from search API (registration doesn't include aggregate downloads)
        totalDownloads = await GetTotalDownloadsAsync(packageId);

        // Extract and process dependencies
        var dependencyMetrics = await ExtractDependencyMetricsAsync(packageId, latestVersion);

        return new NuGetPackageMetrics
        {
            PackageId = packageId,
            LatestVersion = latestVersion,
            TotalDownloads = totalDownloads,
            Description = description,
            Authors = authors,
            ProjectUrl = projectUrl,
            Tags = tags,
            Listed = listed,
            PublishedDate = publishedDate,
            ReleasesLast30Days = releasesLast30Days,
            Dependencies = dependencyMetrics
        };
    }

    private async Task<DependencyMetrics?> ExtractDependencyMetricsAsync(string packageId, string version)
    {
        try
        {
            var url = $"{RegistrationBaseUrl}/{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}.json";
            var json = await GetWithRetryAsync(url);
            if (json is null) return null;

            var root = json.RootElement;
            var dependencies = new List<PackageDependency>();

            // Parse catalogEntry to get dependencies
            if (root.TryGetProperty("catalogEntry", out var catalogEntry) &&
                catalogEntry.TryGetProperty("dependencyGroups", out var dependencyGroups))
            {
                foreach (var group in dependencyGroups.EnumerateArray())
                {
                    if (group.TryGetProperty("dependencies", out var groupDeps))
                    {
                        foreach (var dep in groupDeps.EnumerateArray())
                        {
                            var depId = dep.GetStringOrDefault("id", "");
                            var depVersion = dep.GetStringOrDefault("range", "*");
                            
                            if (!string.IsNullOrEmpty(depId))
                            {
                                // Check if this dependency is on the latest version
                                var isLatest = await IsPackageOnLatestVersionAsync(depId, depVersion);
                                dependencies.Add(new PackageDependency
                                {
                                    Id = depId,
                                    Version = depVersion,
                                    IsLatest = isLatest
                                });
                            }
                        }
                    }
                }
            }

            if (dependencies.Count == 0) return null;

            var outdatedCount = dependencies.Count(d => !d.IsLatest);
            var freshnessPercent = dependencies.Count > 0 
                ? Math.Round((decimal)(dependencies.Count - outdatedCount) / dependencies.Count * 100, 2) 
                : 0m;

            return new DependencyMetrics
            {
                DirectCount = dependencies.Count,
                Dependencies = dependencies,
                OutdatedCount = outdatedCount,
                FreshnessPercent = freshnessPercent
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NuGet] Failed to extract dependencies for '{packageId}': {ex.Message}");
            return null;
        }
    }

    private async Task<bool> IsPackageOnLatestVersionAsync(string packageId, string versionRange)
    {
        try
        {
            // For simplicity, check if the version range constraint allows the latest version
            // This is a simplified check; a production system would parse version ranges properly
            if (versionRange == "*" || versionRange == "") return true;

            var url = $"{RegistrationBaseUrl}/{packageId.ToLowerInvariant()}/index.json";
            var json = await GetWithRetryAsync(url);
            if (json is null) return false;

            var root = json.RootElement;
            string? latestVersion = null;

            if (root.TryGetProperty("items", out var pages))
            {
                foreach (var page in pages.EnumerateArray())
                {
                    JsonElement items;
                    if (page.TryGetProperty("items", out var inlineItems))
                    {
                        items = inlineItems;
                    }
                    else if (page.TryGetProperty("@id", out var pageUrl))
                    {
                        var pageJson = await GetWithRetryAsync(pageUrl.GetString()!);
                        if (pageJson is null) continue;
                        if (!pageJson.RootElement.TryGetProperty("items", out items)) continue;
                    }
                    else
                    {
                        continue;
                    }

                    foreach (var leaf in items.EnumerateArray())
                    {
                        if (leaf.TryGetProperty("catalogEntry", out var entry))
                        {
                            latestVersion = entry.GetStringOrDefault("version", "");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(latestVersion)) return true;

            // Simple version comparison: if the version range includes the latest version, consider it up-to-date
            // This is a simplified implementation
            return !versionRange.StartsWith("<") && !versionRange.Contains("(") && !versionRange.Contains("[");
        }
        catch
        {
            // If we can't determine, assume it's acceptable
            return true;
        }
    }

    private async Task<long> GetTotalDownloadsAsync(string packageId)
    {
        // Query both shards and use the higher value.
        // Download counts are monotonic; max value is the best available reading.
        var query = $"?q=packageid:{packageId}&take=1";
        var usncTask = GetDownloadsFromShardAsync(SearchShardUsnc + query);
        var usscTask = GetDownloadsFromShardAsync(SearchShardUssc + query);

        await Task.WhenAll(usncTask, usscTask);

        var usncCount = Math.Max(await usncTask, 0);
        var usscCount = Math.Max(await usscTask, 0);
        return Math.Max(usncCount, usscCount);
    }

    /// <summary>
    /// Returns download count from a search shard, or -1 if the shard is unavailable.
    /// </summary>
    private async Task<long> GetDownloadsFromShardAsync(string url)
    {
        try
        {
            var json = await GetWithRetryAsync(url);
            if (json is null) return 0;

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("totalDownloads", out var dl))
                    {
                        return dl.GetInt64();
                    }
                }
            }
        }
        catch (HttpRequestException)
        {
            // Shard unavailable after retries — return -1 so caller can try fallback
            return -1;
        }

        return 0;
    }

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
                    Console.Error.WriteLine($"[NuGet] 404 for {url}");
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? RetryDelay;
                    Console.Error.WriteLine($"[NuGet] Rate limited. Waiting {retryAfter.TotalSeconds}s...");
                    await Task.Delay(retryAfter);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync();
                return await JsonDocument.ParseAsync(stream);
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                Console.Error.WriteLine($"[NuGet] Attempt {attempt + 1} failed for {url}. Retrying...");
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
