using System.Text.Json.Serialization;

namespace NuGetDashboard.Collector.Models;

public sealed class MaintainabilityScore
{
    [JsonPropertyName("totalScore")]
    public int TotalScore { get; set; }

    [JsonPropertyName("availablePoints")]
    public int AvailablePoints { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "needs-attention";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "Needs attention";

    [JsonPropertyName("emoji")]
    public string Emoji { get; set; } = "\ud83d\udd34";

    [JsonPropertyName("activity")]
    public MaintainabilityMetricScore Activity { get; set; } = new();

    [JsonPropertyName("issueResolution")]
    public MaintainabilityMetricScore IssueResolution { get; set; } = new();

    [JsonPropertyName("releaseFrequency")]
    public MaintainabilityMetricScore ReleaseFrequency { get; set; } = new();

    [JsonPropertyName("testCoverage")]
    public MaintainabilityMetricScore TestCoverage { get; set; } = new();

    [JsonPropertyName("documentation")]
    public MaintainabilityMetricScore Documentation { get; set; } = new();
}

public sealed class MaintainabilityMetricScore
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("maxScore")]
    public int MaxScore { get; set; }

    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("displayValue")]
    public string? DisplayValue { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}
