using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RepoIdentity.Models;

namespace RepoIdentity.Services;

public sealed class ConfigGenerator : IConfigGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Dictionary<string, string> LanguageIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = "🔷",
        ["Python"] = "🐍",
        ["TypeScript"] = "🟦",
        ["JavaScript"] = "🟨",
        ["Go"] = "🐹",
        ["Rust"] = "🦀",
        ["Java"] = "☕",
        ["PowerShell"] = "💙",
    };

    // Priority-ordered keyword → icon mappings for purpose-based icon selection
    private static readonly (string[] Keywords, string Icon)[] PurposeIcons =
    [
        (["whisper"], "🎙️"),
        (["tts", "speech", "speak", "voice"], "🔊"),
        (["embed", "embedding", "semantic", "rag", "retrieval"], "🧠"),
        (["qr", "qrcode", "barcode"], "📷"),
        (["mcp", "modelcontext", "model-context"], "🔌"),
        (["realtime", "real-time", "streaming", "stream"], "⚡"),
        (["vision", "image", "img", "photo"], "🖼️"),
        (["llm", "gpt", "claude", "openai", "ai-chat", "chatbot"], "🤖"),
        (["agent", "agentic", "copilot"], "🤖"),
        (["nuget", "package", "library", "sdk"], "📦"),
        (["dashboard", "metrics", "analytics", "monitor"], "📊"),
        (["api", "endpoint", "rest", "http"], "🌐"),
    ];

    private readonly IColorGenerator _colorGenerator;

    public ConfigGenerator(IColorGenerator colorGenerator)
    {
        _colorGenerator = colorGenerator;
    }

    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<RepositoryInfo> repositories,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var activeRepos = repositories.Where(r => !r.Archived).ToList();
        var generatedFiles = new List<string>();
        var profiles = new List<object>();

        // Pre-generate colors with minimum perceptual distance enforcement
        var usedColors = new List<string>();
        var repoColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in activeRepos)
        {
            var rawColor = _colorGenerator.Generate($"{repo.FullName}:{repo.Language ?? "unknown"}");
            var color = EnsureMinDistance(rawColor, usedColors);
            usedColors.Add(color);
            repoColors[repo.FullName] = color;
        }

        foreach (var repo in activeRepos)
        {
            var color = repoColors[repo.FullName];
            var contrastColor = GetContrastColor(color);
            var icon = SelectIcon(repo.Name, repo.Language);
            var displayName = repo.Name;
            var template = $" {icon} {displayName} ";

            var config = new
            {
                Schema = "https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/schema.json",
                Version = 2,
                ConsoleTitleTemplate = $" {icon} {displayName} ",
                Blocks = new[]
                {
                    new
                    {
                        Type = "prompt",
                        Alignment = "left",
                        Segments = new[]
                        {
                            new
                            {
                                Type = "text",
                                Foreground = contrastColor,
                                Background = color,
                                Style = "plain",
                                Template = template
                            }
                        }
                    }
                }
            };

            var fileName = SanitizeFileName(repo.FullName) + ".json";
            var filePath = Path.Combine(outputDirectory, fileName);

            var json = JsonSerializer.Serialize(config, WriteOptions);
            // Fix the $schema key (JsonSerializer emits "schema", we need "$schema")
            json = json.Replace("\"schema\":", "\"$schema\":");
            // Fix the console_title_template key (camelCase → snake_case for Oh My Posh)
            json = json.Replace("\"consoleTitleTemplate\":", "\"console_title_template\":");
            // Unescape surrogate pairs so emoji appear as literal UTF-8 (e.g. 🔌 not \uD83D\uDD0C)
            json = UnescapeSurrogatePairs(json);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);

            generatedFiles.Add(filePath);
            profiles.Add(new { repo = repo.FullName, configFile = fileName, accentColor = color, icon });
        }

        // Write index.json
        var index = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            totalRepos = activeRepos.Count,
            profiles
        };
        var indexPath = Path.Combine(outputDirectory, "index.json");
        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(index, WriteOptions), cancellationToken);
        generatedFiles.Add(indexPath);

        return new GenerationResult(activeRepos.Count, outputDirectory, generatedFiles);
    }

    internal static string SanitizeFileName(string fullName)
        => fullName.Replace("/", "-");

    internal static string SelectIcon(string repoName, string? language)
    {
        var lower = repoName.ToLowerInvariant();
        foreach (var (keywords, purposeIcon) in PurposeIcons)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return purposeIcon;
        }
        if (language is not null && LanguageIcons.TryGetValue(language, out var li))
            return li;
        return "📦";
    }

    private static string GetContrastColor(string hexColor)
    {
        var (r, g, b) = ParseRgb(hexColor);
        return Luminance(r, g, b) < 0.5 ? "#FFFFFF" : "#1C1C1C";
    }

    private static double Luminance(int r, int g, int b)
        => (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

    private static string EnsureMinDistance(string newColor, IEnumerable<string> existingColors, int minDistance = 60)
    {
        var existing = existingColors.ToList();
        var current = newColor;

        // Iterative shift with a safety cap to avoid infinite loops
        for (var iter = 0; iter < 20; iter++)
        {
            var (cr, cg, cb) = ParseRgb(current);
            var tooClose = false;
            foreach (var ex in existing)
            {
                var (er, eg, eb) = ParseRgb(ex);
                var dist = Math.Sqrt(
                    Math.Pow(cr - er, 2) +
                    Math.Pow(cg - eg, 2) +
                    Math.Pow(cb - eb, 2));
                if (dist < minDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose)
                return current;

            // Shift R and G channels through the 80-210 range
            var r = 80 + (cr - 80 + 30) % 130;
            var g = 80 + (cg - 80 + 15) % 130;
            current = $"#{r:X2}{g:X2}{cb:X2}";
        }
        return current; // best effort after max iterations
    }

    private static (int r, int g, int b) ParseRgb(string hex)
    {
        hex = hex.TrimStart('#');
        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }

    // System.Text.Json always escapes supplementary Unicode characters (emoji) as
    // \uXXXX\uXXXX surrogate pairs even with UnsafeRelaxedJsonEscaping.
    // This restores them to literal UTF-8 so the JSON file is human-readable.
    private static readonly Regex SurrogatePairPattern = new(
        @"\\u([Dd][89AaBb][0-9A-Fa-f]{2})\\u([Dd][CcDdEeFf][0-9A-Fa-f]{2})",
        RegexOptions.Compiled);

    private static string UnescapeSurrogatePairs(string json)
        => SurrogatePairPattern.Replace(json, m =>
        {
            var high = (char)Convert.ToInt32(m.Groups[1].Value, 16);
            var low  = (char)Convert.ToInt32(m.Groups[2].Value, 16);
            return char.ConvertFromUtf32(char.ConvertToUtf32(high, low));
        });
}