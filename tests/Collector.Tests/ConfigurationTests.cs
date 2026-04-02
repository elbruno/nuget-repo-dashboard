using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NuGetDashboard.Collector.Models;

namespace Collector.Tests;

/// <summary>
/// Tests for the configurable NuGet profile feature:
/// DashboardConfig model, JSON deserialization, and
/// config resolution precedence (Env Var > User Secrets > config file).
/// </summary>
public class ConfigurationTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigurationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"config-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string WriteTempFile(string filename, string content)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Resolves the effective NuGet profile using the same precedence as the Collector:
    /// Environment Variable > User Secrets > config file default.
    /// IConfiguration already handles env var > user secrets ordering
    /// (last provider added wins for duplicate keys).
    /// </summary>
    private static string? ResolveNuGetProfile(IConfiguration? configuration, DashboardConfig dashboardConfig)
    {
        var profile = configuration?["NUGET_PROFILE"];
        if (!string.IsNullOrWhiteSpace(profile))
            return profile;
        return dashboardConfig.NuGetProfile;
    }

    #region DashboardConfig Model

    [Fact]
    public void DashboardConfig_NuGetProfile_DefaultIsNull()
    {
        var config = new DashboardConfig();
        config.NuGetProfile.Should().BeNull();
    }

    [Fact]
    public void DashboardConfig_NuGetProfile_CanBeSet()
    {
        var config = new DashboardConfig { NuGetProfile = "testuser" };
        config.NuGetProfile.Should().Be("testuser");
    }

    [Fact]
    public void DashboardConfig_NuGetProfile_CanBeOverriddenProgrammatically()
    {
        var config = new DashboardConfig { NuGetProfile = "original" };
        config.NuGetProfile = "overridden";
        config.NuGetProfile.Should().Be("overridden");
    }

    #endregion

    #region JSON Deserialization

    [Fact]
    public async Task DashboardConfig_Deserialize_LoadsNuGetProfileFromConfigFile()
    {
        var json = """{"nugetProfile": "elbruno", "mergeWithTrackedPackages": true}""";
        var path = WriteTempFile("config.json", json);

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<DashboardConfig>(stream);

        config.Should().NotBeNull();
        config!.NuGetProfile.Should().Be("elbruno");
    }

    [Fact]
    public async Task DashboardConfig_Deserialize_MissingNuGetProfile_IsNull()
    {
        var json = """{"mergeWithTrackedPackages": true}""";
        var path = WriteTempFile("no-profile.json", json);

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<DashboardConfig>(stream);

        config.Should().NotBeNull();
        config!.NuGetProfile.Should().BeNull();
    }

    [Fact]
    public async Task DashboardConfig_Deserialize_NullNuGetProfile_IsNull()
    {
        var json = """{"nugetProfile": null, "mergeWithTrackedPackages": true}""";
        var path = WriteTempFile("null-profile.json", json);

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<DashboardConfig>(stream);

        config.Should().NotBeNull();
        config!.NuGetProfile.Should().BeNull();
    }

    [Fact]
    public async Task DashboardConfig_Deserialize_EmptyNuGetProfile_IsEmptyString()
    {
        var json = """{"nugetProfile": "", "mergeWithTrackedPackages": true}""";
        var path = WriteTempFile("empty-profile.json", json);

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<DashboardConfig>(stream);

        config.Should().NotBeNull();
        config!.NuGetProfile.Should().BeEmpty();
    }

    [Fact]
    public void DashboardConfig_SerializeDeserialize_NuGetProfile_RoundTrip()
    {
        var original = new DashboardConfig
        {
            NuGetProfile = "elbruno",
            MergeWithTrackedPackages = true,
            IgnorePackages = ["LocalEmbeddings"]
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DashboardConfig>(json);

        deserialized.Should().NotBeNull();
        deserialized!.NuGetProfile.Should().Be("elbruno");
        deserialized.MergeWithTrackedPackages.Should().BeTrue();
        deserialized.IgnorePackages.Should().ContainSingle().Which.Should().Be("LocalEmbeddings");
    }

    [Fact]
    public void DashboardConfig_JsonPropertyNames_IncludeNuGetProfile()
    {
        var config = new DashboardConfig
        {
            NuGetProfile = "user",
            MergeWithTrackedPackages = true,
            IgnorePackages = ["pkg"]
        };
        var json = JsonSerializer.Serialize(config);

        json.Should().Contain("\"nugetProfile\"");
        json.Should().Contain("\"mergeWithTrackedPackages\"");
        json.Should().Contain("\"ignorePackages\"");
    }

    #endregion

    #region Config Resolution Precedence

    [Fact]
    public void ResolveProfile_EnvVar_TakesPrecedenceOverConfigFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = "env-user"
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("env-user");
    }

    [Fact]
    public void ResolveProfile_UserSecrets_TakesPrecedenceOverConfigFile()
    {
        // User Secrets are just key-value pairs in IConfiguration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = "secrets-user"
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("secrets-user");
    }

    [Fact]
    public void ResolveProfile_EnvVar_OverridesUserSecrets()
    {
        // Simulates: User Secrets first, then Env Vars (last provider wins)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = "secrets-user"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = "env-user"
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("env-user");
    }

    [Fact]
    public void ResolveProfile_NoEnvVar_FallsBackToConfigFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("config-user");
    }

    [Fact]
    public void ResolveProfile_NullConfiguration_FallsBackToConfigFile()
    {
        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(null, dashboardConfig);
        result.Should().Be("config-user");
    }

    [Fact]
    public void ResolveProfile_EmptyEnvVar_FallsBackToConfigFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = ""
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("config-user");
    }

    [Fact]
    public void ResolveProfile_WhitespaceEnvVar_FallsBackToConfigFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = "   "
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("config-user");
    }

    [Fact]
    public void ResolveProfile_NullEnvVar_FallsBackToConfigFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = null
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "config-user" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("config-user");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ResolveProfile_AllSourcesNull_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();
        var dashboardConfig = new DashboardConfig { NuGetProfile = null };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveProfile_ConfigFileEmptyString_NoEnvVar_ReturnsEmpty()
    {
        var configuration = new ConfigurationBuilder().Build();
        var dashboardConfig = new DashboardConfig { NuGetProfile = "" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveProfile_ConfigFileWhitespace_NoEnvVar_ReturnsWhitespace()
    {
        var configuration = new ConfigurationBuilder().Build();
        var dashboardConfig = new DashboardConfig { NuGetProfile = "   " };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be("   ");
    }

    [Theory]
    [InlineData("elbruno")]
    [InlineData("ElBruno")]
    [InlineData("some-user")]
    [InlineData("user_with.dots")]
    public void ResolveProfile_VariousValidProfiles_FromEnvVar(string profile)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NUGET_PROFILE"] = profile
            })
            .Build();

        var dashboardConfig = new DashboardConfig { NuGetProfile = "default" };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be(profile);
    }

    [Theory]
    [InlineData("elbruno")]
    [InlineData("ElBruno")]
    [InlineData("some-user")]
    [InlineData("user_with.dots")]
    public void ResolveProfile_VariousValidProfiles_FromConfigFile(string profile)
    {
        var configuration = new ConfigurationBuilder().Build();
        var dashboardConfig = new DashboardConfig { NuGetProfile = profile };

        var result = ResolveNuGetProfile(configuration, dashboardConfig);
        result.Should().Be(profile);
    }

    #endregion
}
