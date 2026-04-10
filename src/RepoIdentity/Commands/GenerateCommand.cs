using System.CommandLine;
using RepoIdentity.Services;

namespace RepoIdentity.Commands;

internal static class GenerateCommand
{
    internal static Command Create()
    {
        var sourceOption = new Option<FileInfo>(
            "--source",
            () => new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "data", "latest", "data.repositories.json")),
            "Path to data.repositories.json produced by the dashboard pipeline");

        var outputOption = new Option<DirectoryInfo>(
            "--output",
            () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "terminal", "ohmyposh")),
            "Directory to write generated Oh My Posh config files");

        var command = new Command("generate", "Generate Oh My Posh config files from tracked repos")
        {
            sourceOption,
            outputOption
        };

        command.SetHandler(async (FileInfo source, DirectoryInfo output) =>
        {
            var reader = new DashboardDataReader();
            var colorGen = new ColorGenerator();
            var configGen = new ConfigGenerator(colorGen);

            Console.WriteLine($"Reading repos from: {source.FullName}");
            var data = await reader.ReadAsync(source.FullName);

            Console.WriteLine($"Found {data.Repositories.Count} repos. Generating profiles...");
            var result = await configGen.GenerateAsync(data.Repositories, output.FullName);

            Console.WriteLine($"✅ Generated {result.FilesGenerated} profile(s) → {result.OutputDirectory}");
            foreach (var file in result.GeneratedFiles.Where(f => !f.EndsWith("index.json")))
                Console.WriteLine($"   {Path.GetFileName(file)}");
            Console.WriteLine($"   index.json");
        }, sourceOption, outputOption);

        return command;
    }
}
