using GMConverter.Core.Formats;
using GMConverter.SDK.Assets;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;

namespace GMConverter.Core.Pipeline.Models;

/// <summary>Imports a model with a registered model importer and writes it with the chosen model exporter.</summary>
internal sealed class ModelPipeline : AssetPipeline<Model>
{
    public override AssetKind Kind => AssetKind.Model;

    public override Model Import(AssetRequest request)
    {
        var importer = FormatCatalog.GetImporter(request.InputFormat) as IModelImporter
            ?? throw new GMConverterException($"{request.InputFormat} input is not a model.");
        var inputPath = FormatCatalog.RequireInputFile(request.InputPath, importer);
        using var scope = PerfTimer.Measure("pipeline", $"{importer.Format}.Import", inputPath);
        return importer.Import(new ModelImportContext(
            inputPath,
            request.ScaleFactor,
            request.AxisMode,
            CreateMaterialResolveOptions(request.MaterialDirectory))
        {
            Options = WithDefaults(importer.OptionSchema, request.ImporterOptions),
        });
    }

    protected override ExportResult Export(Model asset, AssetRequest request, AssetDestination destination)
    {
        var format = destination.Format;
        var exporter = (IModelExporter)format.Exporter;
        var options = format.ApplyForcedOptions(WithDefaults(exporter.OptionSchema, request.ExporterOptions));
        Directory.CreateDirectory(destination.Directory);
        using var scope = PerfTimer.Measure("pipeline", $"{format.Format}.Export", destination.Directory);
        return exporter.Export(asset, new ExportContext(destination.Directory, destination.BaseName, options));
    }

    private static MaterialResolveOptions? CreateMaterialResolveOptions(string? materialDirectory)
    {
        if (string.IsNullOrWhiteSpace(materialDirectory))
        {
            return null;
        }

        var fullPath = FormatCatalog.FullPath(materialDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new GMConverterException($"Material directory not found: {fullPath}");
        }

        return new MaterialResolveOptions(fullPath);
    }
}
