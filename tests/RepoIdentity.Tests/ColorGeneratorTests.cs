using RepoIdentity.Services;
using FluentAssertions;

namespace RepoIdentity.Tests;

public class ColorGeneratorTests
{
    private readonly ColorGenerator _sut = new();

    [Fact]
    public void Generate_ReturnValidHexColor()
    {
        var color = _sut.Generate("elbruno/MyRepo");
        color.Should().MatchRegex(@"^#[0-9A-F]{6}$");
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var seed = "elbruno/SomeCSharpLib";
        var color1 = _sut.Generate(seed);
        var color2 = _sut.Generate(seed);
        color1.Should().Be(color2);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentColors()
    {
        var colors = new[]
        {
            _sut.Generate("elbruno/RepoA"),
            _sut.Generate("elbruno/RepoB"),
            _sut.Generate("elbruno/RepoC"),
            _sut.Generate("elbruno/RepoD"),
        };
        // Not all the same (extremely unlikely to collide)
        colors.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Theory]
    [InlineData("elbruno/test", "C#")]
    [InlineData("elbruno/test", "Python")]
    [InlineData("different/repo", "C#")]
    public void Generate_ColorsAreInBrightnessRange(string repoName, string language)
    {
        var color = _sut.Generate($"{repoName}:{language}");
        // Parse hex and verify each channel is in 80-210 range
        var r = Convert.ToInt32(color[1..3], 16);
        var g = Convert.ToInt32(color[3..5], 16);
        var b = Convert.ToInt32(color[5..7], 16);
        r.Should().BeInRange(80, 210);
        g.Should().BeInRange(80, 210);
        b.Should().BeInRange(80, 210);
    }
}
