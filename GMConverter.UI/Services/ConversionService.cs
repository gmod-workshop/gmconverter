using System.Numerics;
using GMConverter.Common;
using GMConverter.Exporters;
using GMConverter.Geometry;
using GMConverter.Importers;
using GMConverter.Source;
using Microsoft.Extensions.Logging;

namespace GMConverter.UI.Services;

internal sealed class ConversionService(UiLogSink logSink)
{
    private const int _maxCoacdPreviewTriangles = 5000;

    public string RunConversion(ConversionSettings settings)
    {
        var inputPath = RequireInputFile(settings.InputPath, settings.InputFormat);
        using var loggerFactory = CreateLoggerFactory();
        var importer = CreateImporter(settings.InputFormat, loggerFactory);

        if (settings.OutputFormat is "info")
        {
            return importer.Summarize(inputPath).ToString() ?? string.Empty;
        }

        var outputPath = RequireOutputPath(settings.OutputPath, settings.OutputFormat);
        var baseName = string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : settings.BaseName;
        var model = importer.Parse(inputPath, new ModelParseOptions(
            settings.ScaleFactor,
            settings.AxisMode,
            CreateMaterialResolveOptions(settings.MaterialDirectory),
            CreateAnimationPath(settings.AnimationPath)));

        switch (settings.OutputFormat)
        {
            case "obj":
                Directory.CreateDirectory(outputPath);
                new OBJExporter().Export(model, outputPath, baseName, new OBJExportOptions());
                return $"Wrote OBJ output to {outputPath}";

            case "glb":
            case "gltf":
                Directory.CreateDirectory(outputPath);
                new GLTFExporter().Export(
                    model,
                    outputPath,
                    baseName,
                    new GLTFExportOptions(settings.OutputFormat is "glb"));
                return $"Wrote {(settings.OutputFormat is "glb" ? "GLB" : "glTF")} output to {outputPath}";

            case "source":
            case "mdl":
                Directory.CreateDirectory(outputPath);
                new MDLExporter().Export(
                    model,
                    outputPath,
                    baseName,
                    new MDLExportOptions(
                        settings.ModelPath ?? $"gmconverter/{SanitizePathToken(baseName)}.mdl",
                        settings.StudioMdlPath,
                        settings.VtfCmdPath,
                        settings.BuildMaterials,
                        CreatePhysicsOptions(settings)));
                return $"Wrote Source compile workspace to {outputPath}";

            default:
                throw new GMConverterException("Unsupported output format.");
        }
    }

    public PreviewLoadResult LoadPreview(ConversionSettings settings)
    {
        var inputPath = RequireInputFile(settings.InputPath, settings.InputFormat);
        using var loggerFactory = CreateLoggerFactory();
        var importer = CreateImporter(settings.InputFormat, loggerFactory);
        var model = importer.Parse(inputPath, new ModelParseOptions(
            settings.ScaleFactor,
            settings.AxisMode,
            CreateMaterialResolveOptions(settings.MaterialDirectory),
            CreateAnimationPath(settings.AnimationPath)));

        var previewDirectory = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "Preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewDirectory);

        var baseName = string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : settings.BaseName;

        baseName = SanitizePathToken(baseName);
        new GLTFExporter().Export(model, previewDirectory, baseName, new GLTFExportOptions(true));

        var physicsPreview = ExportPhysicsPreview(settings, model, previewDirectory, baseName);

        return new PreviewLoadResult(
            PreviewSummary.From(model, physicsPreview.PartCount),
            Path.Combine(previewDirectory, baseName + ".glb"),
            physicsPreview.ModelPath);
    }

