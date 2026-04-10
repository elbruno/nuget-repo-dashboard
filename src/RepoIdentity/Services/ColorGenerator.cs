using System.Security.Cryptography;
using System.Text;

namespace RepoIdentity.Services;

public sealed class ColorGenerator : IColorGenerator
{
    public string Generate(string seed)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        // Use first 3 bytes for R, G, B
        // Bias toward mid-range brightness: map 0-255 → 80-210 to avoid very dark/very pale
        var r = (int)(80 + hashBytes[0] / 255.0 * 130);
        var g = (int)(80 + hashBytes[1] / 255.0 * 130);
        var b = (int)(80 + hashBytes[2] / 255.0 * 130);

        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
