using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NuGetDashboard.Collector.Models;

namespace NuGetDashboard.Collector.Services;

public interface IMaintainabilityScoreService
{
    Task ApplyAsync(
        List<GitHubRepoMetrics> repositories,
        List<PackageConfig> packages,
        List<NuGetPackageMetrics> packageMetrics);
}

public sealed partial class MaintainabilityScoreService : IMaintainabilityScoreService
{
    private readonly HttpClient _httpClient;
    private const string ApiBase = "https://api.github.com";
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxMetricScore = 20;
    private const int FullCommitScoreThreshold = 20;
    private const int FullReleaseScoreThreshold = 4;
    private const double MaxIssueCloseDays = 30.0;

    public MaintainabilityScoreService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task ApplyAsync(
        List<GitHubRepoMetrics> repositories,
        List<PackageConfig> packages,
        List<NuGetPackageMetrics> packageMetrics)
    {
        var releaseDataByRepo = BuildReleaseData(packages, packageMetrics);

        foreach (var repo in repositories)
        {
            if (string.IsNullOrWhiteSpace(repo.Owner) || string.IsNullOrWhiteSpace(repo.Name))
            {
                repo.Maintainability = CreateDefaultScore();
                continue;
            }

            var signals = await CollectSignalsAsync(repo.Owner, repo.Name);
            releaseDataByRepo.TryGetValue(repo.FullName, out var releaseData);
            repo.Maintainability = BuildScore(repo, signals, releaseData);
        }
    }

    internal static MaintainabilityScore BuildScore(
        GitHubRepoMetrics repo,
        RepositoryHealthSignals signals,
        RepoReleaseData? releaseData)
    {
        var activity = BuildActivityScore(signals.RecentCommitsLast30Days);
        var issueResolution = BuildIssueResolutionScore(GetAverageIssueCloseDays(repo));
        var releaseFrequency = BuildReleaseFrequencyScore(releaseData);
        var testCoverage = BuildCoverageScore(signals.CoveragePercent);
        var documentation = BuildDocumentationScore(signals);

        var metrics = new[]
        {
            activity,
            issueResolution,
            releaseFrequency,
            testCoverage,
            documentation
        };

        var availablePoints = metrics.Sum(metric => metric.MaxScore);
        var rawScore = metrics.Sum(metric => metric.Score);
        var totalScore = availablePoints > 0
            ? Math.Clamp((int)Math.Round((double)rawScore / availablePoints * 100, MidpointRounding.AwayFromZero), 1, 100)
            : 1;

        var (status, label, emoji) = GetStatus(totalScore);

        return new MaintainabilityScore
        {
            TotalScore = totalScore,
            AvailablePoints = availablePoints,
            Status = status,
            Label = label,
            Emoji = emoji,
            Activity = activity,
            IssueResolution = issueResolution,
            ReleaseFrequency = releaseFrequency,
            TestCoverage = testCoverage,
            Documentation = documentation
        };
    }

