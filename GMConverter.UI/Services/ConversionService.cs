using System.Numerics;
using GMConverter.Exporters;
using GMConverter.Plugins;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;

namespace GMConverter.UI.Services;

internal sealed class ConversionService(UiLogSink logSink)
{
    private int _perfLogAnnounced;

    private void AnnouncePerfLog()
    {
        if (Interlocked.Exchange(ref _perfLogAnnounced, 1) == 0)
        {
            logSink.Append($"Perf log: {PerfTimer.LogPath}");
        }
    }

    public string RunConversion(ConversionSettings settings)
    {
        using var scope = PerfTimer.Measure(
            "convert.run",
            "RunConversion",
            $"{settings.InputFormat}->{settings.OutputFormat}");
        AnnouncePerfLog();

        var inputPath = RequireInputFile(settings.InputPath, settings.InputFormat);
        var importer = CreateImporter(settings.InputFormat);

        if (settings.OutputFormat is "info")
        {
            return importer.Summarize(inputPath).ToString() ?? string.Empty;
        }

        var outputPath = RequireOutputPath(settings.OutputPath, settings.OutputFormat);
        var baseName = string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : settings.BaseName;
        Model model;
        using (PerfTimer.Measure("convert.run", $"{settings.InputFormat}.Parse", inputPath))
        {
            model = importer.Parse(inputPath, new ModelParseOptions(
                settings.ScaleFactor,
                settings.AxisMode,
                CreateMaterialResolveOptions(settings.MaterialDirectory))
            {
                Options = settings.ImporterOptions,
            });
        }

        var exporter = GetExporter(settings.OutputFormat);
        var options = settings.ExporterOptions;
        if (settings.OutputFormat is "glb" or "gltf")
        {
            var values = new Dictionary<string, object?>(options.AsDictionary())
            {
                ["binary"] = settings.OutputFormat == "glb"
            };
            options = new OptionValues(values);
        }
        Directory.CreateDirectory(outputPath);
        using (PerfTimer.Measure("convert.run", "Exporter.Export", settings.OutputFormat))
        {
            exporter.Export(model, outputPath, baseName, options);
        }
        return $"Wrote {settings.OutputFormat.ToUpperInvariant()} output to {outputPath}";
    }

    internal static IExporter GetExporter(string format)
    {
        return format switch
        {
            "obj" => new OBJExporter(),
            "glb" or "gltf" => new GLTFExporter(),
            _ => PluginHost.Registry.GetExporter(format == "source" ? "mdl" : format)
                ?? throw new GMConverterException($"Unsupported output format or plugin not loaded: {format}")
        };
    }

    public PreviewLoadResult LoadPreview(ConversionSettings settings)
    {
        using var scope = PerfTimer.Measure("convert.preview", "LoadPreview", settings.InputFormat);
        AnnouncePerfLog();

        var inputPath = RequireInputFile(settings.InputPath, settings.InputFormat);
        var importer = CreateImporter(settings.InputFormat);
        Model model;
        using (PerfTimer.Measure("convert.preview", $"{settings.InputFormat}.Parse", inputPath))
        {
            model = importer.Parse(inputPath, new ModelParseOptions(
                settings.ScaleFactor,
                settings.AxisMode,
                CreateMaterialResolveOptions(settings.MaterialDirectory))
            {
                Options = settings.ImporterOptions,
            });
        }

        var previewDirectory = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "Preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewDirectory);

        var baseName = string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(inputPath)
            : settings.BaseName;

        baseName = SanitizePathToken(baseName);
        using (PerfTimer.Measure("convert.preview", "GLTFExporter.Export"))
        {
            // BakeUvTransforms folds per-material BakedUv0Scale into the mesh's UVs in lieu of
            // KHR_texture_transform; the SharpEngine glTF importer used by the in-app preview does
            // not honor that extension, so without inline baking multi-layer Fortnite materials
            // sample the wrong tile of their bake and render as garbled textures.
            var previewOptions = new OptionValues(new Dictionary<string, object?>
            {
                ["binary"] = true,
                ["bakeUvTransforms"] = true,
            });
            new GLTFExporter().Export(model, previewDirectory, baseName, previewOptions);
        }

        PhysicsPreviewExport physicsPreview;
        using (PerfTimer.Measure("convert.preview", "ExportPhysicsPreview"))
        {
            physicsPreview = ExportPhysicsPreview(settings, model, previewDirectory, baseName);
        }

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

    private static IImporter CreateImporter(string inputFormat)
    {
        return FindImporter(inputFormat)
            ?? throw new GMConverterException($"Unsupported input format: {inputFormat}");
    }

    internal static IImporter? FindImporter(string inputFormat)
    {
        return PluginHost.Registry.GetImporter(inputFormat);
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

    private static PhysicsPreviewExport ExportPhysicsPreview(ConversionSettings settings, Model model, string previewDirectory, string baseName)
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
        var physicsGltfOptions = new OptionValues(new Dictionary<string, object?>
        {
            ["binary"] = true,
        });
        new GLTFExporter().Export(physicsModel, previewDirectory, physicsBaseName, physicsGltfOptions);
        return new PhysicsPreviewExport(Path.Combine(previewDirectory, physicsBaseName + ".glb"), physicsMeshes.Count);
    }

    private static IReadOnlyList<Mesh> BuildPhysicsPreviewMeshes(Model model, ConversionSettings settings)
    {
        // CoACD-based physics preview moved to the Source plugin (which owns CoacdNative). The
        // UI's preview path now shows bounds for both modes; the actual export still uses CoACD
        // when the user selects "coacd" mode. A follow-up could surface a plugin-contributed
        // "preview physics" hook so the UI can render the real shape pre-export, but it's not
        // currently in scope. The Mode string is still read so persistence/round-tripping works.
        _ = settings.PhysicsMode;
        return [CreateBoundsMesh(model.Bounds().WithMinimumThickness(0.0254f))];
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
