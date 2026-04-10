using System.CommandLine;
using RepoIdentity.Commands;

var rootCommand = new RootCommand("repo-identity — generate Oh My Posh profiles from tracked NuGet dashboard repos");

rootCommand.AddCommand(GenerateCommand.Create());
rootCommand.AddCommand(PreviewCommand.Create());
rootCommand.AddCommand(ApplyCommand.Create());

return await rootCommand.InvokeAsync(args);
