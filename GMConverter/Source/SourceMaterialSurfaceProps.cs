using GMConverter.Geometry;

namespace GMConverter.Source;

internal static class SourceMaterialSurfaceProps
{
    public static string? For(Material material)
    {
        return material.SurfaceKind switch
        {
            MaterialSurfaceKind.Metal => "metal",
            MaterialSurfaceKind.Wood => "wood",
            MaterialSurfaceKind.Concrete => "concrete",
            _ => null
        };
    }
}
