using System.Text.Json;
using System.Text.Json.Serialization;
using RepoIdentity.Models;

namespace RepoIdentity.Services;

public sealed class ConfigGenerator : IConfigGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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

        foreach (var repo in activeRepos)
        {
            var color = _colorGenerator.Generate($"{repo.FullName}:{repo.Language ?? "unknown"}");
            var icon = repo.Language is not null && LanguageIcons.TryGetValue(repo.Language, out var li) ? li : "📦";
            var displayName = repo.Name;
            var template = $" {icon} {displayName} ";

            var config = new
            {
                Schema = "https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/schema.json",
                Version = 2,
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
                                Foreground = color,
                                Background = "transparent",
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
}
