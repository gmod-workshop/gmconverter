using System.Globalization;
using GMConverter.SDK.Materials;

namespace GMConverter.SourceEngine.Materials;

internal readonly record struct SourcePhongSettings(
    string Boost,
    string Exponent,
    string FresnelRanges,
    string EnvmapTint = "[0.5 0.5 0.5]")
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
    /// Paint is mostly matte, catching light at grazing angles; metal reflects head-on as well. So
    /// the fresnel ranges flatten, and the boost and cubemap reflection rise, with metalness, and
    /// glossier surfaces get sharper highlights.
    /// </summary>
    public static SourcePhongSettings For(float metalness, float gloss)
    {
        var boost = 1f + metalness;
        var exponent = 10f + 40f * gloss;
        var facing = 0.1f + 0.5f * metalness;
        var middle = 0.4f + 0.4f * metalness;
        var envmap = 0.05f + 0.35f * metalness;
        return new SourcePhongSettings(
            boost.ToString("0.##", CultureInfo.InvariantCulture),
            exponent.ToString("0", CultureInfo.InvariantCulture),
            FormattableString.Invariant($"[{facing:0.##} {middle:0.##} 1]"),
            FormattableString.Invariant($"[{envmap:0.##} {envmap:0.##} {envmap:0.##}]"));
    }
}
