using GMConverter.SDK.Assets;

namespace GMConverter.Core.Pipeline;

/// <summary>
/// The conversion steps for one kind of asset. <see cref="AssetJobRunner"/> hands each request to
/// the pipeline for the kind its importer produces.
/// </summary>
internal interface IAssetPipeline
{
    AssetKind Kind { get; }

    AssetResult Run(AssetRequest request);
}