    internal static double? TryExtractCoveragePercent(string readmeContent)
    {
        if (string.IsNullOrWhiteSpace(readmeContent))
        {
            return null;
        }

        var normalizedContent = readmeContent.Replace("%25", "%", StringComparison.OrdinalIgnoreCase);
        var matches = CoverageRegex().Matches(normalizedContent);
        var percentages = new List<double>();

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(match.Groups["pct"].Value, out var percentage) &&
                percentage >= 0 &&
                percentage <= 100)
            {
                percentages.Add(percentage);
            }
        }

        return percentages.Count > 0 ? percentages.Max() : null;
    }

    private static MaintainabilityScore CreateDefaultScore()
    {
        return new MaintainabilityScore
        {
            TotalScore = 1,
            AvailablePoints = 0,
            Status = "needs-attention",
            Label = "Needs attention",
            Emoji = "\ud83d\udd34",
            Activity = CreateUnavailableMetric("Commit activity unavailable."),
            IssueResolution = CreateUnavailableMetric("Issue close-time data unavailable."),
            ReleaseFrequency = CreateUnavailableMetric("Release data unavailable."),
            TestCoverage = CreateUnavailableMetric("Coverage data unavailable."),
            Documentation = CreateUnavailableMetric("Documentation signals unavailable.")
        };
    }

    private static Dictionary<string, RepoReleaseData> BuildReleaseData(
        List<PackageConfig> packages,
        List<NuGetPackageMetrics> packageMetrics)
    {
        var packageMetricsById = packageMetrics.ToDictionary(
            metric => metric.PackageId,
            metric => metric,
            StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, RepoReleaseData>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            if (!packageMetricsById.TryGetValue(package.PackageId, out var metrics))
            {
                continue;
            }

            foreach (var repoFullName in package.Repos.Where(repo => !string.IsNullOrWhiteSpace(repo)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!result.TryGetValue(repoFullName, out var releaseData))
                {
                    releaseData = new RepoReleaseData();
                    result[repoFullName] = releaseData;
                }

                releaseData.HasPackageData = true;
                releaseData.ReleasesLast30Days += metrics.ReleasesLast30Days;
                releaseData.PackageIds.Add(metrics.PackageId);
            }
        }

        return result;
    }

    private async Task<RepositoryHealthSignals> CollectSignalsAsync(string owner, string repo)
    {
        var commits = await GetRecentCommitCountAsync(owner, repo);

        var readmeResponse = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/readme");
        string? readmeContent = null;
        var hasReadme = false;
        var readmeKnown = readmeResponse.Availability != ApiAvailability.Error;

        if (readmeResponse.Document is not null)
        {
            hasReadme = true;
            readmeContent = DecodeGitHubContent(readmeResponse.Document.RootElement);
            readmeResponse.Document.Dispose();
        }

        var docsResponse = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/contents/docs");
        var hasDocsDirectory = docsResponse.Document is not null;
        var docsKnown = docsResponse.Availability != ApiAvailability.Error;
        docsResponse.Document?.Dispose();

        return new RepositoryHealthSignals
        {
            RecentCommitsLast30Days = commits,
            ReadmeKnown = readmeKnown,
            HasReadme = hasReadme,
            DocsKnown = docsKnown,
            HasDocsDirectory = hasDocsDirectory,
            CoveragePercent = TryExtractCoveragePercent(readmeContent ?? string.Empty)
        };
    }

    private async Task<int?> GetRecentCommitCountAsync(string owner, string repo)
    {
        var since = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
        var response = await GetWithRetryAsync($"{ApiBase}/repos/{owner}/{repo}/commits?since={since}&per_page=100");

        try
        {
            if (response.Document is null || response.Document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return response.Document.RootElement.GetArrayLength();
        }
        finally
        {
            response.Document?.Dispose();
        }
    }

    private static double? GetAverageIssueCloseDays(GitHubRepoMetrics repo)
    {
        var durations = repo.RecentClosedIssues
            .Where(issue => issue.CreatedAt != default && issue.ClosedAt.HasValue)
            .Select(issue => (issue.ClosedAt!.Value - issue.CreatedAt).TotalDays)
            .Where(days => days >= 0)
            .ToList();

        return durations.Count > 0 ? durations.Average() : null;
    }

    private static MaintainabilityMetricScore BuildActivityScore(int? commitsLast30Days)
    {
        if (!commitsLast30Days.HasValue)
        {
            return CreateUnavailableMetric("Commit activity unavailable from GitHub API.");
        }

        var raw = Math.Clamp(commitsLast30Days.Value, 0, FullCommitScoreThreshold);
        return CreateMetric(
            score: raw,
            maxScore: MaxMetricScore,
            value: commitsLast30Days.Value,
            displayValue: $"{commitsLast30Days.Value} commit{(commitsLast30Days.Value == 1 ? string.Empty : "s")}",
            summary: $"{commitsLast30Days.Value} commit{(commitsLast30Days.Value == 1 ? string.Empty : "s")} in the last 30 days.");
    }

    private static MaintainabilityMetricScore BuildIssueResolutionScore(double? averageCloseDays)
    {
        if (!averageCloseDays.HasValue)
        {
            return CreateUnavailableMetric("No recent closed issues with close-time data.");
        }

        var normalized = 1.0 - Math.Min(averageCloseDays.Value, MaxIssueCloseDays) / MaxIssueCloseDays;
        var score = (int)Math.Round(normalized * MaxMetricScore, MidpointRounding.AwayFromZero);

        return CreateMetric(
            score: score,
            maxScore: MaxMetricScore,
            value: Math.Round(averageCloseDays.Value, 1),
            displayValue: $"{averageCloseDays.Value:F1} days",
            summary: $"{averageCloseDays.Value:F1} average days to close recent issues.");
    }

    private static MaintainabilityMetricScore BuildReleaseFrequencyScore(RepoReleaseData? releaseData)
    {
        if (releaseData is null || !releaseData.HasPackageData)
        {
            return CreateUnavailableMetric("No package release data mapped to this repository.");
        }

        var score = (int)Math.Round(
            Math.Min(releaseData.ReleasesLast30Days, FullReleaseScoreThreshold) / (double)FullReleaseScoreThreshold * MaxMetricScore,
            MidpointRounding.AwayFromZero);

        return CreateMetric(
            score: score,
            maxScore: MaxMetricScore,
            value: releaseData.ReleasesLast30Days,
            displayValue: $"{releaseData.ReleasesLast30Days} release{(releaseData.ReleasesLast30Days == 1 ? string.Empty : "s")}",
            summary: $"{releaseData.ReleasesLast30Days} package release{(releaseData.ReleasesLast30Days == 1 ? string.Empty : "s")} in the last 30 days across {releaseData.PackageIds.Count} package{(releaseData.PackageIds.Count == 1 ? string.Empty : "s")}.");
    }

    private static MaintainabilityMetricScore BuildCoverageScore(double? coveragePercent)
    {
        if (!coveragePercent.HasValue)
        {
            return CreateUnavailableMetric("Coverage percentage not found in README.");
        }

        var boundedCoverage = Math.Clamp(coveragePercent.Value, 0, 100);
        var score = (int)Math.Round(boundedCoverage / 100.0 * MaxMetricScore, MidpointRounding.AwayFromZero);

        return CreateMetric(
            score: score,
            maxScore: MaxMetricScore,
            value: Math.Round(boundedCoverage, 1),
            displayValue: $"{boundedCoverage:F0}%",
            summary: $"Coverage badge or README text reports {boundedCoverage:F1}% coverage.");
    }

    private static MaintainabilityMetricScore BuildDocumentationScore(RepositoryHealthSignals signals)
    {
        var maxScore = 0;
        var score = 0;

        if (signals.ReadmeKnown)
        {
            maxScore += 10;
            if (signals.HasReadme)
            {
                score += 10;
            }
        }

        if (signals.DocsKnown)
        {
            maxScore += 10;
            if (signals.HasDocsDirectory)
            {
                score += 10;
            }
        }

        if (maxScore == 0)
        {
            return CreateUnavailableMetric("README and docs/ signals unavailable from GitHub API.");
        }

        var summaryParts = new List<string>();
        summaryParts.Add(signals.ReadmeKnown
            ? (signals.HasReadme ? "README present" : "README missing")
            : "README unavailable");
        summaryParts.Add(signals.DocsKnown
            ? (signals.HasDocsDirectory ? "docs/ present" : "docs/ missing")
            : "docs/ unavailable");

        return CreateMetric(
            score: score,
            maxScore: maxScore,
            value: score,
            displayValue: score == maxScore ? "Complete" : "Partial",
            summary: string.Join(", ", summaryParts) + ".");
    }

    private static MaintainabilityMetricScore CreateMetric(
        int score,
        int maxScore,
        double? value,
        string? displayValue,
        string summary)
    {
        return new MaintainabilityMetricScore
        {
            Score = Math.Clamp(score, 0, maxScore),
            MaxScore = maxScore,
            Available = maxScore > 0,
            Value = value,
            DisplayValue = displayValue,
            Summary = summary
        };
    }

    private static MaintainabilityMetricScore CreateUnavailableMetric(string summary)
    {
        return new MaintainabilityMetricScore
        {
            Score = 0,
            MaxScore = 0,
            Available = false,
            Summary = summary
        };
    }

    private static (string Status, string Label, string Emoji) GetStatus(int totalScore)
    {
        if (totalScore > 75)
        {
            return ("good", "Good", "\ud83d\udfe2");
        }

        if (totalScore >= 50)
        {
            return ("fair", "Fair", "\ud83d\udfe1");
        }

        return ("needs-attention", "Needs attention", "\ud83d\udd34");
    }

    private static string? DecodeGitHubContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentElement) ||
            !root.TryGetProperty("encoding", out var encodingElement))
        {
            return null;
        }

        var content = contentElement.GetString();
        var encoding = encodingElement.GetString();
        if (string.IsNullOrWhiteSpace(content) || !string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(content.Replace("\n", string.Empty, StringComparison.Ordinal));
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            Console.Error.WriteLine("[Health] Failed to decode README content from GitHub API.");
            return null;
        }
    }

    private async Task<ApiJsonResponse> GetWithRetryAsync(string url)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new ApiJsonResponse(null, ApiAvailability.NotFound);
                }

                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                        int.TryParse(remaining.FirstOrDefault(), out var rem) &&
                        rem == 0)
                    {
                        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) &&
                            long.TryParse(resetValues.FirstOrDefault(), out var resetEpoch))
                        {
                            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
                            var waitTime = resetTime - DateTimeOffset.UtcNow;
                            if (waitTime > TimeSpan.Zero && waitTime < TimeSpan.FromMinutes(5))
                            {
                                Console.Error.WriteLine($"[Health] GitHub rate limited. Waiting {waitTime.TotalSeconds:F0}s...");
                                await Task.Delay(waitTime);
                                continue;
                            }
                        }

                        Console.Error.WriteLine($"[Health] GitHub rate limited for {url}.");
                        return new ApiJsonResponse(null, ApiAvailability.Error);
                    }
                }

                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync();
                var document = await JsonDocument.ParseAsync(stream);
                return new ApiJsonResponse(document, ApiAvailability.Available);
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                await Task.Delay(RetryDelay * (attempt + 1));
            }
        }

        return new ApiJsonResponse(null, ApiAvailability.Error);
    }

    [GeneratedRegex(@"(?ix)(?:coverage|codecov|coveralls)[^\r\n]{0,80}?(?<pct>100(?:\.\d+)?|[1-9]?\d(?:\.\d+)?)\s*%")]
    private static partial Regex CoverageRegex();

    internal sealed class RepositoryHealthSignals
    {
        public int? RecentCommitsLast30Days { get; init; }
        public bool ReadmeKnown { get; init; }
        public bool HasReadme { get; init; }
        public bool DocsKnown { get; init; }
        public bool HasDocsDirectory { get; init; }
        public double? CoveragePercent { get; init; }
    }

    internal sealed class RepoReleaseData
    {
        public bool HasPackageData { get; set; }
        public int ReleasesLast30Days { get; set; }
        public HashSet<string> PackageIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private enum ApiAvailability
    {
        Available,
        NotFound,
        Error
    }

    private sealed record ApiJsonResponse(JsonDocument? Document, ApiAvailability Availability);
}
