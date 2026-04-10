namespace RepoIdentity.Models;

public record GenerationResult(
    int FilesGenerated,
    string OutputDirectory,
    IReadOnlyList<string> GeneratedFiles
);
