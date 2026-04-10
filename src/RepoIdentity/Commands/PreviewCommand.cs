using System.CommandLine;
using RepoIdentity.Services;

namespace RepoIdentity.Commands;

internal static class PreviewCommand
{
    internal static Command Create()
    {
        var sourceOption = new Option<FileInfo>(
            "--source",
            () => new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), "data", "latest", "data.repositories.json")),
            "Path to data.repositories.json produced by the dashboard pipeline");

        var command = new Command("preview", "Preview the Oh My Posh profiles that would be generated")
        {
            sourceOption
        };

        command.SetHandler(async (FileInfo source) =>
        {
            var reader = new DashboardDataReader();
            var colorGen = new ColorGenerator();

            var data = await reader.ReadAsync(source.FullName);
            var activeRepos = data.Repositories.Where(r => !r.Archived).ToList();

            Console.WriteLine($"{"Repo",-45} {"Language",-12} {"Color",-9} {"Stars",5}");
            Console.WriteLine(new string('-', 75));

            foreach (var repo in activeRepos)
            {
                var color = colorGen.Generate($"{repo.FullName}:{repo.Language ?? "unknown"}");
                var lang = repo.Language ?? "(none)";
                Console.WriteLine($"{repo.FullName,-45} {lang,-12} {color,-9} {repo.Stars,5}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {activeRepos.Count} profiles would be generated.");
        }, sourceOption);

        return command;
    }
}
