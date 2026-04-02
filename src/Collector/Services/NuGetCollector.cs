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
            PublishedDate = publishedDate
        };
    }

    private async Task<long> GetTotalDownloadsAsync(string packageId)
    {
        var url = $"https://api-v2v3search-0.nuget.org/query?q=packageid:{packageId}&take=1";
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
