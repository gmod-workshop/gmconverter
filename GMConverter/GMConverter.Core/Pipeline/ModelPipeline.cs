using GMConverter.SDK.Common;
using GMConverter.SDK.Geometry;
using GMConverter.SDK.Importers;

namespace GMConverter.Core.Pipeline;

/// <summary>Imports a model with a registered importer and writes it with the chosen exporter.</summary>
internal sealed class ModelPipeline : AssetPipeline<Model>
{
    public override bool CanRun(AssetRequest request)
    {
        return AssetFormats.FindImporter(request.InputFormat) is not null;
    }

    public override Model Import(AssetRequest request)
    {
        var importer = AssetFormats.GetImporter(request.InputFormat);
        var inputPath = AssetFormats.RequireInputFile(request.InputPath, importer);
        using var scope = PerfTimer.Measure("pipeline", $"{importer.InputFormat}.Parse", inputPath);
        return importer.Parse(inputPath, new ModelParseOptions(
            request.ScaleFactor,
            request.AxisMode,
            CreateMaterialResolveOptions(request.MaterialDirectory))
        {
            Options = WithDefaults(importer.OptionSchema, request.ImporterOptions),
        });
    }

    protected override void Export(Model asset, AssetRequest request, AssetDestination destination)
    {
        var format = destination.Format;
        var options = format.ApplyForcedOptions(WithDefaults(format.Exporter.OptionSchema, request.ExporterOptions));
        Directory.CreateDirectory(destination.Directory);
        using var scope = PerfTimer.Measure("pipeline", $"{format.Format}.Export", destination.Directory);
        format.Exporter.Export(asset, destination.Directory, destination.BaseName, options);
    }

    private static MaterialResolveOptions? CreateMaterialResolveOptions(string? materialDirectory)
    {
        if (string.IsNullOrWhiteSpace(materialDirectory))
        {
            return null;
        }

        var fullPath = AssetFormats.FullPath(materialDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new GMConverterException($"Material directory not found: {fullPath}");
        }

        return new MaterialResolveOptions(fullPath);
    }
}
