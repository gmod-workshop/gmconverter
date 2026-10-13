namespace GMConverter.Core.Pipeline;

/// <summary>
/// The conversion steps for one kind of asset. <see cref="AssetJobRunner"/> hands each request to
/// the first pipeline that can run it.
/// </summary>
internal interface IAssetPipeline
{
    bool CanRun(AssetRequest request);

    AssetResult Run(AssetRequest request);
}
