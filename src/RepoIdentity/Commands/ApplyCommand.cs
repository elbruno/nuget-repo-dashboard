using System.CommandLine;

namespace RepoIdentity.Commands;

internal static class ApplyCommand
{
    internal static Command Create()
    {
        var profilesOption = new Option<DirectoryInfo>(
            "--profiles",
            () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "terminal", "ohmyposh")),
            "Directory containing generated Oh My Posh profile files");

        var targetOption = new Option<DirectoryInfo>(
            "--target",
            () => new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".poshthemes")),
            "Target directory to copy the profile(s) to");

        var repoOption = new Option<string?>(
            "--repo",
            () => null,
            "Full repo name to apply (e.g. elbruno/MyRepo). Omit to copy all profiles.");

        var command = new Command("apply", "Copy generated Oh My Posh profile(s) to your posh themes directory")
        {
            profilesOption,
            targetOption,
            repoOption
        };

        command.SetHandler((DirectoryInfo profiles, DirectoryInfo target, string? repo) =>
        {
            if (!profiles.Exists)
            {
                Console.Error.WriteLine($"Profiles directory not found: {profiles.FullName}");
                Console.Error.WriteLine("Run 'repo-identity generate' first.");
                return;
            }

            target.Create();

            var filesToCopy = repo is not null
                ? profiles.GetFiles($"{repo.Replace("/", "-")}.json")
                : profiles.GetFiles("*.json").Where(f => f.Name != "index.json").ToArray();

            if (!filesToCopy.Any())
            {
                Console.Error.WriteLine(repo is not null
                    ? $"No profile found for repo '{repo}'. Run 'repo-identity generate' first."
                    : "No profiles found. Run 'repo-identity generate' first.");
                return;
            }

            foreach (var file in filesToCopy)
            {
                var dest = Path.Combine(target.FullName, file.Name);
                File.Copy(file.FullName, dest, overwrite: true);
                Console.WriteLine($"✅ {file.Name} → {dest}");
            }

            Console.WriteLine($"\nCopied {filesToCopy.Count()} profile(s) to {target.FullName}");
        }, profilesOption, targetOption, repoOption);

        return command;
    }
}
