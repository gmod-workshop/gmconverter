using System.Diagnostics;
using System.Numerics;
using System.Text;
using GMConverter.Common;
using GMConverter.Geometry;
using GMConverter.Source;

namespace GMConverter.Exporters;

/// <summary>
/// Exports a model to the Source Engine MDL format.
/// </summary>
internal sealed class MDLExporter : IExporter<MDLExportOptions>
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private const int SourceMaxConvexPieces = 1024;

    public void Export(
        Model model,
        string outputDirectory,
        string baseName,
        MDLExportOptions options)
    {
        var sourceTools = SourceToolPaths.Resolve(options.EngineDirectory, options.GameDirectory);
        var physicsOptions = options.Physics;
        var modelPath = options.ModelPath;
        var safeBaseName = NameHelpers.SanitizeFileName(baseName);
        var smdPath = Path.Combine(outputDirectory, $"{safeBaseName}.smd");
        var physicsSmdPath =
            physicsOptions is null ? null : Path.Combine(outputDirectory, $"{safeBaseName}_phys.smd");
        var qcPath = Path.Combine(outputDirectory, $"{safeBaseName}.qc");
        var materialRoot = Path.Combine(outputDirectory, "materials");
        var materialRelativeDirectories = GetMaterialDirectories(model, modelPath);
        var materialRelativeDirectory = materialRelativeDirectories[0];
        var materialDirectory =
            Path.Combine(materialRoot, materialRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(materialDirectory);

        WriteSmd(model, smdPath);
        if (physicsSmdPath is not null)
        {
            WritePhysicsSmd(model, physicsSmdPath, physicsOptions!);
        }

        WriteQc(qcPath, modelPath, safeBaseName, materialRelativeDirectories, physicsSmdPath, physicsOptions);
        ExportSourceMaterials(model, materialDirectory, materialRelativeDirectory);

        var result = new MDLExportResult(qcPath, smdPath, physicsSmdPath, materialDirectory, materialRelativeDirectory);
        Compile(model, result, sourceTools, options.BuildMaterials);
    }

    private void Compile(Model model, MDLExportResult result, SourceToolPaths sourceTools, bool buildMaterials)
    {
        if (buildMaterials)
        {
            if (sourceTools.CanCompileMaterials)
            {
                var materialCompiler = new SourceMaterialCompiler(sourceTools.VtexPath!, sourceTools.GameDirectory);
                materialCompiler.Compile(model.Materials, result.MaterialRelativeDirectory);
            }
        }

        if (sourceTools.CanCompileModel)
        {
            RunStudioMdl(sourceTools.StudioMdlPath!, result.QcPath);
        }
    }

    private static void RunStudioMdl(string studioMdlPath, string qcPath)
    {
        if (!File.Exists(studioMdlPath))
        {
            throw new GMConverterException($"studiomdl not found: {studioMdlPath}");
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = studioMdlPath,
            ArgumentList = { qcPath },
            UseShellExecute = false
        });

        process?.WaitForExit();

        if (process is null)
        {
            throw new GMConverterException("Failed to start studiomdl.");
        }

        if (process.ExitCode != 0)
        {
            throw new GMConverterException($"studiomdl exited with code {process.ExitCode}.");
        }
    }

    private void WriteSmd(Model model, string smdPath)
    {
        using var writer = new StreamWriter(smdPath, false, Utf8NoBom);
        writer.WriteLine("version 1");
        writer.WriteLine("nodes");
        writer.WriteLine("0 \"root\" -1");
        writer.WriteLine("end");
        writer.WriteLine("skeleton");
        writer.WriteLine("time 0");
        writer.WriteLine("0 0 0 0 0 0 0");
        writer.WriteLine("end");
        writer.WriteLine("triangles");

        foreach (var mesh in model.Meshes)
        {
            foreach (var submesh in mesh.Submeshes)
            {
                var materialName = submesh.MaterialName ?? "default";

                foreach (var triangle in submesh.Triangles)
                {
                    writer.WriteLine(materialName);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.A]);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.B]);
                    WriteSmdVertex(writer, mesh.Vertices[triangle.C]);
                }
            }
        }

        writer.WriteLine("end");
    }

    private static void WriteSmdVertex(StreamWriter writer, Vertex vertex)
    {
        var position = vertex.Position;
        var normal = vertex.Normal;
        var uv = vertex.TextureCoordinate;

        writer.WriteLine(FormattableString.Invariant(
            $"0 {position.X:0.######} {position.Y:0.######} {position.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} {uv.X:0.######} {uv.Y:0.######}"));
    }

    private void WritePhysicsSmd(Model model, string physicsSmdPath, PhysicsOptions physicsOptions)
    {
        switch (physicsOptions.Mode)
        {
            case PhysicsMode.Bounds:
                WriteBoundsPhysicsSmd(model, physicsSmdPath);
                break;

            case PhysicsMode.Coacd:
                WriteCoacdPhysicsSmd(model, physicsSmdPath,
                    physicsOptions.Coacd ?? throw new GMConverterException("Missing CoACD options."));
                break;

            default:
                throw new GMConverterException($"Unsupported physics mode: {physicsOptions.Mode}");
        }
    }

    private void WriteBoundsPhysicsSmd(Model model, string physicsSmdPath)
    {
        var bounds = model.Bounds().WithMinimumThickness();
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);

        Vector3[] vertices =
        [
            new(bounds.Min.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Min.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Min.Z),
            new(bounds.Min.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Min.Y, bounds.Max.Z),
            new(bounds.Max.X, bounds.Max.Y, bounds.Max.Z),
            new(bounds.Min.X, bounds.Max.Y, bounds.Max.Z)
        ];

        WritePhysicsQuad(writer, vertices, 0, 3, 2, 1, -Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 4, 5, 6, 7, Vector3.UnitZ);
        WritePhysicsQuad(writer, vertices, 0, 1, 5, 4, -Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 3, 7, 6, 2, Vector3.UnitY);
        WritePhysicsQuad(writer, vertices, 0, 4, 7, 3, -Vector3.UnitX);
        WritePhysicsQuad(writer, vertices, 1, 2, 6, 5, Vector3.UnitX);

        writer.WriteLine("end");
    }

    private void WriteCoacdPhysicsSmd(Model model, string physicsSmdPath, CoacdOptions options)
    {
        var parts = CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(options.Threshold, options.MaxConvexPieces, options.MaxHullVertices));

        if (parts.Count == 0)
        {
            throw new GMConverterException("CoACD did not produce any convex parts.");
        }

        WritePhysicsPartsSmd(physicsSmdPath, parts);
    }

    private void WritePhysicsPartsSmd(string physicsSmdPath, IReadOnlyList<Mesh> parts)
    {
        using var writer = CreatePhysicsSmdWriter(physicsSmdPath);
        var triangleCount = 0;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var materialName = FormattableString.Invariant($"physics_{partIndex}");
            var partNormal = GetPartNormal(part);

            foreach (var triangle in part.Triangles)
            {
                WritePhysicsTriangle(
                    writer,
                    part.Vertices[triangle.A].Position,
                    part.Vertices[triangle.B].Position,
                    part.Vertices[triangle.C].Position,
                    partNormal,
                    materialName);
                triangleCount++;
            }
        }

        if (triangleCount == 0)
        {
            throw new GMConverterException("CoACD produced convex parts, but none of them contained triangles.");
        }

        writer.WriteLine("end");
    }

    private static StreamWriter CreatePhysicsSmdWriter(string physicsSmdPath)
    {
        var writer = new StreamWriter(physicsSmdPath, false, Utf8NoBom);
        writer.WriteLine("version 1");
        writer.WriteLine("nodes");
        writer.WriteLine("0 \"root\" -1");
        writer.WriteLine("end");
        writer.WriteLine("skeleton");
        writer.WriteLine("time 0");
        writer.WriteLine("0 0 0 0 0 0 0");
        writer.WriteLine("end");
        writer.WriteLine("triangles");
        return writer;
    }

    private static Vector3 GetPartNormal(Mesh part)
    {
        foreach (var triangle in part.Triangles)
        {
            var normal = triangle.Normal(part.Vertices);

            if (normal != Vector3.UnitZ)
            {
                return normal;
            }
        }

        return Vector3.UnitZ;
    }

    private static void WritePhysicsQuad(StreamWriter writer, IReadOnlyList<Vector3> vertices, int a, int b, int c,
        int d, Vector3 normal)
    {
        WritePhysicsTriangle(writer, vertices[a], vertices[b], vertices[c], normal);
        WritePhysicsTriangle(writer, vertices[a], vertices[c], vertices[d], normal);
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal)
    {
        WritePhysicsTriangle(writer, a, b, c, normal, "physics");
    }

    private static void WritePhysicsTriangle(StreamWriter writer, Vector3 a, Vector3 b, Vector3 c,
        Vector3 normal, string materialName)
    {
        writer.WriteLine(materialName);
        WritePhysicsVertex(writer, a, normal);
        WritePhysicsVertex(writer, b, normal);
        WritePhysicsVertex(writer, c, normal);
    }

    private static void WritePhysicsVertex(StreamWriter writer, Vector3 vertex, Vector3 normal)
    {
        writer.WriteLine(FormattableString.Invariant(
            $"0 {vertex.X:0.######} {vertex.Y:0.######} {vertex.Z:0.######} {normal.X:0.######} {normal.Y:0.######} {normal.Z:0.######} 0 0"));
    }

    private static void WriteQc(
        string qcPath,
        string modelPath,
        string safeBaseName,
        IReadOnlyList<string> materialRelativeDirectories,
        string? physicsSmdPath,
        PhysicsOptions? physicsOptions)
    {
        var smdFileName = $"{safeBaseName}.smd";
        using var writer = new StreamWriter(qcPath, false, Utf8NoBom);

        writer.WriteLine(FormattableString.Invariant($"$modelname \"{modelPath.Replace('\\', '/')}\""));
        writer.WriteLine(FormattableString.Invariant($"$body \"body\" \"{smdFileName}\""));
        foreach (var materialRelativeDirectory in materialRelativeDirectories)
        {
            writer.WriteLine(FormattableString.Invariant($"$cdmaterials \"{materialRelativeDirectory.Replace('\\', '/')}\""));
        }
        writer.WriteLine("$staticprop");
        writer.WriteLine("$surfaceprop \"metal\"");
        writer.WriteLine("$sequence \"idle\" \"{0}\" fps 1", smdFileName);

        if (physicsSmdPath is not null)
        {
            writer.WriteLine(FormattableString.Invariant($"$collisionmodel \"{Path.GetFileName(physicsSmdPath)}\""));
            writer.WriteLine("{");
            if (physicsOptions?.Mode is PhysicsMode.Coacd)
            {
                writer.WriteLine("    $concave");
                writer.WriteLine(FormattableString.Invariant($"    $maxconvexpieces {SourceMaxConvexPieces}"));
            }

            writer.WriteLine(FormattableString.Invariant($"    $mass {(physicsOptions?.Mass ?? 100.0f):0.###}"));
            writer.WriteLine("}");
        }
    }

    private void ExportSourceMaterials(Model model, string materialDirectory, string materialRelativeDirectory)
    {
        foreach (var material in model.Materials)
        {
            if (material.DiffuseTexture is null)
            {
                continue;
            }

            var pngPath = Path.Combine(materialDirectory, $"{material.Name}.png");
            var vmtPath = Path.Combine(materialDirectory, $"{material.Name}.vmt");
            var sourceTexturePath = $"{materialRelativeDirectory}/{material.Name}".Replace('\\', '/');

            material.DiffuseTexture.WritePng(pngPath);

            using var writer = new StreamWriter(vmtPath, false, Utf8NoBom);
            writer.WriteLine("\"VertexLitGeneric\"");
            writer.WriteLine("{");
            writer.WriteLine(FormattableString.Invariant($"    \"$basetexture\" \"{sourceTexturePath}\""));

            if (material.HasAlpha)
            {
                writer.WriteLine("    \"$translucent\" \"1\"");
            }

            if (material.IsIlluminated)
            {
                writer.WriteLine("    \"$selfillum\" \"1\"");
                writer.WriteLine(FormattableString.Invariant($"    \"$selfillummask\" \"{sourceTexturePath}_illum\""));
            }

            writer.WriteLine("}");

            material.EmissiveTexture?.WritePng(Path.Combine(materialDirectory, $"{material.Name}_illum.png"));
        }
    }

    private static IReadOnlyList<string> GetMaterialDirectories(Model model, string modelPath)
    {
        var directories = model.Materials
            .SelectMany(MaterialPaths)
            .Select(NormalizeMaterialDirectory)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return directories.Length == 0 ? [GetMaterialDirectory(modelPath)] : directories;
    }

    private static IEnumerable<string> MaterialPaths(Material material)
    {
        if (!string.IsNullOrWhiteSpace(material.Path))
        {
            yield return material.Path;
        }

        foreach (var texture in material.Textures)
        {
            if (!string.IsNullOrWhiteSpace(texture.Path))
            {
                yield return texture.Path;
            }
        }
    }

    private static string GetMaterialDirectory(string modelPath)
    {
        var normalized = modelPath.Replace('\\', '/');

        if (normalized.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');

        return string.IsNullOrWhiteSpace(directory) ? "models/gmconverter" : directory;
    }

    private static string NormalizeMaterialDirectory(string materialDirectory)
    {
        var normalized = materialDirectory.Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["materials/".Length..];
        }

        return normalized;
    }

    private sealed record MDLExportResult(
        string QcPath,
        string SmdPath,
        string? PhysicsSmdPath,
        string MaterialDirectory,
        string MaterialRelativeDirectory);
}

internal sealed record MDLExportOptions(
    string ModelPath,
    string? EngineDirectory,
    string? GameDirectory,
    bool BuildMaterials,
    PhysicsOptions? Physics);
