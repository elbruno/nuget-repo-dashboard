namespace RepoIdentity.Services;

public interface IColorGenerator
{
    /// <summary>Generates a deterministic hex color (e.g. "#A3B4C5") from a seed string.</summary>
    string Generate(string seed);
}
