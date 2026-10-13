using GMConverter.Core.Formats;
using GMConverter.Core.Pipeline.Models;
using GMConverter.Core.Pipeline.Sounds;
using GMConverter.SDK.Common;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// Runs an <see cref="AssetJob"/>: each request goes through the pipeline for the kind of asset
/// its importer produces. The CLI and UI both convert through this class, so they share
/// defaults, validation and routing.
/// </summary>
internal sealed class AssetJobRunner
{
    private readonly IReadOnlyList<IAssetPipeline> _pipelines = [new ModelPipeline(), new SoundPipeline()];

    public AssetJobResult Run(AssetJob job)
    {
        return new AssetJobResult([.. job.Requests.Select(RunRequest)]);
    }

    private AssetResult RunRequest(AssetRequest request)
    {
        var kind = FormatCatalog.GetImporter(request.InputFormat).Kind;
        var pipeline = _pipelines.FirstOrDefault(pipeline => pipeline.Kind == kind)
            ?? throw new GMConverterException($"No pipeline converts {kind} assets.");
        return pipeline.Run(request);
    }
}
