using GMConverter.Core.Formats;
using GMConverter.SDK.Assets;
using GMConverter.SDK.Common;
using GMConverter.SDK.Exporters;
using GMConverter.SDK.Importers;
using GMConverter.SDK.Options;
using GMConverter.SDK.Particles;

namespace GMConverter.Core.Pipeline.Particles;

/// <summary>Imports a particle effect with a registered particle importer and writes it with the chosen particle exporter.</summary>
internal sealed class ParticlePipeline : AssetPipeline<ParticleEffect>
{
    public override AssetKind Kind => AssetKind.Particle;

    public override ParticleEffect Import(AssetRequest request)
    {
        var importer = FormatCatalog.GetImporter(request.InputFormat) as IParticleImporter
            ?? throw new GMConverterException($"{request.InputFormat} input is not a particle effect.");
        var inputPath = FormatCatalog.RequireInputFile(request.InputPath, importer);
        using var scope = PerfTimer.Measure("pipeline", $"{importer.Format}.Import", inputPath);
        return importer.Import(new ParticleImportContext(inputPath)
        {
            Options = WithDefaults(importer.OptionSchema, request.ImporterOptions),
        });
    }

    protected override ExportResult Export(ParticleEffect asset, AssetRequest request, AssetDestination destination)
    {
        var format = destination.Format;
        var exporter = (IParticleExporter)format.Exporter;
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
