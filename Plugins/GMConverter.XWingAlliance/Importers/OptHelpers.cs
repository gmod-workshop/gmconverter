using System.Numerics;
using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using JeremyAnsel.Xwa.Opt;

#pragma warning disable CS8602, CS8604 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.XWingAlliance.Importers;

internal static class OptHelpers
{
    public static float GetHighestLodDistance(OptFile opt)
    {
        return opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .Select(lod => lod.Distance)
            .DefaultIfEmpty(0)
            .OrderByDescending(distance => distance)
            .First();
    }

    public static Bounds GetBounds(OptFile opt, float scaleFactor)
    {
        var hasVertex = false;
        float minX = 0;
        float minY = 0;
        float minZ = 0;
        float maxX = 0;
        float maxY = 0;
        float maxZ = 0;

        foreach (var mesh in opt.Meshes)
        {
            foreach (var vertex in mesh.Vertices)
            {
                var sourceVertex = CoordinateTransforms.ToSource(vertex, scaleFactor, ModelAxisMode.Auto);

                if (!hasVertex)
                {
                    minX = maxX = sourceVertex.X;
                    minY = maxY = sourceVertex.Y;
                    minZ = maxZ = sourceVertex.Z;
                    hasVertex = true;
                    continue;
                }

                minX = Math.Min(minX, sourceVertex.X);
                minY = Math.Min(minY, sourceVertex.Y);
                minZ = Math.Min(minZ, sourceVertex.Z);
                maxX = Math.Max(maxX, sourceVertex.X);
                maxY = Math.Max(maxY, sourceVertex.Y);
                maxZ = Math.Max(maxZ, sourceVertex.Z);
            }
        }

        if (!hasVertex)
        {
            throw new GMConverterException("Cannot convert an OPT with no vertices.");
        }

        return new Bounds(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }
}
