using System.CommandLine;
using System.Diagnostics;

namespace RepoIdentity.Commands;

internal static class InstallCommand
{
    internal static Command Create()
    {
        var profilesOption = new Option<DirectoryInfo>(
            "--profiles",
            () => new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "terminal", "ohmyposh")),
            "Source directory containing generated Oh My Posh profile files");

        var targetOption = new Option<DirectoryInfo>(
            "--target",
            () => new DirectoryInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".poshthemes")),
            "Destination directory for profile files");

        var skipPrereqsOption = new Option<bool>(
            "--skip-prereqs",
            () => false,
            "Skip oh-my-posh availability check");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            () => false,
            "Print every action without executing anything");

        var command = new Command("install", "One-shot device bootstrap: copy profiles and patch $PROFILE")
        {
            profilesOption,
            targetOption,
            skipPrereqsOption,
            dryRunOption
        };

        command.SetHandler((DirectoryInfo profiles, DirectoryInfo target, bool skipPrereqs, bool dryRun) =>
        {
            // Step 1 — Prereq check
            if (!skipPrereqs)
            {
                Console.Write("Checking oh-my-posh...  ");
                var result = RunProcess("oh-my-posh", "--version");
                if (result.ExitCode != 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("⚠️  oh-my-posh not found.");
                    if (OperatingSystem.IsWindows())
                        Console.WriteLine("   Install: winget install JanDeDobbeleer.OhMyPosh -s winget");
                    else
                        Console.WriteLine("   Install: brew install jandedobbeleer/oh-my-posh/oh-my-posh");
                    Console.WriteLine("   Then re-run: repo-identity install");
                    Console.WriteLine("   Or skip check: repo-identity install --skip-prereqs");
                    return;
                }
                Console.WriteLine($"✅ oh-my-posh {result.Output.Trim()} found");
            }

            // Validate profiles directory
            if (!profiles.Exists)
            {
                Console.Error.WriteLine($"Profiles directory not found: {profiles.FullName}");
                Console.Error.WriteLine("Run 'repo-identity generate' first.");
                return;
            }

            // Step 2 — Copy profiles
            Console.WriteLine($"Copying profiles to {target.FullName}...");

            if (!dryRun)
                Directory.CreateDirectory(target.FullName);

            var profileFiles = profiles.GetFiles("*.json");
            foreach (var file in profileFiles)
            {
                var dest = Path.Combine(target.FullName, file.Name);
                if (dryRun)
                    Console.WriteLine($"  [dry-run] Copy {file.Name} → {dest}");
                else
                {
                    File.Copy(file.FullName, dest, overwrite: true);
                    Console.WriteLine($"  ✅ {file.Name} → {dest}");
                }
            }

            // Step 3 — Copy Set-RepoTheme.ps1
            var scriptSrc = Path.Combine(profiles.FullName, "Set-RepoTheme.ps1");
            if (File.Exists(scriptSrc))
            {
                var scriptDest = Path.Combine(target.FullName, "Set-RepoTheme.ps1");
                if (dryRun)
                    Console.WriteLine($"  [dry-run] Copy Set-RepoTheme.ps1 → {scriptDest}");
                else
                {
                    File.Copy(scriptSrc, scriptDest, overwrite: true);
                    Console.WriteLine($"  ✅ Set-RepoTheme.ps1 → {scriptDest}");
                }
            }
            else
            {
                Console.WriteLine("  ⚠️  Set-RepoTheme.ps1 not found in profiles dir. Run 'repo-identity generate' first.");
            }

            // Step 4 — Patch $PROFILE
            Console.WriteLine("Patching $PROFILE...");
            var profilePath = GetPowerShellProfilePath();
            bool alreadyPatched = File.Exists(profilePath) &&
                File.ReadAllText(profilePath).Contains(ProfileMarker);

            if (alreadyPatched)
            {
                Console.WriteLine($"  ✅ $PROFILE already contains repo-identity snippet (skipping)");
            }
            else if (dryRun)
            {
                Console.WriteLine($"  [dry-run] Append snippet to {profilePath}");
                Console.WriteLine("  Snippet:");
                foreach (var line in ProfileSnippet.Trim().Split('\n'))
                    Console.WriteLine("    " + line);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
                File.AppendAllText(profilePath, ProfileSnippet);
                Console.WriteLine($"  ✅ Appended snippet to {profilePath}");
            }

            // Final summary
            if (!dryRun)
            {
                Console.WriteLine();
                Console.WriteLine("✅ Installation complete!");
                Console.WriteLine($"   Profiles: {target.FullName}");
                Console.WriteLine($"   Profile patched: {profilePath}");
                Console.WriteLine();
                Console.WriteLine("Open a new PowerShell window inside any tracked repo folder.");
                Console.WriteLine("Your terminal theme will change automatically.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("[dry-run] No changes made. Remove --dry-run to apply.");
            }
        }, profilesOption, targetOption, skipPrereqsOption, dryRunOption);

        return command;
    }

    static (int ExitCode, string Output) RunProcess(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return (p.ExitCode, p.StandardOutput.ReadToEnd());
        }
        catch { return (1, ""); }
    }

    static string GetPowerShellProfilePath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (OperatingSystem.IsWindows())
            return Path.Combine(docs, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "powershell", "Microsoft.PowerShell_profile.ps1");
    }

    const string ProfileSnippet = """


# repo-identity: auto-detect terminal theme from current git repo
$repoIdentityScript = Join-Path $HOME ".poshthemes/Set-RepoTheme.ps1"
if (Test-Path $repoIdentityScript) {
    . $repoIdentityScript
    function global:Set-Location {
        Microsoft.PowerShell.Management\Set-Location @args
        $s = Join-Path $HOME ".poshthemes/Set-RepoTheme.ps1"
        if (Test-Path $s) { . $s }
    }
}
""";

    const string ProfileMarker = "# repo-identity:";
}
