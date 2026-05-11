using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;

namespace GMConverter.Exporters;

internal sealed class OBJExporter : IExporter<OBJExportOptions>
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string OutputFormat => "obj";

    public string OutputName => "Wavefront OBJ";

    public void Export(Model model, string outputDirectory, string baseName, OBJExportOptions options)
    {
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var materialPath = Path.Combine(outputDirectory, $"{safeBaseName}.mtl");
        var objPath = Path.Combine(outputDirectory, $"{safeBaseName}.obj");

        Directory.CreateDirectory(outputDirectory);
        ExportTextures(model, outputDirectory);
        WriteMaterials(model, materialPath);
        WriteObj(model, objPath, safeBaseName);

    }

    private static void ExportTextures(Model model, string outputDirectory)
    {
        foreach (var texture in model.Textures)
        {
            var texturePath = Path.Combine(outputDirectory, $"{texture.Name}.png");

            texture.WritePng(texturePath);
        }
    }

    private static void WriteMaterials(Model model, string materialPath)
    {
        using var writer = new StreamWriter(materialPath, false, Utf8NoBom);

        foreach (var material in model.Materials)
        {
            writer.WriteLine(FormattableString.Invariant($"newmtl {material.Name}"));
            writer.WriteLine("Ka 1.000000 1.000000 1.000000");
            writer.WriteLine("Kd 1.000000 1.000000 1.000000");
            writer.WriteLine("Ks 0.000000 0.000000 0.000000");

            if (material.DiffuseTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"map_Kd {material.DiffuseTexture.Name}.png"));
                if (material.HasAlpha)
                {
                    writer.WriteLine(FormattableString.Invariant($"map_d {material.DiffuseTexture.Name}.png"));
                }
            }

            if (material.SpecularTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"map_Ks {material.SpecularTexture.Name}.png"));
            }

            if (material.NormalTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"map_Bump {material.NormalTexture.Name}.png"));
            }

            if (material.EmissiveTexture is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"map_Ke {material.EmissiveTexture.Name}.png"));
            }

            writer.WriteLine();
        }
    }

    private void WriteObj(Model model, string objPath, string safeBaseName)
    {
        using var writer = new StreamWriter(objPath, false, Utf8NoBom);
        writer.WriteLine(FormattableString.Invariant($"mtllib {safeBaseName}.mtl"));
        writer.WriteLine(FormattableString.Invariant($"o {safeBaseName}"));

        var vertexOffset = 1;

        foreach (var mesh in model.Meshes)
        {
            writer.WriteLine(FormattableString.Invariant($"g {safeBaseName}_{vertexOffset}"));

            foreach (var vertex in mesh.Vertices)
            {
                WriteObjVector(writer, "v", vertex.Position);
            }

            foreach (var vertex in mesh.Vertices)
            {
                writer.WriteLine(FormattableString.Invariant(
                    $"vt {vertex.TextureCoordinate.X:0.######} {vertex.TextureCoordinate.Y:0.######}"));
            }

            foreach (var vertex in mesh.Vertices)
            {
                WriteObjVector(writer, "vn", vertex.Normal);
            }

            foreach (var submesh in mesh.Submeshes)
            {
                if (!string.IsNullOrWhiteSpace(submesh.MaterialName))
                {
                    writer.WriteLine(FormattableString.Invariant($"usemtl {submesh.MaterialName}"));
                }

                foreach (var triangle in submesh.Triangles)
                {
                    WriteFace(writer, triangle, vertexOffset);
                }
            }

            vertexOffset += mesh.Vertices.Count;
        }
    }

    private static void WriteFace(StreamWriter writer, Triangle triangle, int vertexOffset)
    {
        writer.Write("f");
        WriteFaceVertex(writer, vertexOffset + triangle.A);
        WriteFaceVertex(writer, vertexOffset + triangle.B);
        WriteFaceVertex(writer, vertexOffset + triangle.C);
        writer.WriteLine();
    }

    private static void WriteFaceVertex(StreamWriter writer, int index)
    {
        writer.Write(FormattableString.Invariant($" {index}/{index}/{index}"));
    }

    private static void WriteObjVector(StreamWriter writer, string prefix, System.Numerics.Vector3 vector)
    {
        writer.WriteLine(FormattableString.Invariant($"{prefix} {vector.X:0.######} {vector.Y:0.######} {vector.Z:0.######}"));
    }
}

internal sealed record OBJExportOptions;
