using GMConverter.Core.Formats;
using GMConverter.SDK.Assets;
using GMConverter.SDK.Audio;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;

namespace GMConverter.Core.Pipeline.Sounds;

/// <summary>Imports a sound with a registered sound importer and writes it with the chosen sound exporter.</summary>
internal sealed class SoundPipeline : AssetPipeline<Sound>
{
    public override AssetKind Kind => AssetKind.Sound;

    public override Sound Import(AssetRequest request)
    {
        var importer = FormatCatalog.GetImporter(request.InputFormat) as ISoundImporter
            ?? throw new GMConverterException($"{request.InputFormat} input is not a sound.");
        var inputPath = FormatCatalog.RequireInputFile(request.InputPath, importer);
        using var scope = PerfTimer.Measure("pipeline", $"{importer.Format}.Import", inputPath);
        return importer.Import(new SoundImportContext(inputPath)
        {
            Options = WithDefaults(importer.OptionSchema, request.ImporterOptions),
        });
    }

    protected override ExportResult Export(Sound asset, AssetRequest request, AssetDestination destination)
    {
        var format = destination.Format;
        var exporter = (ISoundExporter)format.Exporter;
        var options = format.ApplyForcedOptions(WithDefaults(exporter.OptionSchema, request.ExporterOptions));
        var target = destination.Target;
        var targetOptions = target is null ? OptionValues.Empty : WithDefaults(target.OptionSchema, request.TargetOptions);
        Directory.CreateDirectory(destination.Directory);
        using var scope = PerfTimer.Measure("pipeline", $"{format.Format}.Export", destination.Directory);
        return exporter.Export(asset, new ExportContext(destination.Directory, destination.BaseName, options)
        {
            TargetGameId = target?.Id,
            TargetOptions = targetOptions,
        });
    }
}
