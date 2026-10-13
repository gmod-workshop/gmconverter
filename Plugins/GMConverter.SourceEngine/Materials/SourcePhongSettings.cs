using System.Globalization;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine.Materials;

internal readonly record struct SourcePhongSettings(
    string Boost,
    string Exponent,
    string FresnelRanges)
{
    public static SourcePhongSettings For(Material material)
    {
        return material.SurfaceKind switch
        {
            MaterialSurfaceKind.Metal => new SourcePhongSettings("1.5", "30", "[0.1 0.45 0.85]"),
            _ => new SourcePhongSettings("1", "25", "[0 0.5 1]")
        };
    }

    /// <summary>
    /// Settings for a material whose mask says how metallic and how glossy it is, on average.
    /// Paint reflects mostly at grazing angles, metal about as strongly head-on, so the fresnel
    /// ranges flatten and the boost rises with metalness; glossier surfaces get sharper highlights.
    /// </summary>
    public static SourcePhongSettings For(float metalness, float gloss)
    {
        var boost = 1f + 2f * metalness;
        var exponent = 10f + 50f * gloss;
        var facing = 0.2f + 0.8f * metalness;
        var middle = 0.6f + 0.4f * metalness;
        return new SourcePhongSettings(
            boost.ToString("0.##", CultureInfo.InvariantCulture),
            exponent.ToString("0", CultureInfo.InvariantCulture),
            FormattableString.Invariant($"[{facing:0.##} {middle:0.##} 1]"));
    }
}
