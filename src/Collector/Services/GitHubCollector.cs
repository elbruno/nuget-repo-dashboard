using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IGitHubCollector
{
    Task<List<GitHubRepoMetrics>> CollectAsync(List<string> repoFullNames);
    Task<List<GitHubRepoMetrics>> CollectWithStubsAsync(List<string> repoFullNames, List<WatchListEntry> watchList);
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
        var collectedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullName in repoFullNames)
        {
            try
            {
                var metrics = await CollectRepoAsync(fullName);
                if (metrics is not null)
                {
                    results.Add(metrics);
                    collectedRepos.Add(fullName);
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

        // Fetch open PRs with full detail
        metrics.RecentPullRequests = await GetRecentPullRequestsAsync(owner, repo);
        metrics.OpenPullRequests = metrics.RecentPullRequests.Count;

        // Fetch recently merged PRs (last 30 days)
        metrics.RecentMergedPullRequests = await GetRecentMergedPullRequestsAsync(owner, repo);
        metrics.MergedPullRequestsCount = metrics.RecentMergedPullRequests.Count;

        // Fetch last 20 open issues
        metrics.RecentIssues = await GetRecentIssuesAsync(owner, repo);

        // Fetch recently closed issues (last 30 days)
        metrics.RecentClosedIssues = await GetRecentClosedIssuesAsync(owner, repo);
        metrics.ClosedIssuesCount = metrics.RecentClosedIssues.Count;

        return metrics;
    }

    private async Task<List<GitHubIssue>> GetRecentIssuesAsync(string owner, string repo)
    {
        // Fetch more than 20 to account for PRs mixed in by the API (pull requests also appear here)
        var json = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/issues?state=open&sort=created&direction=desc&per_page=40");
        if (json is null) return [];

        var issues = new List<GitHubIssue>();
        if (json.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in json.RootElement.EnumerateArray())
            {
                // Skip pull requests – they appear in the issues endpoint too
                if (element.TryGetProperty("pull_request", out _)) continue;

                var issue = new GitHubIssue
                {
                    Number = element.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = element.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                    HtmlUrl = element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
                    CommentsCount = GetCommentsCount(element),
                };

                if (element.TryGetProperty("created_at", out var createdAt) &&
                    DateTimeOffset.TryParse(createdAt.GetString(), out var created))
                {
                    issue.CreatedAt = created;
                }

                if (element.TryGetProperty("updated_at", out var updatedAt) &&
                    DateTimeOffset.TryParse(updatedAt.GetString(), out var updated))
                {
                    issue.UpdatedAt = updated;
                }

                if (element.TryGetProperty("user", out var user) &&
                    user.ValueKind == JsonValueKind.Object &&
                    user.TryGetProperty("login", out var login))
                {
                    issue.UserLogin = login.GetString();
                }

                if (element.TryGetProperty("labels", out var labelsArray) &&
                    labelsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labelsArray.EnumerateArray())
                    {
                        if (label.TryGetProperty("name", out var labelName))
                        {
                            var name = labelName.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                issue.Labels.Add(name);
                            }
                        }
                    }
                }

                issues.Add(issue);
                if (issues.Count >= 20) break;
            }
        }

        json.Dispose();
        return issues;
    }

    private async Task<List<GitHubIssue>> GetRecentClosedIssuesAsync(string owner, string repo)
    {
        var since = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var json = await GetWithRetryAsync(
            $"{ApiBase}/repos/{owner}/{repo}/issues?state=closed&sort=updated&direction=desc&per_page=40&since={since}");
        if (json is null) return [];

        var issues = new List<GitHubIssue>();
        if (json.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in json.RootElement.EnumerateArray())
            {
                // Skip pull requests
                if (element.TryGetProperty("pull_request", out _)) continue;

                var issue = new GitHubIssue
                {
                    Number = element.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = element.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
                    HtmlUrl = element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
                    CommentsCount = GetCommentsCount(element),
                    State = element.TryGetProperty("state", out var state) ? state.GetString() : null,
                };

                if (element.TryGetProperty("created_at", out var createdAt) &&
                    DateTimeOffset.TryParse(createdAt.GetString(), out var created))
                {
                    issue.CreatedAt = created;
                }

                if (element.TryGetProperty("updated_at", out var updatedAt) &&
                    DateTimeOffset.TryParse(updatedAt.GetString(), out var updated))
                {
                    issue.UpdatedAt = updated;
                }

                if (element.TryGetProperty("closed_at", out var closedAt) &&
                    DateTimeOffset.TryParse(closedAt.GetString(), out var closed))
                {
                    issue.ClosedAt = closed;
                }

                if (element.TryGetProperty("user", out var user) &&
                    user.ValueKind == JsonValueKind.Object &&
                    user.TryGetProperty("login", out var login))
                {
                    issue.UserLogin = login.GetString();
                }

                if (element.TryGetProperty("labels", out var labelsArray) &&
                    labelsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in labelsArray.EnumerateArray())
                    {
                        if (label.TryGetProperty("name", out var labelName))
                        {
                            var name = labelName.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                issue.Labels.Add(name);
                            }
                        }
                    }
                }

                issues.Add(issue);
            }
        }

        json.Dispose();
        return issues;
    }

    private async Task<List<GitHubPullRequest>> GetRecentPullRequestsAsync(string owner, string repo)
    {
        var json = await GetWithRetryAsync(
            $"{ApiBase}/repos/{owner}/{repo}/pulls?state=open&sort=created&direction=desc&per_page=40");
        if (json is null) return [];

        var prs = ParsePullRequests(json);
        json.Dispose();
        return prs;
    }

    private async Task<List<GitHubPullRequest>> GetRecentMergedPullRequestsAsync(string owner, string repo)
    {
        var json = await GetWithRetryAsync(
            $"{ApiBase}/repos/{owner}/{repo}/pulls?state=closed&sort=updated&direction=desc&per_page=40");
        if (json is null) return [];

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var prs = new List<GitHubPullRequest>();

        if (json.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in json.RootElement.EnumerateArray())
            {
                // Only include PRs that were actually merged (merged_at is set) within last 30 days
                if (!element.TryGetProperty("merged_at", out var mergedAtProp) ||
                    mergedAtProp.ValueKind == JsonValueKind.Null)
                    continue;

                if (!DateTimeOffset.TryParse(mergedAtProp.GetString(), out var mergedAt) || mergedAt < cutoff)
                    continue;

                var pr = ParseSinglePullRequest(element);
                prs.Add(pr);
            }
        }

        json.Dispose();
        return prs;
    }

    private static List<GitHubPullRequest> ParsePullRequests(JsonDocument json)
    {
        var prs = new List<GitHubPullRequest>();
        if (json.RootElement.ValueKind != JsonValueKind.Array) return prs;

        foreach (var element in json.RootElement.EnumerateArray())
        {
            prs.Add(ParseSinglePullRequest(element));
        }

        return prs;
    }

    private static GitHubPullRequest ParseSinglePullRequest(JsonElement element)
    {
        var pr = new GitHubPullRequest
        {
            Number = element.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
            Title = element.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty,
            HtmlUrl = element.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null,
            IsDraft = element.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
            State = element.TryGetProperty("state", out var state) ? state.GetString() : null,
            CommentsCount = GetCommentsCount(element),
            // additions/deletions/changed_files are not returned by the list endpoint — set to 0
            Additions = 0,
            Deletions = 0,
            ChangedFiles = 0,
            ReviewDecision = null, // Not available from REST list endpoint
        };

        if (element.TryGetProperty("created_at", out var createdAt) &&
            DateTimeOffset.TryParse(createdAt.GetString(), out var created))
        {
            pr.CreatedAt = created;
        }

        if (element.TryGetProperty("updated_at", out var updatedAt) &&
            DateTimeOffset.TryParse(updatedAt.GetString(), out var updated))
        {
            pr.UpdatedAt = updated;
        }

        if (element.TryGetProperty("merged_at", out var mergedAt) &&
            mergedAt.ValueKind != JsonValueKind.Null &&
            DateTimeOffset.TryParse(mergedAt.GetString(), out var merged))
        {
            pr.MergedAt = merged;
        }

        if (element.TryGetProperty("closed_at", out var closedAt) &&
            closedAt.ValueKind != JsonValueKind.Null &&
            DateTimeOffset.TryParse(closedAt.GetString(), out var closed))
        {
            pr.ClosedAt = closed;
        }

        if (element.TryGetProperty("user", out var user) &&
            user.ValueKind == JsonValueKind.Object &&
            user.TryGetProperty("login", out var login))
        {
            pr.UserLogin = login.GetString();
        }

        if (element.TryGetProperty("labels", out var labelsArray) &&
            labelsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelsArray.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var labelName))
                {
                    var name = labelName.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        pr.Labels.Add(name);
                    }
                }
            }
        }

        if (element.TryGetProperty("head", out var head) &&
            head.ValueKind == JsonValueKind.Object &&
            head.TryGetProperty("ref", out var headRef))
        {
            pr.HeadBranch = headRef.GetString();
        }

        if (element.TryGetProperty("base", out var baseProp) &&
            baseProp.ValueKind == JsonValueKind.Object &&
            baseProp.TryGetProperty("ref", out var baseRef))
        {
            pr.BaseBranch = baseRef.GetString();
        }

        return pr;
    }

    private static int GetCommentsCount(JsonElement element)
    {
        if (TryReadIntProperty(element, "comments", out var commentsCount))
        {
            return commentsCount;
        }

        if (TryReadIntProperty(element, "comments_count", out commentsCount))
        {
            return commentsCount;
        }

        return 0;
    }

    private static bool TryReadIntProperty(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            _ => false
        };
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

    public async Task<List<GitHubRepoMetrics>> CollectWithStubsAsync(List<string> repoFullNames, List<WatchListEntry> watchList)
    {
        // First, collect what we can from the API
        var results = await CollectAsync(repoFullNames);
        var collectedFullNames = new HashSet<string>(results.Select(r => r.FullName), StringComparer.OrdinalIgnoreCase);

        // For each watch-list entry, if it wasn't collected, create a stub
        foreach (var watchEntry in watchList)
        {
            var fullName = $"{watchEntry.Owner}/{watchEntry.Repo}";
            if (!collectedFullNames.Contains(fullName))
            {
                var stub = new GitHubRepoMetrics
                {
                    Owner = watchEntry.Owner,
                    Name = watchEntry.Repo,
                    FullName = fullName,
                    Description = watchEntry.Description,
                    HtmlUrl = watchEntry.Url,
                    IsStub = true,
                    StubReason = "Failed to fetch live data from GitHub API (rate limited or unavailable)"
                };

                results.Add(stub);
                Console.WriteLine($"[GitHub] Created stub entry for watch-list repo: {fullName}");
            }
        }

        return results;
    }
}
