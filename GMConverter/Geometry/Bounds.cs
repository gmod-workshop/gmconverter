using System.Numerics;
using GMConverter.Common;

namespace GMConverter.Geometry;

/// <summary>
/// Represents an axis-aligned bounding box.
/// </summary>
/// <param name="Min">Minimum coordinates.</param>
/// <param name="Max">Maximum coordinates.</param>
internal sealed record Bounds(Vector3 Min, Vector3 Max)
{
    private const float MinimumThickness = 1.0f;

    public static Bounds FromPoints(IReadOnlyList<Vector3> points)
    {
        if (points.Count == 0)
        {
            throw new GMConverterException("Cannot generate bounds for an empty point set.");
        }

        var minX = points[0].X;
        var minY = points[0].Y;
        var minZ = points[0].Z;
        var maxX = points[0].X;
        var maxY = points[0].Y;
        var maxZ = points[0].Z;

        foreach (var point in points.Skip(1))
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        return new Bounds(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }

    public Bounds WithMinimumThickness()
    {
        var minX = Min.X;
        var minY = Min.Y;
        var minZ = Min.Z;
        var maxX = Max.X;
        var maxY = Max.Y;
        var maxZ = Max.Z;

        EnsureThickness(ref minX, ref maxX);
        EnsureThickness(ref minY, ref maxY);
        EnsureThickness(ref minZ, ref maxZ);

        return new Bounds(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }

    private static void EnsureThickness(ref float min, ref float max)
    {
        if (max - min >= MinimumThickness)
        {
            return;
        }

        var center = (min + max) / 2.0f;
        min = center - MinimumThickness / 2.0f;
        max = center + MinimumThickness / 2.0f;
    }
}
