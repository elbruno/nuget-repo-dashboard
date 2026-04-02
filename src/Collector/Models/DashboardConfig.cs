using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

/// <summary>
/// Top-level dashboard configuration loaded from config/dashboard-config.json.
/// </summary>
public sealed class DashboardConfig
{
    /// <summary>
    /// NuGet profile username to auto-discover packages from (e.g., "elbruno").
    /// </summary>
    [JsonPropertyName("nugetProfile")]
    public string? NuGetProfile { get; set; }

    /// <summary>
    /// When true, packages from tracked-packages.json are merged with discovered packages.
    /// </summary>
    [JsonPropertyName("mergeWithTrackedPackages")]
    public bool MergeWithTrackedPackages { get; set; } = true;

    /// <summary>
    /// Package IDs to exclude from the final collection (case-insensitive).
    /// </summary>
    [JsonPropertyName("ignorePackages")]
    public List<string> IgnorePackages { get; set; } = [];
}
