using System.Globalization;
using System.Numerics;
using System.Text;
using MdlCrowbar;
using GMConverter.Common;
using GMConverter.Geometry;
using Mesh = GMConverter.Geometry.Mesh;
using static MdlCrowbar.Enums;

namespace GMConverter.Importers;

internal sealed class MDLImporter : IImporter
{
    public string InputFormat => "mdl";

    public string InputName => "Source Engine";

    public object Summarize(string inputPath)
    {
        var sourceModel = LoadHeader(inputPath);
        return MdlSummary.From(inputPath, sourceModel);
    }

    public Model Parse(string inputPath, ModelParseOptions options)
    {
        var sourceModel = LoadModel(inputPath);
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "GMConverter",
            "mdlcrowbar",
            $"{Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(tempDirectory);

        try
        {
            RequireSuccess(sourceModel.SetAllSmdPathFileNames(), "prepare SMD file names");
            RequireSuccess(sourceModel.WriteReferenceMeshFiles(tempDirectory), "decompile reference mesh SMD");

            var smdPaths = Directory
                .EnumerateFiles(tempDirectory, "*.smd", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (smdPaths.Length == 0)
            {
                throw new GMConverterException("MdlCrowbar did not produce any reference SMD files.");
            }

            var materialPaths = sourceModel
                .GetTextureFolders()
                .Select(NormalizeMaterialDirectory)
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ParseReferenceMeshes(
                Path.GetFileNameWithoutExtension(inputPath),
                smdPaths,
                options.ScaleFactor,
                options.AxisMode,
                materialPaths);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static SourceModel LoadHeader(string inputPath)
    {
        EnsureMdlCrowbarSettings();
        var sourceModel = CreateSourceModel(inputPath);
        RequireSuccess(sourceModel.ReadMdlFileHeader(), "read MDL header");
        RequireSuccess(sourceModel.ReadMdlFile(), "read MDL");
        return sourceModel;
    }

    private static Model ParseReferenceMeshes(
        string modelName,
        IEnumerable<string> smdPaths,
        float scaleFactor,
        ModelAxisMode axisMode,
        IReadOnlyList<string>? materialPaths = null)
    {
        List<Mesh> meshes = [];
        Dictionary<string, Material> materials = new(StringComparer.OrdinalIgnoreCase);

        foreach (var smdPath in smdPaths)
        {
            var mesh = ParseMesh(smdPath, scaleFactor, axisMode, materialPaths, materials);
            if (mesh.Vertices.Count > 0)
            {
                meshes.Add(mesh);
            }
        }

        if (meshes.Count == 0)
        {
            throw new GMConverterException("No reference mesh triangles were found in the decompiled MDL.");
        }

        return new Model(modelName, meshes, materials.Values.ToArray());
    }

    private static Mesh ParseMesh(
        string smdPath,
        float scaleFactor,
        ModelAxisMode axisMode,
        IReadOnlyList<string>? materialPaths,
        Dictionary<string, Material> materials)
    {
        List<Vertex> vertices = [];
        Dictionary<string, List<Triangle>> trianglesByMaterial = new(StringComparer.OrdinalIgnoreCase);
        using var reader = File.OpenText(smdPath);

        var inTriangles = false;
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!inTriangles)
            {
                inTriangles = string.Equals(line, "triangles", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (string.Equals(line, "end", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var materialReference = NormalizeMaterialReference(line, materialPaths);
            if (!materials.ContainsKey(materialReference.Name))
            {
                materials.Add(materialReference.Name, new Material(materialReference.Name, materialReference.Path));
            }

            if (!trianglesByMaterial.TryGetValue(materialReference.Name, out var triangles))
            {
                triangles = [];
                trianglesByMaterial.Add(materialReference.Name, triangles);
            }

            var a = ReadVertex(reader, smdPath, scaleFactor, axisMode, vertices);
            var b = ReadVertex(reader, smdPath, scaleFactor, axisMode, vertices);
            var c = ReadVertex(reader, smdPath, scaleFactor, axisMode, vertices);
            triangles.Add(new Triangle(a, b, c));
        }

        return new Mesh(
            vertices,
            trianglesByMaterial.Select(pair => new Submesh(pair.Key, pair.Value)).ToArray());
    }

    private static int ReadVertex(
        StreamReader reader,
        string smdPath,
        float scaleFactor,
        ModelAxisMode axisMode,
        List<Vertex> vertices)
    {
        var line = reader.ReadLine();
        if (line is null)
        {
            throw new GMConverterException($"Unexpected end of SMD while reading triangle vertices: {smdPath}");
        }

        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9)
        {
            throw new GMConverterException($"Invalid SMD vertex line in {smdPath}: {line}");
        }

        var position = new Vector3(
            ParseFloat(parts[1]) * scaleFactor,
            ParseFloat(parts[2]) * scaleFactor,
            ParseFloat(parts[3]) * scaleFactor);
        var normal = new Vector3(ParseFloat(parts[4]), ParseFloat(parts[5]), ParseFloat(parts[6]));
        var vertex = new Vertex(
            ModelAxisTransforms.TransformPosition(position, axisMode, "mdl"),
            ModelAxisTransforms.TransformNormal(normal, axisMode, "mdl"),
            new Vector2(ParseFloat(parts[7]), ParseFloat(parts[8])));

        vertices.Add(vertex);
        return vertices.Count - 1;
    }

    private static SourceModel LoadModel(string inputPath)
    {
        EnsureMdlCrowbarSettings();
        var sourceModel = CreateSourceModel(inputPath);
        RequireSuccess(sourceModel.ReadMdlFileHeader(), "read MDL header");
        var requiredFiles = sourceModel.CheckForRequiredFiles();
        if (requiredFiles is not FilesFoundFlags.AllFilesFound)
        {
            throw new GMConverterException($"Missing required MDL sidecar files: {requiredFiles}");
        }

        RequireSuccess(sourceModel.ReadMdlFile(), "read MDL");

        if (sourceModel.VtxFileIsUsed)
        {
            RequireSuccess(sourceModel.ReadVtxFile(), "read VTX");
        }

        if (sourceModel.VvdFileIsUsed)
        {
            RequireSuccess(sourceModel.ReadVvdFile(), "read VVD");
        }

        return sourceModel;
    }

    private static SourceModel CreateSourceModel(string inputPath)
    {
        var overrideVersion = 0;
        return SourceModel.Create(inputPath, SupportedMdlVersion.DoNotOverride, ref overrideVersion);
    }

    private static void EnsureMdlCrowbarSettings()
    {
        Settings.SmdFileNames ??= [];
    }

    private static void RequireSuccess(StatusMessage status, string operation)
    {
        if (status is not StatusMessage.Success)
        {
            throw new GMConverterException($"MdlCrowbar failed to {operation}: {status}");
        }
    }

    private static string NormalizeMaterialDirectory(string value)
    {
        var normalized = value.Trim().Trim('"').Replace('\\', '/').Trim('/');

        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["materials/".Length..];
        }

        return normalized;
    }

    private static MaterialReference NormalizeMaterialReference(string value, IReadOnlyList<string>? materialPaths)
    {
        var normalized = value.Trim().Trim('"').Replace('\\', '/');

        if (normalized.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new MaterialReference("default", materialPaths?.FirstOrDefault());
        }

        var materialDirectory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        var materialName = Path.GetFileName(normalized);

        if (!string.IsNullOrWhiteSpace(materialDirectory))
        {
            return new MaterialReference(materialName, materialDirectory);
        }

        return new MaterialReference(materialName, materialPaths?.FirstOrDefault());
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Temporary decompile output is best-effort cleanup only.
        }
    }
}

internal sealed record MaterialReference(string Name, string? Path);

internal sealed record MdlSummary(
    string FilePath,
    string Name,
    string Id,
    int Version,
    bool HasMeshData,
    bool HasPhysicsMeshData,
    bool HasBoneAnimationData,
    bool HasVertexAnimationData,
    IReadOnlyList<string> TextureFolders,
    IReadOnlyList<string> TextureFiles)
{
    public static MdlSummary From(string inputPath, SourceModel sourceModel)
    {
        return new MdlSummary(
            inputPath,
            sourceModel.Name,
            sourceModel.ID,
            sourceModel.Version,
            sourceModel.HasMeshData,
            sourceModel.HasPhysicsMeshData,
            sourceModel.HasBoneAnimationData,
            sourceModel.HasVertexAnimationData,
            sourceModel.GetTextureFolders().ToArray(),
            sourceModel.GetTextureFileNames().ToArray());
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"File: {FilePath}");
        builder.AppendLine($"Name: {Name}");
        builder.AppendLine($"ID: {Id}");
        builder.AppendLine($"Version: {Version}");
        builder.AppendLine($"Mesh data: {HasMeshData}");
        builder.AppendLine($"Physics mesh data: {HasPhysicsMeshData}");
        builder.AppendLine($"Bone animation data: {HasBoneAnimationData}");
        builder.AppendLine($"Vertex animation data: {HasVertexAnimationData}");

        if (TextureFolders.Count > 0)
        {
            builder.AppendLine("Texture folders: " + string.Join(", ", TextureFolders));
        }

        if (TextureFiles.Count > 0)
        {
            builder.AppendLine("Textures: " + string.Join(", ", TextureFiles));
        }

        return builder.ToString().TrimEnd();
    }
}
