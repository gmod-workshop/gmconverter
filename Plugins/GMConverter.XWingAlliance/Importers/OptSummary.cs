using System.Globalization;
using System.Text;
using GMConverter.SDK.Geometry;
using JeremyAnsel.Xwa.Opt;

#pragma warning disable CS8602, CS8604 // JeremyAnsel.Xwa.Opt exposes populated collections without nullable annotations.

namespace GMConverter.XWingAlliance.Importers;

internal sealed record OptSummary(
    string FilePath,
    int MeshCount,
    int LodCount,
    int TextureCount,
    int TextureVersionCount,
    int FaceCount,
    int VertexCount,
    Bounds SourceBounds,
    Bounds DisplayBounds,
    IReadOnlyList<float> LodDistances)
{
    public static OptSummary From(string inputPath, OptFile opt)
    {
        var lodCount = opt.Meshes.Sum(mesh => mesh.Lods.Count);
        var faceCount = opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .SelectMany(lod => lod.FaceGroups)
            .Sum(group => group.Faces.Count);
        var vertexCount = opt.Meshes.Sum(mesh => mesh.Vertices.Count);
        var distances = opt.Meshes
            .SelectMany(mesh => mesh.Lods)
            .Select(lod => lod.Distance)
            .Distinct()
            .OrderByDescending(distance => distance)
            .ToArray();

        return new OptSummary(
            inputPath,
            opt.Meshes.Count,
            lodCount,
            opt.Textures.Count,
            opt.MaxTextureVersion,
            faceCount,
            vertexCount,
            OptHelpers.GetBounds(opt, 1.0f),
            OptHelpers.GetBounds(opt, OptFile.ScaleFactor),
            distances);
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {FilePath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Meshes: {MeshCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"LODs: {LodCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Textures: {TextureCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Texture versions: {TextureVersionCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Faces: {FaceCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Vertices: {VertexCount}");
        AppendBounds(builder, "Source size at --scale 1", SourceBounds);
        AppendBounds(builder, "Library display size", DisplayBounds);

        if (LodDistances.Count > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"LOD distances: {string.Join(", ", LodDistances)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendBounds(StringBuilder builder, string label, Bounds bounds)
    {
        var sizeX = bounds.Max.X - bounds.Min.X;
        var sizeY = bounds.Max.Y - bounds.Min.Y;
        var sizeZ = bounds.Max.Z - bounds.Min.Z;
        var maxDimension = Math.Max(Math.Max(sizeX, sizeY), sizeZ);

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"{label}: {sizeX:0.###} x {sizeY:0.###} x {sizeZ:0.###} Source units, max {maxDimension:0.###}");
    }
}
