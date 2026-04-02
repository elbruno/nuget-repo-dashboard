using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IGitHubCollector
{
    Task<List<GitHubRepoMetrics>> CollectAsync(List<string> repoFullNames);
}

public sealed class GitHubCollector : IGitHubCollector
{
    private readonly HttpClient _httpClient;
    private const string ApiBase = "https://api.github.com";
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public GitHubCollector(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<GitHubRepoMetrics>> CollectAsync(List<string> repoFullNames)
    {
        var results = new List<GitHubRepoMetrics>();

        foreach (var fullName in repoFullNames)
        {
            try
            {
                var metrics = await CollectRepoAsync(fullName);
                if (metrics is not null)
                {
                    results.Add(metrics);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GitHub] Failed to collect '{fullName}': {ex.Message}");
            }
        }

        return results;
    }

    private async Task<GitHubRepoMetrics?> CollectRepoAsync(string fullName)
    {
        var parts = fullName.Split('/', 2);
        if (parts.Length != 2)
        {
            Console.Error.WriteLine($"[GitHub] Invalid repo name: '{fullName}'. Expected 'owner/repo'.");
            return null;
        }

        var owner = parts[0];
        var repo = parts[1];

        // Fetch repo metadata
        var repoJson = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}");
        if (repoJson is null) return null;

        var root = repoJson.RootElement;

        var metrics = new GitHubRepoMetrics
        {
            Owner = owner,
            Name = repo,
            FullName = fullName,
            Stars = root.TryGetProperty("stargazers_count", out var stars) ? stars.GetInt32() : 0,
            Forks = root.TryGetProperty("forks_count", out var forks) ? forks.GetInt32() : 0,
            OpenIssues = root.TryGetProperty("open_issues_count", out var issues) ? issues.GetInt32() : 0,
            Description = root.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Language = root.TryGetProperty("language", out var lang) ? lang.GetString() : null,
            Archived = root.TryGetProperty("archived", out var archived) && archived.GetBoolean(),
            WatchersCount = root.TryGetProperty("subscribers_count", out var watchers) ? watchers.GetInt32() : 0,
            Size = root.TryGetProperty("size", out var size) ? size.GetInt32() : 0,
            DefaultBranch = root.TryGetProperty("default_branch", out var branch) ? branch.GetString() : null,
            Homepage = root.TryGetProperty("homepage", out var homepage) ? homepage.GetString() : null,
            HasWiki = root.TryGetProperty("has_wiki", out var hasWiki) && hasWiki.GetBoolean(),
            HasPages = root.TryGetProperty("has_pages", out var hasPages) && hasPages.GetBoolean(),
            NetworkCount = root.TryGetProperty("network_count", out var network) ? network.GetInt32() : 0,
            Visibility = root.TryGetProperty("visibility", out var visibility) ? visibility.GetString() : null,
            HtmlUrl = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
        };

        if (root.TryGetProperty("license", out var licenseObj) &&
            licenseObj.ValueKind == JsonValueKind.Object &&
            licenseObj.TryGetProperty("spdx_id", out var spdx))
        {
            metrics.License = spdx.GetString();
        }

        if (root.TryGetProperty("pushed_at", out var pushedAt) &&
            DateTimeOffset.TryParse(pushedAt.GetString(), out var lastPush))
        {
            metrics.LastPush = lastPush;
        }

        if (root.TryGetProperty("created_at", out var createdAt) &&
            DateTimeOffset.TryParse(createdAt.GetString(), out var created))
        {
            metrics.CreatedAt = created;
        }

        if (root.TryGetProperty("updated_at", out var updatedAt) &&
            DateTimeOffset.TryParse(updatedAt.GetString(), out var updated))
        {
            metrics.UpdatedAt = updated;
        }

        if (root.TryGetProperty("topics", out var topicsArray) &&
            topicsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var topic in topicsArray.EnumerateArray())
            {
                if (topic.ValueKind == JsonValueKind.String)
                {
                    var topicValue = topic.GetString();
                    if (!string.IsNullOrWhiteSpace(topicValue))
                    {
                        metrics.Topics.Add(topicValue);
                    }
                }
            }
        }

        // Fetch open PRs count
        metrics.OpenPullRequests = await GetOpenPrCountAsync(owner, repo);

        return metrics;
    }

    private async Task<int> GetOpenPrCountAsync(string owner, string repo)
    {
        // Use per_page=1 and check the total from the response to minimize data transfer
        var json = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/pulls?state=open&per_page=1");
        if (json is null) return 0;

        // The response is an array; for an accurate count we check the Link header,
        // but for simplicity we'll fetch up to 100 and count.
        json.Dispose();

        var fullJson = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/pulls?state=open&per_page=100");
        if (fullJson is null) return 0;

        int count = 0;
        if (fullJson.RootElement.ValueKind == JsonValueKind.Array)
        {
            count = fullJson.RootElement.GetArrayLength();
        }

        fullJson.Dispose();
        return count;
    }

    private async Task<JsonDocument?> GetWithRetryAsync(string url)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                using var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.Error.WriteLine($"[GitHub] 404 for {url}");
                    return null;
                }

                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Check for rate limit
                    if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                        int.TryParse(remaining.FirstOrDefault(), out var rem) && rem == 0)
                    {
                        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
                            long.TryParse(resetValues.FirstOrDefault(), out var resetEpoch))
                        {
                            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
                            var waitTime = resetTime - DateTimeOffset.UtcNow;
                            if (waitTime > TimeSpan.Zero && waitTime < TimeSpan.FromMinutes(5))
                            {
                                Console.Error.WriteLine($"[GitHub] Rate limited. Waiting {waitTime.TotalSeconds:F0}s until reset...");
                                await Task.Delay(waitTime);
                                continue;
                            }
                        }

                        Console.Error.WriteLine("[GitHub] Rate limited. Skipping request.");
                        return null;
                    }
                }

                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync();
                return await JsonDocument.ParseAsync(stream);
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                Console.Error.WriteLine($"[GitHub] Attempt {attempt + 1} failed for {url}. Retrying...");
                await Task.Delay(RetryDelay * (attempt + 1));
            }
        }

        return null;
    }
}
