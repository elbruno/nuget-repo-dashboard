using System.Net;
using Collector.Tests.Helpers;
using FluentAssertions;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class NuGetProfileDiscoveryServiceTests
{
    // GitHub URL parsing tests - standard repos without dots
    [Theory]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://github.com/JamesNK/Newtonsoft.Json", "JamesNK/Newtonsoft.Json")]
    [InlineData("https://github.com/serilog/serilog", "serilog/serilog")]
    public void ParseGitHubRepo_StandardUrl_ReturnsOwnerAndRepo(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // GitHub URL parsing tests - repos WITH DOTS in the name (the bug fix!)
    [Theory]
    [InlineData("https://github.com/elbruno/ElBruno.ClockTray", "elbruno/ElBruno.ClockTray")]
    [InlineData("https://github.com/elbruno/ElBruno.HuggingFace.Downloader", "elbruno/ElBruno.HuggingFace.Downloader")]
    [InlineData("https://github.com/elbruno/ElBruno.LocalLLMs", "elbruno/ElBruno.LocalLLMs")]
    [InlineData("https://github.com/elbruno/ElBruno.ModelContextProtocol", "elbruno/ElBruno.ModelContextProtocol")]
    [InlineData("https://github.com/elbruno/ElBruno.QRCodeGenerator", "elbruno/ElBruno.QRCodeGenerator")]
    [InlineData("https://github.com/elbruno/ElBruno.Realtime", "elbruno/ElBruno.Realtime")]
    [InlineData("https://github.com/elbruno/ElBruno.Text2Image", "elbruno/ElBruno.Text2Image")]
    [InlineData("https://github.com/elbruno/elbruno.localembeddings", "elbruno/elbruno.localembeddings")]
    [InlineData("https://github.com/elbruno/elbruno.OllamaSharp.Extensions", "elbruno/elbruno.OllamaSharp.Extensions")]
    public void ParseGitHubRepo_RepoNameWithDots_ReturnsFullRepoName(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Edge cases: dotted repo names with trailing paths
    [Theory]
    [InlineData("https://github.com/elbruno/ElBruno.Text2Image/tree/main", "elbruno/ElBruno.Text2Image")]
    [InlineData("https://github.com/elbruno/ElBruno.LocalLLMs/blob/main/README.md", "elbruno/ElBruno.LocalLLMs")]
    [InlineData("https://github.com/elbruno/ElBruno.QRCodeGenerator/issues", "elbruno/ElBruno.QRCodeGenerator")]
    public void ParseGitHubRepo_DottedRepoWithTrailingPath_ReturnsRepoWithoutPath(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Edge cases: dotted repo names with .git suffix
    [Theory]
    [InlineData("https://github.com/elbruno/ElBruno.Realtime.git", "elbruno/ElBruno.Realtime")]
    [InlineData("https://github.com/elbruno/ElBruno.ClockTray.git", "elbruno/ElBruno.ClockTray")]
    public void ParseGitHubRepo_DottedRepoWithGitSuffix_StripsGitSuffix(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Edge cases: dotted repo names with query strings
    [Theory]
    [InlineData("https://github.com/elbruno/ElBruno.QwenTTS?tab=readme", "elbruno/ElBruno.QwenTTS")]
    [InlineData("https://github.com/elbruno/ElBruno.Text2Image?ref=main", "elbruno/ElBruno.Text2Image")]
    public void ParseGitHubRepo_DottedRepoWithQueryString_ReturnsRepoWithoutQuery(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Edge cases: dotted repo names with fragments
    [Theory]
    [InlineData("https://github.com/elbruno/ElBruno.LocalLLMs#readme", "elbruno/ElBruno.LocalLLMs")]
    [InlineData("https://github.com/elbruno/ElBruno.ModelContextProtocol#installation", "elbruno/ElBruno.ModelContextProtocol")]
    public void ParseGitHubRepo_DottedRepoWithFragment_ReturnsRepoWithoutFragment(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Non-dotted repos with paths (regression check)
    [Theory]
    [InlineData("https://github.com/owner/repo/tree/main", "owner/repo")]
    [InlineData("https://github.com/owner/repo/tree/main/docs", "owner/repo")]
    [InlineData("https://github.com/owner/repo/blob/master/README.md", "owner/repo")]
    [InlineData("https://github.com/owner/repo/issues", "owner/repo")]
    [InlineData("https://github.com/owner/repo/pulls", "owner/repo")]
    public void ParseGitHubRepo_UrlWithPathSuffix_ReturnsOwnerAndRepo(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    // Non-dotted repos with .git suffix (regression check)
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/Microsoft/dotnet.git", "Microsoft/dotnet")]
    public void ParseGitHubRepo_UrlWithGitSuffix_ReturnsOwnerAndRepo(string url, string expected)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://nuget.org")]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://bitbucket.org/owner/repo")]
    public void ParseGitHubRepo_NonGitHubUrl_ReturnsNull(string url)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseGitHubRepo_NullOrEmptyInput_ReturnsNull(string? url)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/")]
    public void ParseGitHubRepo_MalformedGitHubUrl_ReturnsNull(string url)
    {
        var result = NuGetProfileDiscoveryService.ParseGitHubRepo(url);
        result.Should().BeNull();
    }

    // Discovery service tests - these will be uncommented/updated once implementation is available
    /*
    [Fact]
    public async Task DiscoverAsync_ValidUsername_ReturnsPackageList()
    {
        var mockHandler = new MockHttpMessageHandler();
        var searchJson = BuildNuGetSearchResponse([
            ("Package.One", "1.0.0", "Description one", 1000, "https://github.com/owner/repo1"),
            ("Package.Two", "2.0.0", "Description two", 2000, "https://github.com/owner/repo2")
        ]);
        
        mockHandler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=owner:testuser&take=100",
            HttpStatusCode.OK,
            searchJson
        );

        var httpClient = new HttpClient(mockHandler);
        var service = new NuGetProfileDiscoveryService(httpClient);

        var result = await service.DiscoverAsync("testuser");

        result.Should().HaveCount(2);
        result[0].PackageId.Should().Be("Package.One");
        result[0].Version.Should().Be("1.0.0");
        result[0].DownloadCount.Should().Be(1000);
        result[0].ProjectUrl.Should().Be("https://github.com/owner/repo1");
        result[0].GitHubRepo.Should().Be("owner/repo1");
    }

    [Fact]
    public async Task DiscoverAsync_EmptySearchResponse_ReturnsEmptyList()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=owner:nobody&take=100",
            HttpStatusCode.OK,
            BuildNuGetSearchResponse([])
        );

        var httpClient = new HttpClient(mockHandler);
        var service = new NuGetProfileDiscoveryService(httpClient);

        var result = await service.DiscoverAsync("nobody");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_OwnerWithNoPackages_ReturnsEmptyList()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=owner:nopackages&take=100",
            HttpStatusCode.OK,
            "{\"data\":[]}"
        );

        var httpClient = new HttpClient(mockHandler);
        var service = new NuGetProfileDiscoveryService(httpClient);

        var result = await service.DiscoverAsync("nopackages");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_MalformedJson_ThrowsException()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.AddResponse(
            "https://azuresearch-usnc.nuget.org/query?q=owner:bad&take=100",
            HttpStatusCode.OK,
            "{ invalid json !!!"
        );

        var httpClient = new HttpClient(mockHandler);
        var service = new NuGetProfileDiscoveryService(httpClient);

        var act = () => service.DiscoverAsync("bad");

        await act.Should().ThrowAsync<Exception>();
    }

    private static string BuildNuGetSearchResponse(
        (string packageId, string version, string description, long downloads, string? projectUrl)[] packages)
    {
        var dataItems = packages.Select(p => 
        {
            var projectUrlJson = p.projectUrl is null ? "null" : $"\"{p.projectUrl}\"";
            return $$"""
                {
                  "id": "{{p.packageId}}",
                  "version": "{{p.version}}",
                  "description": "{{p.description}}",
                  "totalDownloads": {{p.downloads}},
                  "projectUrl": {{projectUrlJson}}
                }
                """;
        });

        return $$"""
        {
          "data": [
            {{string.Join(",\n    ", dataItems)}}
          ]
        }
        """;
    }
    */
}
