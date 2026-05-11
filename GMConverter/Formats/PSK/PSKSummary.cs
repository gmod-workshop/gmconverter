using System.Globalization;
using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Formats.PSK;

internal sealed record PSKSummary(
    string FilePath,
    int PointCount,
    int WedgeCount,
    int FaceCount,
    int MaterialCount,
    int BoneCount,
    int WeightCount,
    int VertexNormalCount,
    int VertexColorCount,
    int ExtraUvChannelCount,
    Bounds Bounds)
{
    public static PSKSummary From(string inputPath, PSKFile psk)
    {
        if (psk.Points.Count == 0)
        {
            throw new GMConverterException("Cannot summarize a PSK with no points.");
        }

        return new PSKSummary(
            inputPath,
            psk.Points.Count,
            psk.Wedges.Count,
            psk.Faces.Count,
            psk.Materials.Count,
            psk.Bones.Count,
            psk.Weights.Count,
            psk.VertexNormals.Count,
            psk.VertexColorCount,
            psk.ExtraUvChannelCount,
            Bounds.FromPoints(psk.Points));
    }

    public override string ToString()
    {
        var size = Bounds.Max - Bounds.Min;
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {FilePath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Points: {PointCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Wedges: {WedgeCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Faces: {FaceCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Materials: {MaterialCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Bones: {BoneCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Weights: {WeightCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Vertex normals: {VertexNormalCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Vertex colors: {VertexColorCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Extra UV channels: {ExtraUvChannelCount}");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Size at --scale 1: {size.X:0.###} x {size.Y:0.###} x {size.Z:0.###}");
        return builder.ToString().TrimEnd();
    }
}
