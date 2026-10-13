using GMConverter.Core.Exporters;
using GMConverter.Core.Formats;
using GMConverter.Core.Pipeline;
using GMConverter.Core.Pipeline.Models;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Materials;
using GMConverter.SDK.Options;

namespace GMConverter.UI.Services;

internal sealed class ConversionService(UiLogSink logSink)
{
    private readonly AssetJobRunner _runner = new();
    private readonly ModelPipeline _modelPipeline = new();
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

        if (settings.OutputFormat is "info")
        {
            var importer = FormatCatalog.GetImporter(settings.InputFormat);
            return importer.Summarize(FormatCatalog.RequireInputFile(settings.InputPath, importer)).ToString() ?? string.Empty;
        }

        var result = _runner.Run(new AssetJob(CreateRequest(settings))).Results.Single();
        return $"Wrote {settings.OutputFormat.ToUpperInvariant()} output to {result.Destination.Directory}";
    }

    internal static IExporter GetExporter(string format)
    {
        return FormatCatalog.GetExport(format).Exporter;
    }

    public PreviewLoadResult LoadPreview(ConversionSettings settings)
    {
        using var scope = PerfTimer.Measure("convert.preview", "LoadPreview", settings.InputFormat);
        AnnouncePerfLog();

        var request = CreateRequest(settings);
        var model = _modelPipeline.Import(request);

        var previewDirectory = Path.Combine(Path.GetTempPath(), "GMConverter.UI", "Preview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewDirectory);

        var baseName = SanitizePathToken(string.IsNullOrWhiteSpace(settings.BaseName)
            ? Path.GetFileNameWithoutExtension(request.InputPath)
            : settings.BaseName);
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
            // The viewer shows the bind pose and never plays clips, and a model with hundreds of
            // bundled animations would otherwise write a several-times-larger preview file.
            new GLTFExporter().Export(model with { Animations = null }, new ExportContext(previewDirectory, baseName, previewOptions));
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

    internal static IImporter? FindImporter(string inputFormat)
    {
        return FormatCatalog.FindImporter(inputFormat);
    }

    private static AssetRequest CreateRequest(ConversionSettings settings)
    {
        return new AssetRequest(settings.InputFormat, settings.InputPath)
        {
            OutputFormat = settings.OutputFormat,
            OutputDirectory = settings.OutputPath,
            BaseName = settings.BaseName,
            ImporterOptions = settings.ImporterOptions,
            ExporterOptions = settings.ExporterOptions,
            ScaleFactor = settings.ScaleFactor,
            AxisMode = settings.AxisMode,
            MaterialDirectory = settings.MaterialDirectory,
            TargetGameId = settings.TargetGameId,
            TargetOptions = settings.TargetOptions,
        };
    }

    // Collision comes from the selected exporter, so the overlay shows exactly what that export
    // would generate (including CoACD hulls) without the host knowing the exporter's options.
    private static PhysicsPreviewExport ExportPhysicsPreview(ConversionSettings settings, Model model, string previewDirectory, string baseName)
    {
        if (FormatCatalog.FindExport(settings.OutputFormat)?.Exporter is not ICollisionPreview collision)
        {
            return new PhysicsPreviewExport(null, 0);
        }

        var physicsMeshes = collision.CreateCollisionPreview(model, settings.ExporterOptions);
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
        new GLTFExporter().Export(physicsModel, new ExportContext(previewDirectory, physicsBaseName, physicsGltfOptions));
        return new PhysicsPreviewExport(Path.Combine(previewDirectory, physicsBaseName + ".glb"), physicsMeshes.Count);
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
