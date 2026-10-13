using GMConverter.SDK.Common;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// Runs an <see cref="AssetJob"/>: each request goes through the pipeline for its kind of asset.
/// The CLI and UI both convert through this class, so they share defaults, validation and routing.
/// </summary>
internal sealed class AssetJobRunner
{
    private readonly IReadOnlyList<IAssetPipeline> _pipelines = [new ModelPipeline()];

    public AssetJobResult Run(AssetJob job)
    {
        return new AssetJobResult([.. job.Requests.Select(RunRequest)]);
    }

    private AssetResult RunRequest(AssetRequest request)
    {
        var pipeline = _pipelines.FirstOrDefault(pipeline => pipeline.CanRun(request))
            ?? throw new GMConverterException($"No pipeline can convert {request.InputFormat} input.");
        return pipeline.Run(request);
    }
}
