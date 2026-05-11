using GMConverter.Geometry;

namespace GMConverter.Source;

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
}