    public static ModelAxisMode NormalizeAxisMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "" or "auto" => ModelAxisMode.Auto,
            "z" or "z-up" or "zup" => ModelAxisMode.ZUp,
            "y" or "y-up" or "yup" => ModelAxisMode.YUp,
            _ => throw new GMConverterException("Unsupported input axis mode.")
        };
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new UiLoggerProvider(logSink));
        });
    }

    private static IImporter CreateImporter(string inputFormat, ILoggerFactory? loggerFactory = null)
    {
        return inputFormat switch
        {
            "opt" => new OPTImporter(),
            "mdl" => new MDLImporter(),
            "psk" => new PSKImporter(),
            "mow" => new MOWImporter(loggerFactory),
            _ => throw new InvalidOperationException($"Unsupported input format: {inputFormat}")
        };
    }

    private static string RequireInputFile(string path, string inputFormat)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var extension = Path.GetExtension(fullPath);
        var allowedExtensions = inputFormat switch
        {
            "psk" => [".psk", ".pskx", ".ue4scene"],
            "mow" => [".def", ".mdl"],
            _ => new[] { $".{inputFormat}" }
        };

        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"File not found: {fullPath}");
        }

        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a {string.Join(" or ", allowedExtensions)} file: {fullPath}");
        }

        return fullPath;
    }

    private static string RequireOutputPath(string? path, string outputFormat)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GMConverterException($"Output path is required for {outputFormat} output.");
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static MaterialResolveOptions? CreateMaterialResolveOptions(string? materialDirectory)
    {
        if (string.IsNullOrWhiteSpace(materialDirectory))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(materialDirectory));
        if (!Directory.Exists(fullPath))
        {
            throw new GMConverterException($"Material directory not found: {fullPath}");
        }

        return new MaterialResolveOptions(fullPath);
    }

    private static PhysicsOptions? CreatePhysicsOptions(ConversionSettings settings)
    {
        if (!settings.GeneratePhysics && string.IsNullOrWhiteSpace(settings.PhysicsMode))
        {
            return null;
        }

        var mode = settings.PhysicsMode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "bounds" => PhysicsMode.Bounds,
            "coacd" => PhysicsMode.Coacd,
            _ => throw new GMConverterException("Unsupported physics mode.")
        };

        if (mode is PhysicsMode.Bounds)
        {
            return new PhysicsOptions(mode, settings.PhysicsMass, null);
        }

        return new PhysicsOptions(
            mode,
            settings.PhysicsMass,
            new CoacdOptions(settings.CoacdThreshold, settings.MaxConvexPieces, settings.MaxHullVertices));
    }

    private PhysicsPreviewExport ExportPhysicsPreview(ConversionSettings settings, Model model, string previewDirectory, string baseName)
    {
        if (!settings.GeneratePhysics)
        {
            return new PhysicsPreviewExport(null, 0);
        }

        var physicsMeshes = BuildPhysicsPreviewMeshes(model, settings);
        if (physicsMeshes.Count == 0)
        {
            return new PhysicsPreviewExport(null, 0);
        }

        var physicsBaseName = baseName + "_physics";
        var physicsModel = new Model(
            model.Name + " Physics",
            physicsMeshes,
            [new Material("physics")]);
        new GLTFExporter().Export(physicsModel, previewDirectory, physicsBaseName, new GLTFExportOptions(true));
        return new PhysicsPreviewExport(Path.Combine(previewDirectory, physicsBaseName + ".glb"), physicsMeshes.Count);
    }

    private IReadOnlyList<Mesh> BuildPhysicsPreviewMeshes(Model model, ConversionSettings settings)
    {
        return settings.PhysicsMode?.Trim().ToLowerInvariant() switch
        {
            "coacd" => BuildCoacdPhysicsPreviewMeshes(model, settings),
            _ => [CreateBoundsMesh(model.Bounds().WithMinimumThickness())]
        };
    }

    private IReadOnlyList<Mesh> BuildCoacdPhysicsPreviewMeshes(Model model, ConversionSettings settings)
    {
        var triangleCount = model.Meshes.Sum(mesh => mesh.Triangles.Count());
        if (triangleCount > _maxCoacdPreviewTriangles)
        {
            logSink.Append(
                $"Skipped CoACD physics preview for {triangleCount:N0} render triangles. " +
                $"Preview uses bounds above {_maxCoacdPreviewTriangles:N0} triangles; run conversion to build full CoACD physics.");
            return [CreateBoundsMesh(model.Bounds().WithMinimumThickness())];
        }

        logSink.Append($"Building CoACD physics preview from {triangleCount:N0} render triangles...");
        return CoacdNative.Decompose(
            model.Merge(),
            new CoacdDecompositionOptions(
                settings.CoacdThreshold,
                settings.MaxConvexPieces,
                settings.MaxHullVertices));
    }

    private static Mesh CreateBoundsMesh(Bounds bounds)
    {
        Vector3[] positions =
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

        var vertices = positions
            .Select(position => new Vertex(position, Vector3.UnitZ, Vector2.Zero))
            .ToArray();

        Triangle[] triangles =
        [
            new(0, 3, 2),
            new(0, 2, 1),
            new(4, 5, 6),
            new(4, 6, 7),
            new(0, 1, 5),
            new(0, 5, 4),
            new(3, 7, 6),
            new(3, 6, 2),
            new(0, 4, 7),
            new(0, 7, 3),
            new(1, 2, 6),
            new(1, 6, 5)
        ];

        return new Mesh(vertices, [new Submesh("physics", triangles)]);
    }

    private static string? CreateAnimationPath(string? animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(animationPath));
        if (!File.Exists(fullPath))
        {
            throw new GMConverterException($"Animation file not found: {fullPath}");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".psa", StringComparison.OrdinalIgnoreCase))
        {
            throw new GMConverterException($"Expected a .psa animation file: {fullPath}");
        }

        return fullPath;
    }

    private static string SanitizePathToken(string value)
    {
        return string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_')).Trim('_');
    }
}

internal sealed record PhysicsPreviewExport(string? ModelPath, int PartCount);

internal sealed record PreviewLoadResult(PreviewSummary Summary, string ModelPath, string? PhysicsModelPath);

internal sealed record PreviewSummary(
    int MeshCount,
    int SubmeshCount,
    int MaterialCount,
    int TextureCount,
    int VertexCount,
    int TriangleCount,
    int PhysicsPartCount)
{
    public static PreviewSummary From(Model model, int physicsPartCount)
    {
        return new PreviewSummary(
            model.Meshes.Count,
            model.Meshes.Sum(mesh => mesh.Submeshes.Count),
            model.Materials.Count,
            model.Textures.Count,
            model.Meshes.Sum(mesh => mesh.Vertices.Count),
            model.Meshes.Sum(mesh => mesh.Triangles.Count()),
            physicsPartCount);
    }

    public override string ToString()
    {
        return $"Meshes {MeshCount} | Submeshes {SubmeshCount} | Materials {MaterialCount} | Textures {TextureCount}\n" +
            $"Vertices {VertexCount} | Triangles {TriangleCount} | Physics {PhysicsPartCount}";
    }
}
