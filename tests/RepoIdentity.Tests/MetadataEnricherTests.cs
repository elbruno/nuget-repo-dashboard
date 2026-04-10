using RepoIdentity.Services;
using FluentAssertions;

namespace RepoIdentity.Tests;

public class MetadataEnricherTests
{
    private readonly MetadataEnricher _sut = new();

    [Fact]
    public async Task TryReadAsync_WhenFileExists_ReturnsMetadata()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var identityFile = Path.Combine(dir, "repo.identity.json");
        await File.WriteAllTextAsync(identityFile, """
            {
              "name": "My Library",
              "type": "library",
              "accentColor": "#0078D4",
              "icon": "🧠"
            }
            """);
        try
        {
            var result = await _sut.TryReadAsync(dir);
            result.Should().NotBeNull();
            result!.AccentColor.Should().Be("#0078D4");
            result.Icon.Should().Be("🧠");
            result.Type.Should().Be("library");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryReadAsync_WhenFileAbsent_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var result = await _sut.TryReadAsync(dir);
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryReadAsync_WhenFileMalformed_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "repo.identity.json"), "NOT JSON {{{");
        try
        {
            var result = await _sut.TryReadAsync(dir);
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
