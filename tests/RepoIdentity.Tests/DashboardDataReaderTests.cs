using FluentAssertions;
using RepoIdentity.Models;
using RepoIdentity.Services;

namespace RepoIdentity.Tests;

public class DashboardDataReaderTests
{
    [Fact]
    public async Task ReadAsync_WithValidFile_ReturnsParsedData()
    {
        var json = """
        {
          "generatedAt": "2026-04-03T16:37:54Z",
          "repositories": [
            {
              "owner": "elbruno",
              "name": "MyRepo",
              "fullName": "elbruno/MyRepo",
              "description": "A test repo",
              "language": "C#",
              "stars": 42,
              "lastPush": "2026-03-01T10:00:00Z",
              "archived": false,
              "htmlUrl": "https://github.com/elbruno/MyRepo"
            }
          ]
        }
        """;

        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            var reader = new DashboardDataReader();

            var result = await reader.ReadAsync(tempFile);

            result.Repositories.Should().HaveCount(1);
            result.Repositories[0].Owner.Should().Be("elbruno");
            result.Repositories[0].Name.Should().Be("MyRepo");
            result.Repositories[0].Stars.Should().Be(42);
            result.Repositories[0].Language.Should().Be("C#");
            result.Repositories[0].Archived.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReadAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        var reader = new DashboardDataReader();
        await reader.Invoking(r => r.ReadAsync("/nonexistent/file.json"))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_WithNullableFields_HandlesNullsGracefully()
    {
        var json = """
        {
          "generatedAt": "2026-04-03T16:37:54Z",
          "repositories": [
            {
              "owner": "elbruno",
              "name": "NoLangRepo",
              "fullName": "elbruno/NoLangRepo",
              "description": null,
              "language": null,
              "stars": 0,
              "lastPush": "2026-01-01T00:00:00Z",
              "archived": false,
              "htmlUrl": "https://github.com/elbruno/NoLangRepo"
            }
          ]
        }
        """;

        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, json);

        try
        {
            var reader = new DashboardDataReader();
            var result = await reader.ReadAsync(tempFile);
            result.Repositories[0].Language.Should().BeNull();
            result.Repositories[0].Description.Should().BeNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
